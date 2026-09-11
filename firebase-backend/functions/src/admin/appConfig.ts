import { FieldValue } from "firebase-admin/firestore";
import { onRequest } from "firebase-functions/v2/https";
import {
  boundedInteger,
  requirePostMethod,
} from "./mutationRules";
import {
  normalizePaymentConfig,
} from "../payments/paymentConfig";

type AdminIdentity = {
  uid: string;
};

type AdminAppConfigDeps = {
  db: FirebaseFirestore.Firestore;
  requireAdmin: (request: any) => Promise<AdminIdentity>;
  sendAdminError: (response: any, error: any) => void;
  safeText: (value: unknown, maxLength: number) => string;
};

export function createAdminAppConfigHandler(
  deps: AdminAppConfigDeps
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

        const hrs =
          boundedInteger(
            request.body?.clientRefreshHours,
            48,
            12,
            168);

        const update: Record<string, unknown> = {
          latestVersion:
            safeText(
              request.body?.latestVersion,
              32),

          minimumVersion:
            safeText(
              request.body?.minimumVersion,
              32),

          updateMessage:
            safeText(
              request.body?.updateMessage,
              500),

          updateUrl:
            safeText(
              request.body?.updateUrl,
              500),

          forceUpdate:
            request.body?.forceUpdate === true,

          paymentsEnabled:
            request.body?.paymentsEnabled === true,

          cloudSyncEnabled:
            request.body?.cloudSyncEnabled === true,

          clientRefreshHours:
            hrs,

          updatedAt:
            FieldValue.serverTimestamp(),

          updatedByUid:
            admin.uid,
        };

        // Payment config contains only safe operational values:
        // prices, currencies, enabled flags, provider routing,
        // and public Razorpay/Dodo plan/product IDs.
        //
        // Gateway keys and webhook secrets MUST remain in
        // Firebase / Google Secret Manager and are never accepted
        // through this admin endpoint.
        if (
          request.body?.paymentConfig &&
          typeof request.body.paymentConfig === "object"
        ) {
          update.paymentConfig =
            normalizePaymentConfig(
              request.body.paymentConfig);
        }

        await db.collection("config")
          .doc("app")
          .set(
            update,
            { merge: true });

        response.status(200).json({
          ok: true,
        });
      }
      catch (error: any) {
        sendAdminError(response, error);
      }
    });
}