import {
  FieldValue,
  Timestamp,
  type DocumentReference,
  type Firestore,
} from "firebase-admin/firestore";
import {
  defineSecret,
} from "firebase-functions/params";
import {
  onRequest,
} from "firebase-functions/v2/https";
import {
  Webhook,
} from "standardwebhooks";

const DODO_PAYMENTS_WEBHOOK_SECRET =
  defineSecret(
    "DODO_PAYMENTS_WEBHOOK_SECRET");

type DodoEvent = {
  business_id?: string;
  type?: string;
  timestamp?: string;
  data?: Record<string, any>;
};

function safeString(
  value: unknown,
  max = 500,
): string {
  return typeof value === "string"
    ? value.trim().substring(0, max)
    : "";
}

function toTimestamp(
  value: unknown,
): Timestamp | null {
  if (typeof value !== "string") {
    return null;
  }

  const date =
    new Date(value);

  return Number.isNaN(
    date.getTime())
    ? null
    : Timestamp.fromDate(date);
}

function metadataValue(
  data: Record<string, any>,
  key: string,
): string {
  const metadata =
    data.metadata &&
    typeof data.metadata === "object"
      ? data.metadata as
          Record<string, unknown>
      : {};

  return safeString(
    metadata[key],
    256);
}

function eventTime(
  event: DodoEvent,
): Timestamp {
  return toTimestamp(
    event.timestamp) ??
    Timestamp.now();
}

function shouldApplyEvent(
  existing: FirebaseFirestore.DocumentData | undefined,
  incoming: Timestamp,
): boolean {
  const previous =
    existing?.providerEventAt;

  return !(previous instanceof Timestamp) ||
    incoming.toMillis() >=
      previous.toMillis();
}

async function findPaymentRef(
  db: Firestore,
  data: Record<string, any>,
): Promise<DocumentReference | null> {
  const metadataPaymentId =
    metadataValue(
      data,
      "bezel_payment_id");

  if (metadataPaymentId) {
    return db.collection("payments")
      .doc(metadataPaymentId);
  }

  const subscriptionId =
    safeString(
      data.subscription_id,
      256);

  if (subscriptionId) {
    const bySubscription =
      await db.collection("payments")
        .where(
          "dodoSubscriptionId",
          "==",
          subscriptionId)
        .limit(1)
        .get();

    if (!bySubscription.empty) {
      return bySubscription.docs[0].ref;
    }
  }

  const paymentId =
    safeString(
      data.payment_id,
      256);

  if (paymentId) {
    const byProviderPayment =
      await db.collection("payments")
        .where(
          "dodoPaymentId",
          "==",
          paymentId)
        .limit(1)
        .get();

    if (!byProviderPayment.empty) {
      return byProviderPayment.docs[0].ref;
    }
  }

  return null;
}

function isSubscriptionEvent(
  type: string,
): boolean {
  return type.startsWith(
    "subscription.");
}

function statusForPaymentEvent(
  type: string,
): string | null {
  switch (type) {
    case "payment.succeeded":
      return "confirmed";
    case "payment.failed":
      return "failed";
    case "payment.processing":
      return "processing";
    case "payment.cancelled":
      return "cancelled";
    case "refund.succeeded":
      return "refunded";
    case "refund.failed":
      return "refund_failed";
    default:
      return null;
  }
}

function entitlementPatchForSubscription(
  type: string,
  data: Record<string, any>,
  payment:
    FirebaseFirestore.DocumentData,
  incoming: Timestamp,
): Record<string, unknown> {
  const planCode =
    metadataValue(
      data,
      "bezel_plan_code") ||
    safeString(
      payment.planCode,
      32) ||
    "none";

  const subscriptionId =
    safeString(
      data.subscription_id,
      256) ||
    safeString(
      payment.dodoSubscriptionId,
      256);

  const nextBilling =
    toTimestamp(
      data.next_billing_date);

  const expiresAt =
    toTimestamp(
      data.expires_at);

  const periodEnd =
    nextBilling ??
    expiresAt ??
    null;

  const common:
    Record<string, unknown> = {
      planCode,
      provider: "dodo",
      providerSubscriptionId:
        subscriptionId,
      providerEventAt:
        incoming,
      updatedAt:
        FieldValue.serverTimestamp(),
  };

  if (periodEnd) {
    common.currentPeriodEnd =
      periodEnd;
  }

  switch (type) {
    case "subscription.active":
    case "subscription.renewed":
      return {
        ...common,
        entitlementState: "active",
        premiumEnabled: true,
        cancelAtPeriodEnd:
          data.cancel_at_next_billing_date === true,
      };

    case "subscription.updated": {
      const status =
        safeString(
          data.status,
          32).toLowerCase();

      if (
        status === "active" ||
        status === "renewed"
      ) {
        return {
          ...common,
          entitlementState: "active",
          premiumEnabled: true,
          cancelAtPeriodEnd:
            data.cancel_at_next_billing_date === true,
        };
      }

      if (
        status === "on_hold" ||
        status === "past_due"
      ) {
        return {
          ...common,
          entitlementState: "grace",
          premiumEnabled: true,
        };
      }

      return {
        ...common,
        cancelAtPeriodEnd:
          data.cancel_at_next_billing_date === true,
      };
    }

    case "subscription.on_hold":
      return {
        ...common,
        entitlementState: "grace",
        premiumEnabled: true,
      };

    case "subscription.cancelled":
      return {
        ...common,
        entitlementState: "active",
        premiumEnabled: true,
        cancelAtPeriodEnd: true,
      };

    case "subscription.failed":
    case "subscription.expired":
      return {
        ...common,
        entitlementState: "expired",
        premiumEnabled: false,
        cancelAtPeriodEnd: false,
      };

    case "subscription.paused":
      return {
        ...common,
        entitlementState: "grace",
        premiumEnabled: true,
      };

    case "subscription.plan_changed":
      return {
        ...common,
        entitlementState: "active",
        premiumEnabled: true,
        cancelAtPeriodEnd:
          data.cancel_at_next_billing_date === true,
      };

    default:
      return common;
  }
}

export function createDodoWebhook(
  db: Firestore,
) {
  return onRequest(
    {
      region: "asia-south1",
      cors: false,
      invoker: "public",
      secrets: [
        DODO_PAYMENTS_WEBHOOK_SECRET,
      ],
      timeoutSeconds: 15,
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

      const webhookId =
        safeString(
          request.header("webhook-id"),
          256);

      const webhookSignature =
        safeString(
          request.header(
            "webhook-signature"),
          2048);

      const webhookTimestamp =
        safeString(
          request.header(
            "webhook-timestamp"),
          64);

      if (
        !webhookId ||
        !webhookSignature ||
        !webhookTimestamp
      ) {
        response.status(400).json({
          error:
            "MISSING_WEBHOOK_HEADERS",
        });
        return;
      }

      let event: DodoEvent;

      try {
        const secret =
          DODO_PAYMENTS_WEBHOOK_SECRET
            .value();

        if (!secret) {
          throw new Error(
            "WEBHOOK_SECRET_MISSING");
        }

        const rawBody =
          request.rawBody;

        const verifier =
          new Webhook(secret);

        event =
          verifier.verify(
            rawBody,
            {
              "webhook-id":
                webhookId,
              "webhook-signature":
                webhookSignature,
              "webhook-timestamp":
                webhookTimestamp,
            }) as DodoEvent;
      }
      catch (error) {
        console.error(
          "Dodo webhook signature rejected",
          error);

        response.status(401).json({
          error: "INVALID_SIGNATURE",
        });
        return;
      }

      const type =
        safeString(
          event.type,
          80);

      const data =
        event.data &&
        typeof event.data === "object"
          ? event.data
          : {};

      if (!type) {
        response.status(400).json({
          error: "INVALID_EVENT",
        });
        return;
      }

      const eventRef =
        db.collection(
          "paymentWebhookEvents")
          .doc(webhookId);

      const existingEvent =
        await eventRef.get();

      if (existingEvent.exists) {
        response.status(200).json({
          received: true,
          duplicate: true,
        });
        return;
      }

      const paymentRef =
        await findPaymentRef(
          db,
          data);

      /*
       * Recognized subscription events must correlate to a Bezel
       * checkout before acknowledgement. Returning 500 lets Dodo
       * retry if an earlier payment event has not arrived yet.
       */
      if (
        isSubscriptionEvent(type) &&
        !paymentRef
      ) {
        console.error(
          "Uncorrelated subscription event",
          webhookId,
          type);

        response.status(500).json({
          error:
            "PAYMENT_CORRELATION_PENDING",
        });
        return;
      }

      const incoming =
        eventTime(event);

      await db.runTransaction(
        async (transaction) => {
          const eventSnapshot =
            await transaction.get(
              eventRef);

          if (eventSnapshot.exists) {
            return;
          }

          let payment:
            FirebaseFirestore.DocumentData =
              {};

          if (paymentRef) {
            const paymentSnapshot =
              await transaction.get(
                paymentRef);

            if (paymentSnapshot.exists) {
              payment =
                paymentSnapshot.data() ??
                {};
            }
          }

          const paymentStatus =
            statusForPaymentEvent(
              type);

          if (
            paymentRef &&
            paymentStatus &&
            shouldApplyEvent(
              payment,
              incoming)
          ) {
            const paymentPatch:
              Record<string, unknown> = {
                status:
                  paymentStatus,
                providerEventAt:
                  incoming,
                updatedAt:
                  FieldValue.serverTimestamp(),
            };

            const dodoPaymentId =
              safeString(
                data.payment_id,
                256);

            const dodoSubscriptionId =
              safeString(
                data.subscription_id,
                256);

            if (dodoPaymentId) {
              paymentPatch.dodoPaymentId =
                dodoPaymentId;
              paymentPatch.gatewayReference =
                dodoPaymentId;
            }

            if (dodoSubscriptionId) {
              paymentPatch.dodoSubscriptionId =
                dodoSubscriptionId;
            }

            if (
              type === "payment.failed"
            ) {
              paymentPatch.failureReason =
                safeString(
                  data.error_message,
                  300) ||
                safeString(
                  data.error_code,
                  120) ||
                "Payment failed";
            }

            transaction.set(
              paymentRef,
              paymentPatch,
              { merge: true });
          }

          if (
            paymentRef &&
            isSubscriptionEvent(type)
          ) {
            const uid =
              metadataValue(
                data,
                "bezel_uid") ||
              safeString(
                payment.uid,
                160);

            if (!uid) {
              throw new Error(
                "MISSING_BEZEL_UID");
            }

            const entitlementRef =
              db.collection(
                "entitlements")
                .doc(uid);

            const entitlementSnapshot =
              await transaction.get(
                entitlementRef);

            const entitlement =
              entitlementSnapshot.exists
                ? entitlementSnapshot.data()
                : undefined;

            if (
              shouldApplyEvent(
                entitlement,
                incoming)
            ) {
              const patch =
                entitlementPatchForSubscription(
                  type,
                  data,
                  payment,
                  incoming);

              transaction.set(
                entitlementRef,
                {
                  uid,
                  ...patch,
                },
                { merge: true });
            }

            transaction.set(
              paymentRef,
              {
                dodoSubscriptionId:
                  safeString(
                    data.subscription_id,
                    256) ||
                  safeString(
                    payment.dodoSubscriptionId,
                    256),
                subscriptionStatus:
                  safeString(
                    data.status,
                    64) ||
                  type.substring(
                    "subscription.".length),
                updatedAt:
                  FieldValue.serverTimestamp(),
              },
              { merge: true });
          }

          transaction.create(
            eventRef,
            {
              webhookId,
              type,
              businessId:
                safeString(
                  event.business_id,
                  256),
              providerTimestamp:
                incoming,
              paymentDocumentId:
                paymentRef?.id ?? "",
              receivedAt:
                FieldValue.serverTimestamp(),
              processed: true,
            });
        });

      response.status(200).json({
        received: true,
      });
    });
}