import { createHash } from "node:crypto";
import { defineSecret } from "firebase-functions/params";
import { onRequest } from "firebase-functions/v2/https";
import {
  FieldValue,
  type Firestore,
} from "firebase-admin/firestore";

import {
  normalizePaymentConfig,
  type PaymentPlanCode,
} from "./paymentConfig";

type VerifiedUser = {
  uid: string;
  email: string;
  creationTime: Date;
};

type VerifyBearer =
  (request: any) => Promise<VerifiedUser>;

type DodoCheckoutResponse = {
  session_id?: string;
  checkout_url?: string;
};

const dodoPaymentsApiKey =
  defineSecret("DODO_PAYMENTS_API_KEY");

function safeRequestId(value: unknown): string {
  if (
    typeof value !== "string" ||
    !/^[A-Za-z0-9._:-]{16,128}$/.test(value)
  ) {
    throw new Error("INVALID_REQUEST_ID");
  }

  return value;
}

function safePlanCode(
  value: unknown
): PaymentPlanCode {
  if (
    value === "six_month" ||
    value === "yearly"
  ) {
    return value;
  }

  throw new Error("INVALID_PLAN");
}

function paymentDocumentId(
  uid: string,
  requestId: string
): string {
  return createHash("sha256")
    .update(
      `${uid}:${requestId}`,
      "utf8")
    .digest("hex");
}

function dodoCheckoutEndpoint(
  environment: "test" | "live"
): string {
  return environment === "live"
    ? "https://live.dodopayments.com/checkouts"
    : "https://test.dodopayments.com/checkouts";
}

function isPlaceholderProductId(
  value: string
): boolean {
  return (
    !value ||
    value.startsWith("dodo_test_")
  );
}

export function createPaymentCheckoutHandler(
  db: Firestore,
  verifyBearer: VerifyBearer
) {
  return onRequest(
    {
      region: "asia-south1",
      invoker: "public",
      cors: false,
      secrets: [dodoPaymentsApiKey],
      timeoutSeconds: 30,
      memory: "256MiB",
    },
    async (request, response) => {
      response.set("Cache-Control", "no-store");

      if (request.method !== "POST") {
        response.status(405).json({
          error: "METHOD_NOT_ALLOWED",
        });
        return;
      }

      try {
        const user =
          await verifyBearer(request);

        const planCode =
          safePlanCode(
            request.body?.planCode);

        const requestId =
          safeRequestId(
            request.body?.requestId);

        const appSnapshot =
          await db.collection("config")
            .doc("app")
            .get();

        const appData =
          appSnapshot.exists
            ? appSnapshot.data() ?? {}
            : {};

        const paymentConfig =
          normalizePaymentConfig(
            appData.paymentConfig);

        if (
          appData.paymentsEnabled !== true ||
          paymentConfig.enabled !== true
        ) {
          throw new Error("PAYMENTS_DISABLED");
        }

        // Bezel uses one global Dodo price/product pair for all markets.
        // The international branch is the canonical source so the client
        // cannot influence provider or price by claiming a country.
        const plan =
          planCode === "six_month"
            ? paymentConfig.sixMonth.international
            : paymentConfig.yearly.international;

        if (
          plan.enabled !== true ||
          plan.provider !== "dodo"
        ) {
          throw new Error("PLAN_DISABLED");
        }

        if (
          plan.currency !== "USD" ||
          !Number.isInteger(plan.amountMinor) ||
          plan.amountMinor <= 0
        ) {
          throw new Error("INVALID_PAYMENT_CONFIG");
        }

        if (
          isPlaceholderProductId(
            plan.providerProductId)
        ) {
          throw new Error(
            "PAYMENT_PRODUCT_NOT_CONFIGURED");
        }

        const paymentId =
          paymentDocumentId(
            user.uid,
            requestId);

        const paymentRef =
          db.collection("payments")
            .doc(paymentId);

        const existing =
          await paymentRef.get();

        if (existing.exists) {
          const data =
            existing.data() ?? {};

          const existingUrl =
            typeof data.checkoutUrl === "string"
              ? data.checkoutUrl
              : "";

          const existingSessionId =
            typeof data.checkoutSessionId === "string"
              ? data.checkoutSessionId
              : "";

          if (
            existingUrl &&
            existingSessionId
          ) {
            response.status(200).json({
              paymentId,
              checkoutUrl: existingUrl,
              checkoutSessionId:
                existingSessionId,
              duplicateRetry: true,
            });
            return;
          }

          throw new Error(
            "CHECKOUT_ALREADY_IN_PROGRESS");
        }

        // Reserve this checkout request before calling Dodo.
        // A repeated requestId cannot create a second payment document.
        await paymentRef.create({
          uid: user.uid,
          email: user.email,
          planCode,
          provider: "dodo",
          environment:
            paymentConfig.environment,
          providerProductId:
            plan.providerProductId,
          amountCents:
            plan.amountMinor,
          currency:
            plan.currency,
          status: "creating",
          requestId,
          createdAt:
            FieldValue.serverTimestamp(),
          updatedAt:
            FieldValue.serverTimestamp(),
        });

        const apiKey =
          dodoPaymentsApiKey.value();

        if (!apiKey) {
          await paymentRef.set(
            {
              status: "checkout_failed",
              failureReason:
                "DODO_API_KEY_NOT_CONFIGURED",
              updatedAt:
                FieldValue.serverTimestamp(),
            },
            { merge: true });

          throw new Error(
            "PAYMENT_PROVIDER_NOT_CONFIGURED");
        }

        const endpoint =
          dodoCheckoutEndpoint(
            paymentConfig.environment);

        const dodoResponse =
          await fetch(
            endpoint,
            {
              method: "POST",
              headers: {
                "Authorization":
                  `Bearer ${apiKey}`,
                "Content-Type":
                  "application/json",
              },
              body: JSON.stringify({
                product_cart: [
                  {
                    product_id:
                      plan.providerProductId,
                    quantity: 1,
                  },
                ],
                customer: {
                  email: user.email,
                },
                metadata: {
                  bezel_payment_id:
                    paymentId,
                  bezel_uid:
                    user.uid,
                  bezel_plan_code:
                    planCode,
                },
                return_url:
                  paymentConfig.checkoutReturnUrl,
              }),
            });

        let dodoJson:
          DodoCheckoutResponse & {
            message?: unknown;
            error?: unknown;
          } = {};

        try {
          dodoJson =
            await dodoResponse.json() as
              DodoCheckoutResponse & {
                message?: unknown;
                error?: unknown;
              };
        }
        catch {
          dodoJson = {};
        }

        if (
          !dodoResponse.ok ||
          typeof dodoJson.checkout_url !== "string" ||
          typeof dodoJson.session_id !== "string"
        ) {
          console.error(
            "Dodo checkout creation failed",
            {
              status: dodoResponse.status,
              paymentId,
              planCode,
              environment:
                paymentConfig.environment,
            });

          await paymentRef.set(
            {
              status: "checkout_failed",
              failureReason:
                `DODO_HTTP_${dodoResponse.status}`,
              updatedAt:
                FieldValue.serverTimestamp(),
            },
            { merge: true });

          throw new Error(
            "CHECKOUT_PROVIDER_FAILED");
        }

        await paymentRef.set(
          {
            status: "pending",
            checkoutSessionId:
              dodoJson.session_id,
            checkoutUrl:
              dodoJson.checkout_url,
            updatedAt:
              FieldValue.serverTimestamp(),
          },
          { merge: true });

        response.status(200).json({
          paymentId,
          checkoutUrl:
            dodoJson.checkout_url,
          checkoutSessionId:
            dodoJson.session_id,
          duplicateRetry: false,
        });
      }
      catch (error: any) {
        const code =
          error?.message ??
          "INTERNAL_ERROR";

        if (code === "UNAUTHENTICATED") {
          response.status(401).json({
            error: code,
          });
          return;
        }

        if (
          code === "INVALID_REQUEST_ID" ||
          code === "INVALID_PLAN"
        ) {
          response.status(400).json({
            error: code,
          });
          return;
        }

        if (
          code === "PAYMENTS_DISABLED" ||
          code === "PLAN_DISABLED"
        ) {
          response.status(403).json({
            error: code,
          });
          return;
        }

        if (
          code === "PAYMENT_PRODUCT_NOT_CONFIGURED" ||
          code === "PAYMENT_PROVIDER_NOT_CONFIGURED"
        ) {
          response.status(503).json({
            error: code,
          });
          return;
        }

        if (
          code === "CHECKOUT_ALREADY_IN_PROGRESS"
        ) {
          response.status(409).json({
            error: code,
          });
          return;
        }

        if (
          code === "INVALID_PAYMENT_CONFIG" ||
          code === "CHECKOUT_PROVIDER_FAILED"
        ) {
          response.status(502).json({
            error: code,
          });
          return;
        }

        console.error(
          "createPaymentCheckout failed",
          error);

        response.status(500).json({
          error: "INTERNAL_ERROR",
        });
      }
    });
}