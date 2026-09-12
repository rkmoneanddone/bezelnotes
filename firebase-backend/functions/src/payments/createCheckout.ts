import { createHash } from "node:crypto";
import {
  FieldValue,
  type Firestore,
} from "firebase-admin/firestore";
import {
  defineSecret,
} from "firebase-functions/params";
import {
  onRequest,
} from "firebase-functions/v2/https";

import {
  normalizePaymentConfig,
  type PaymentMarket,
  type PaymentPlanCode,
  type PaymentPlanConfig,
} from "./paymentConfig";

type VerifiedUser = {
  uid: string;
  email: string;
  creationTime: Date;
};

type VerifyBearer =
  (request: any) => Promise<VerifiedUser>;

const DODO_PAYMENTS_API_KEY =
  defineSecret("DODO_PAYMENTS_API_KEY");

function requirePlanCode(
  value: unknown,
): PaymentPlanCode {
  if (
    value === "six_month" ||
    value === "yearly"
  ) {
    return value;
  }

  throw new Error("INVALID_PLAN");
}

function requireMarket(
  value: unknown,
): PaymentMarket {
  if (
    value === "india" ||
    value === "international"
  ) {
    return value;
  }

  throw new Error("INVALID_MARKET");
}

function requireRequestId(
  value: unknown,
): string {
  if (
    typeof value !== "string" ||
    !/^[A-Za-z0-9._:-]{16,128}$/.test(value)
  ) {
    throw new Error("INVALID_REQUEST_ID");
  }

  return value;
}

function getPlan(
  planCode: PaymentPlanCode,
  market: PaymentMarket,
  paymentConfig:
    ReturnType<typeof normalizePaymentConfig>,
): PaymentPlanConfig {
  const group =
    planCode === "six_month"
      ? paymentConfig.sixMonth
      : paymentConfig.yearly;

  return market === "india"
    ? group.india
    : group.international;
}

function isUsableProductId(
  value: string,
): boolean {
  return /^pdt_[A-Za-z0-9]+$/.test(value);
}

function paymentDocumentId(
  uid: string,
  requestId: string,
): string {
  return createHash("sha256")
    .update(`${uid}:${requestId}`)
    .digest("hex");
}

function checkoutEndpoint(
  environment: "test" | "live",
): string {
  return environment === "live"
    ? "https://live.dodopayments.com/checkouts"
    : "https://test.dodopayments.com/checkouts";
}

export function createPaymentCheckoutHandler(
  db: Firestore,
  verifyBearer: VerifyBearer,
) {
  return onRequest(
    {
      region: "asia-south1",
      cors: false,
      invoker: "public",
      secrets: [
        DODO_PAYMENTS_API_KEY,
      ],
      timeoutSeconds: 30,
    },
    async (request, response) => {
      response.set(
        "Cache-Control",
        "no-store");

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
          requirePlanCode(
            request.body?.planCode);

        const market =
          requireMarket(
            request.body?.market);

        const requestId =
          requireRequestId(
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

        /*
         * paymentConfig.enabled is the payment-system switch.
         * appConfig.paymentsEnabled remains a desktop/UI switch.
         * Either explicit false disables checkout.
         */
        if (
          paymentConfig.enabled !== true ||
          appData.paymentsEnabled === false
        ) {
          throw new Error(
            "PAYMENTS_DISABLED");
        }

        const plan =
          getPlan(
            planCode,
            market,
            paymentConfig);

        if (
          plan.enabled !== true ||
          plan.provider !== "dodo" ||
          !isUsableProductId(
            plan.providerProductId)
        ) {
          throw new Error(
            "PLAN_NOT_AVAILABLE");
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

          const checkoutUrl =
            typeof data.checkoutUrl === "string"
              ? data.checkoutUrl
              : "";

          if (checkoutUrl) {
            response.status(200).json({
              paymentId,
              checkoutUrl,
              sessionId:
                typeof data.checkoutSessionId === "string"
                  ? data.checkoutSessionId
                  : "",
              reused: true,
            });
            return;
          }

          if (data.status === "creating") {
            response.status(409).json({
              error: "CHECKOUT_CREATING",
            });
            return;
          }
        }

        await paymentRef.set(
          {
            uid: user.uid,
            email: user.email,
            requestId,
            planCode,
            market,
            provider: "dodo",
            environment:
              paymentConfig.environment,
            providerProductId:
              plan.providerProductId,
            amountMinor:
              plan.amountMinor,
            amountCents:
              plan.amountMinor,
            currency:
              plan.currency,
            status: "creating",
            createdAt:
              FieldValue.serverTimestamp(),
            updatedAt:
              FieldValue.serverTimestamp(),
          },
          { merge: true });

        const apiKey =
          DODO_PAYMENTS_API_KEY.value();

        if (!apiKey) {
          throw new Error(
            "DODO_API_KEY_NOT_CONFIGURED");
        }

        const dodoResponse =
          await fetch(
            checkoutEndpoint(
              paymentConfig.environment),
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
                  bezel_market:
                    market,
                  bezel_request_id:
                    requestId,
                },
                return_url:
                  paymentConfig.checkoutReturnUrl,
              }),
            });

        const rawBody =
          await dodoResponse.text();

        let dodoData:
          Record<string, unknown> = {};

        if (rawBody) {
          try {
            dodoData =
              JSON.parse(rawBody) as
                Record<string, unknown>;
          }
          catch {
            dodoData = {};
          }
        }

        if (!dodoResponse.ok) {
          const reason =
            typeof dodoData.message === "string"
              ? dodoData.message.substring(0, 300)
              : `DODO_HTTP_${dodoResponse.status}`;

          await paymentRef.set(
            {
              status: "failed",
              failureReason: reason,
              updatedAt:
                FieldValue.serverTimestamp(),
            },
            { merge: true });

          throw new Error(
            "DODO_CHECKOUT_FAILED");
        }

        const checkoutUrl =
          typeof dodoData.checkout_url === "string"
            ? dodoData.checkout_url
            : "";

        const sessionId =
          typeof dodoData.session_id === "string"
            ? dodoData.session_id
            : "";

        if (
          !checkoutUrl ||
          !sessionId
        ) {
          await paymentRef.set(
            {
              status: "failed",
              failureReason:
                "INVALID_DODO_CHECKOUT_RESPONSE",
              updatedAt:
                FieldValue.serverTimestamp(),
            },
            { merge: true });

          throw new Error(
            "INVALID_DODO_CHECKOUT_RESPONSE");
        }

        await paymentRef.set(
          {
            status: "pending",
            checkoutUrl,
            checkoutSessionId:
              sessionId,
            updatedAt:
              FieldValue.serverTimestamp(),
          },
          { merge: true });

        response.status(200).json({
          paymentId,
          checkoutUrl,
          sessionId,
          reused: false,
        });
      }
      catch (error: any) {
        const code =
          error?.message ??
          "INTERNAL_ERROR";

        const status =
          code === "UNAUTHENTICATED"
            ? 401
            : code === "INVALID_PLAN" ||
              code === "INVALID_MARKET" ||
              code === "INVALID_REQUEST_ID"
              ? 400
              : code === "PAYMENTS_DISABLED" ||
                code === "PLAN_NOT_AVAILABLE"
                ? 409
                : 500;

        console.error(
          "createPaymentCheckout failed",
          code,
          error);

        response.status(status).json({
          error:
            status === 500
              ? "CHECKOUT_FAILED"
              : code,
        });
      }
    });
}