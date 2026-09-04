import { FieldValue } from "firebase-admin/firestore";
import { onRequest } from "firebase-functions/v2/https";
import {
  boundedInteger,
  boundedPositiveMoneyCents,
  requirePostMethod,
} from "./mutationRules";

type AdminIdentity = {
  uid: string;
};

type AdminPricingDeps = {
  db: FirebaseFirestore.Firestore;
  requireAdmin: (request: any) => Promise<AdminIdentity>;
  sendAdminError: (response: any, error: any) => void;
  safeText: (value: unknown, maxLength: number) => string;
};

type PricingValues = {
  trialDays: number;
  monthlyPriceCents: number;
  yearlyPriceCents: number;
  currency: string;
  monthlyPriceProtectionMonths: number;
};

function samePricingValues(
  current: FirebaseFirestore.DocumentData | undefined,
  next: PricingValues
): boolean {
  if (!current) {
    return false;
  }

  return (
    Number(current.trialDays) === next.trialDays &&
    Number(current.monthlyPriceCents) ===
      next.monthlyPriceCents &&
    Number(current.yearlyPriceCents) ===
      next.yearlyPriceCents &&
    String(current.currency || "").toUpperCase() ===
      next.currency &&
    Number(current.monthlyPriceProtectionMonths) ===
      next.monthlyPriceProtectionMonths
  );
}

export function createAdminPricingHandler(
  deps: AdminPricingDeps
) {
  const {
    db,
    requireAdmin,
    sendAdminError,
    safeText,
  } = deps;

  return onRequest(
    {
      region: "asia-south1",
      invoker: "public",
      cors: false,
    },
    async (request, response) => {
      response.set("Cache-Control", "no-store");

      if (!requirePostMethod(request, response)) {
        return;
      }

      try {
        const admin =
          await requireAdmin(request);

        const pricingValues: PricingValues = {
          trialDays:
            boundedInteger(
              request.body?.trialDays,
              7,
              1,
              90),

          monthlyPriceCents:
            boundedPositiveMoneyCents(
              request.body?.monthlyPriceCents,
              149),

          yearlyPriceCents:
            boundedPositiveMoneyCents(
              request.body?.yearlyPriceCents,
              599),

          currency:
            (
              safeText(
                request.body?.currency,
                8) || "USD"
            ).toUpperCase(),

          monthlyPriceProtectionMonths:
            boundedInteger(
              request.body?.monthlyPriceProtectionMonths,
              12,
              1,
              36),
        };

        const pricingRef =
          db.collection("config")
            .doc("pricing");

        const result =
          await db.runTransaction(
            async (transaction) => {
              const currentSnapshot =
                await transaction.get(pricingRef);

              const current =
                currentSnapshot.exists
                  ? currentSnapshot.data()
                  : undefined;

              // Reliability rule:
              // identical retries are successful no-ops.
              if (samePricingValues(
                    current,
                    pricingValues)) {
                return {
                  changed: false,
                  historyId: null as string | null,
                };
              }

              const history =
                db.collection("pricingHistory")
                  .doc();

              const pricing = {
                ...pricingValues,
                updatedAt:
                  FieldValue.serverTimestamp(),
                updatedByUid:
                  admin.uid,
              };

              transaction.set(
                pricingRef,
                pricing,
                {merge: true});

              transaction.set(
                history,
                {
                  ...pricing,
                  createdAt:
                    FieldValue.serverTimestamp(),
                });

              return {
                changed: true,
                historyId: history.id as string | null,
              };
            });

        response.status(200).json({
          ok: true,
          historyId: result.historyId,
          unchanged: !result.changed,
        });
      }
      catch (error: any) {
        sendAdminError(response, error);
      }
    });
}