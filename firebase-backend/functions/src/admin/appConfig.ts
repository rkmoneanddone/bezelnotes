import { FieldValue } from "firebase-admin/firestore";
import { onRequest } from "firebase-functions/v2/https";
import {
  boundedInteger,
  requirePostMethod,
} from "./mutationRules";

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
        // Rule: authorization before authoritative write.
        const admin =
          await requireAdmin(request);

        const hrs =
          boundedInteger(
            request.body?.clientRefreshHours,
            48,
            12,
            168);

        // Rule: one authoritative document, merge semantics.
        await db.collection("config")
          .doc("app")
          .set(
            {
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
            },
            {merge: true});

        response.status(200).json({
          ok: true,
        });
      }
      catch (error: any) {
        // Rule: centralized structured error mapping.
        sendAdminError(response, error);
      }
    });
}