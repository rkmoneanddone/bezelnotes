import { onRequest } from "firebase-functions/v2/https";
import { getAuth } from "firebase-admin/auth";
import {
  FieldValue,
  Timestamp,
  type Firestore,
} from "firebase-admin/firestore";

type RequireAdmin =
  (request: any) => Promise<any>;

type SendAdminError =
  (response: any, error: any) => void;

export function createLegacyAdminHandlers(options: {
  db: Firestore;
  requireAdmin: RequireAdmin;
  sendAdminError: SendAdminError;
}) {
  const {
    db,
    requireAdmin,
    sendAdminError,
  } = options;
  const getAdminOverview = onRequest(
  {
    region: "asia-south1",
    invoker: "public",
    cors: false,
  },
  async (request, response) => {
    response.set("Cache-Control", "no-store");

    if (request.method !== "GET") {
      response.status(405).json({error: "METHOD_NOT_ALLOWED"});
      return;
    }

    try {
      await requireAdmin(request);

      const states = ["trial", "active", "grace", "expired"];

      const [
        usersCount,
        installationsCount,
        ...stateCounts
      ] = await Promise.all([
        db.collection("users").count().get(),
        db.collection("installations").count().get(),
        ...states.map(
          (state) =>
            db.collection("entitlements")
              .where("entitlementState", "==", state)
              .count()
              .get()),
      ]);

      const entitlements: Record<string, number> = {};

      states.forEach((state, index) => {
        entitlements[state] = stateCounts[index].data().count;
      });

      response.status(200).json({
        users: usersCount.data().count,
        installations: installationsCount.data().count,
        entitlements,
      });
    }
    catch (error: any) {
      sendAdminError(response, error);
    }
  });

  const listAdminUsers = onRequest(
  {
    region: "asia-south1",
    invoker: "public",
    cors: false,
  },
  async (request, response) => {
    response.set("Cache-Control", "no-store");

    if (request.method !== "GET") {
      response.status(405).json({error: "METHOD_NOT_ALLOWED"});
      return;
    }

    try {
      await requireAdmin(request);

      const page = await getAuth().listUsers(100);
      const refs =
        page.users.map(
          (u) => db.collection("entitlements").doc(u.uid));

      const docs =
        refs.length > 0
          ? await db.getAll(...refs)
          : [];

      const byUid = new Map<string, any>();

      docs.forEach((doc) => {
        byUid.set(doc.id, doc.exists ? doc.data() : null);
      });

      const users = page.users.map((user) => {
        const entitlement = byUid.get(user.uid);

        return {
          uid: user.uid,
          email: user.email ?? "",
          disabled: user.disabled,
          createdAtUtc: user.metadata.creationTime ?? null,
          lastSignInAtUtc: user.metadata.lastSignInTime ?? null,
          entitlementState:
            typeof entitlement?.entitlementState === "string"
              ? entitlement.entitlementState
              : "none",
          trialEndsAtUtc:
            entitlement?.trialEndsAt instanceof Timestamp
              ? entitlement.trialEndsAt.toDate().toISOString()
              : null,
          premiumEnabled: entitlement?.premiumEnabled === true,
          planCode:
            typeof entitlement?.planCode === "string"
              ? entitlement.planCode
              : "none",
        };
      });

      response.status(200).json({users});
    }
    catch (error: any) {
      sendAdminError(response, error);
    }
  });

  const getAdminUser = onRequest(
  {
    region: "asia-south1",
    invoker: "public",
    cors: false,
  },
  async (request, response) => {
    response.set("Cache-Control", "no-store");

    if (request.method !== "GET") {
      response.status(405).json({error: "METHOD_NOT_ALLOWED"});
      return;
    }

    try {
      await requireAdmin(request);

      const uid =
        typeof request.query.uid === "string"
          ? request.query.uid.trim()
          : "";

      if (uid.length < 10 || uid.length > 128) {
        throw new Error("INVALID_REQUEST");
      }

      const [authUser, entitlementDoc, installs] =
        await Promise.all([
          getAuth().getUser(uid),
          db.collection("entitlements").doc(uid).get(),
          db.collection("installations")
            .where("uid", "==", uid)
            .limit(25)
            .get(),
        ]);

      const entitlement =
        entitlementDoc.exists ? entitlementDoc.data() : null;

      response.status(200).json({
        user: {
          uid: authUser.uid,
          email: authUser.email ?? "",
          disabled: authUser.disabled,
          createdAtUtc: authUser.metadata.creationTime ?? null,
          lastSignInAtUtc: authUser.metadata.lastSignInTime ?? null,
        },
        entitlement:
          entitlement
            ? {
                entitlementState: entitlement.entitlementState ?? "none",
                premiumEnabled: entitlement.premiumEnabled === true,
                planCode: entitlement.planCode ?? "none",
                trialStartedAtUtc:
                  entitlement.trialStartedAt instanceof Timestamp
                    ? entitlement.trialStartedAt.toDate().toISOString()
                    : null,
                trialEndsAtUtc:
                  entitlement.trialEndsAt instanceof Timestamp
                    ? entitlement.trialEndsAt.toDate().toISOString()
                    : null,
              }
            : null,
        installations:
          installs.docs.map((doc) => {
            const data = doc.data();

            return {
              installationId: data.installationId ?? doc.id,
              platform: data.platform ?? "",
              osVersion: data.osVersion ?? "",
              appVersion: data.appVersion ?? "",
              lastSeenAtUtc:
                data.lastSeenAt instanceof Timestamp
                  ? data.lastSeenAt.toDate().toISOString()
                  : null,
            };
          }),
      });
    }
    catch (error: any) {
      sendAdminError(response, error);
    }
  });

const adminConfigKeys =
  new Set([
    "latestVersion",
    "minimumVersion",
    "premiumEnabled",
    "googleDriveEnabled",
    "oneDriveEnabled",
    "maintenanceMessage",
  ]);

  const getAdminConfig = onRequest(
  {
    region: "asia-south1",
    invoker: "public",
    cors: false,
  },
  async (request, response) => {
    response.set("Cache-Control", "no-store");

    if (request.method !== "GET") {
      response.status(405).json({error: "METHOD_NOT_ALLOWED"});
      return;
    }

    try {
      await requireAdmin(request);

      const doc =
        await db.collection("config").doc("app").get();

      response.status(200).json({
        config:
          doc.exists
            ? doc.data()
            : {
                latestVersion: "",
                minimumVersion: "",
                premiumEnabled: false,
                googleDriveEnabled: false,
                oneDriveEnabled: false,
                maintenanceMessage: "",
              },
      });
    }
    catch (error: any) {
      sendAdminError(response, error);
    }
  });

  const updateAdminConfig = onRequest(
  {
    region: "asia-south1",
    invoker: "public",
    cors: false,
  },
  async (request, response) => {
    response.set("Cache-Control", "no-store");

    if (request.method !== "POST") {
      response.status(405).json({error: "METHOD_NOT_ALLOWED"});
      return;
    }

    try {
      const admin = await requireAdmin(request);

      const incoming =
        request.body && typeof request.body === "object"
          ? request.body
          : {};

      const patch: Record<string, unknown> = {};

      for (const [key, value] of Object.entries(incoming)) {
        if (!adminConfigKeys.has(key)) {
          continue;
        }

        if (key.endsWith("Enabled") && typeof value === "boolean") {
          patch[key] = value;
          continue;
        }

        if (typeof value === "string") {
          patch[key] = value.substring(0, 500);
        }
      }

      if (Object.keys(patch).length === 0) {
        throw new Error("INVALID_REQUEST");
      }

      patch.updatedAt = FieldValue.serverTimestamp();
      patch.updatedByUid = admin.uid;

      await db.collection("config").doc("app").set(
        patch,
        {merge: true});

      response.status(200).json({ok: true});
    }
    catch (error: any) {
      sendAdminError(response, error);
    }
  });
  return {
    getAdminOverview,
    listAdminUsers,
    getAdminUser,
    getAdminConfig,
    updateAdminConfig,
  };
}