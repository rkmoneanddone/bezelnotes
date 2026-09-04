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
        // Rule: authorization before authoritative writes.
        const admin =
          await requireAdmin(request);

        // Rule: server-side bounded validation.
        const pricing = {
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

          updatedAt:
            FieldValue.serverTimestamp(),

          updatedByUid:
            admin.uid,
        };

        const history =
          db.collection("pricingHistory")
            .doc();

        // Rule: current pricing + pricing history must succeed/fail together.
        const batch = db.batch();

        batch.set(
          db.collection("config").doc("pricing"),
          pricing,
          {merge: true});

        batch.set(
          history,
          {
            ...pricing,
            createdAt:
              FieldValue.serverTimestamp(),
          });

        await batch.commit();

        response.status(200).json({
          ok: true,
          historyId: history.id,
        });
      }
      catch (error: any) {
        // Rule: centralized structured error mapping.
        sendAdminError(response, error);
      }
    });
}