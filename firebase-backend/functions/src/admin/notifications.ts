import { createHash } from "node:crypto";
import { FieldValue } from "firebase-admin/firestore";
import { onRequest } from "firebase-functions/v2/https";

type AdminIdentity = {
  uid: string;
};

type AdminNotificationDeps = {
  db: FirebaseFirestore.Firestore;
  requireAdmin: (request: any) => Promise<AdminIdentity>;
  sendAdminError: (response: any, error: any) => void;
  safeText: (value: unknown, maxLength: number) => string;
  isoFromTimestamp: (value: unknown) => string | null;
};

function normalizeOptionalUtc(
  value: unknown,
  safeText: (value: unknown, maxLength: number) => string
): string {
  const text =
    safeText(value, 64).trim();

  if (!text) {
    return "";
  }

  // Admin UI sends Date.toISOString(), which is explicit UTC.
  // Do not accept local/ambiguous timestamps without a Z suffix.
  const utcIsoPattern =
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z$/;

  if (!utcIsoPattern.test(text)) {
    throw new Error("INVALID_REQUEST");
  }

  const millis =
    Date.parse(text);

  if (!Number.isFinite(millis)) {
    throw new Error("INVALID_REQUEST");
  }

  return new Date(millis).toISOString();
}
export function createAdminNotificationHandlers(
  deps: AdminNotificationDeps
) {
  const {
    db,
    requireAdmin,
    sendAdminError,
    safeText,
    isoFromTimestamp,
  } = deps;

  const listAdminNotificationsV2 = onRequest(
    {
      region: "asia-south1",
      invoker: "public",
      cors: false,
    },
    async (request, response) => {
      response.set("Cache-Control", "no-store");

      if (request.method !== "GET") {
        response.status(405).json({
          error: "METHOD_NOT_ALLOWED",
        });
        return;
      }

      try {
        await requireAdmin(request);

        const snap =
          await db.collection("notifications")
            .orderBy("createdAt", "desc")
            .limit(50)
            .get();

        response.status(200).json({
          notifications:
            snap.docs.map((doc) => {
              const data = doc.data();

              return {
                id: doc.id,
                title:
                  safeText(data.title, 100),
                message:
                  safeText(data.message, 500),
                type:
                  safeText(data.type, 24) || "info",
                active:
                  data.active === true,
                startAtUtc:
                  safeText(data.startAtUtc, 64),
                expiresAtUtc:
                  safeText(data.expiresAtUtc, 64),
                createdAtUtc:
                  isoFromTimestamp(data.createdAt),
              };
            }),
        });
      }
      catch (error: any) {
        sendAdminError(response, error);
      }
    });

  const saveAdminNotificationV2 = onRequest(
    {
      region: "asia-south1",
      invoker: "public",
      cors: false,
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
        const admin =
          await requireAdmin(request);

        const id =
          safeText(request.body?.id, 160);

        const notificationRequestId =
          safeText(request.body?.requestId, 128);
        const title =
          safeText(request.body?.title, 100);
        const message =
          safeText(request.body?.message, 500);
        const type =
          safeText(request.body?.type, 24);

        if (
          title.length < 2 ||
          message.length < 2 ||
          !["info", "update", "warning"].includes(type)
        ) {
          throw new Error("INVALID_REQUEST");
        }

        const startAtUtc =
          normalizeOptionalUtc(
            request.body?.startAtUtc,
            safeText);

        const expiresAtUtc =
          normalizeOptionalUtc(
            request.body?.expiresAtUtc,
            safeText);

        if (
          startAtUtc &&
          expiresAtUtc &&
          Date.parse(startAtUtc) >= Date.parse(expiresAtUtc)
        ) {
          throw new Error("INVALID_REQUEST");
        }
        const payload = {
          title,
          message,
          type,
          active:
            request.body?.active === true,
          startAtUtc,
          expiresAtUtc,
          updatedAt:
            FieldValue.serverTimestamp(),
          updatedByUid:
            admin.uid,
        };

        if (id) {
          await db.collection("notifications")
            .doc(id)
            .set(
              payload,
              {merge: true});

          response.status(200).json({
            ok: true,
            id,
          });
          return;
        }

        if (
          !/^[A-Za-z0-9._:-]{16,128}$/.test(
            notificationRequestId)
        ) {
          throw new Error("INVALID_REQUEST");
        }

        const deterministicId =
          createHash("sha256")
            .update(
              `${admin.uid}:${notificationRequestId}`,
              "utf8")
            .digest("hex");

        const ref =
          db.collection("notifications")
            .doc(deterministicId);

        const result =
          await db.runTransaction(
            async (transaction) => {
              const existing =
                await transaction.get(ref);

              if (existing.exists) {
                return {
                  created: false,
                };
              }

              transaction.create(
                ref,
                {
                  ...payload,
                  requestId:
                    notificationRequestId,
                  createdAt:
                    FieldValue.serverTimestamp(),
                  createdByUid:
                    admin.uid,
                });

              return {
                created: true,
              };
            });

        response.status(200).json({
          ok: true,
          id: ref.id,
          duplicateRetry:
            !result.created,
        });
      }
      catch (error: any) {
        sendAdminError(response, error);
      }
    });

  return {
    listAdminNotificationsV2,
    saveAdminNotificationV2,
  };
}