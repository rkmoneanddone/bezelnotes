import { onRequest } from "firebase-functions/v2/https";
import { initializeApp } from "firebase-admin/app";
import { getAuth } from "firebase-admin/auth";
import {
  FieldValue,
  Timestamp,
  getFirestore,
} from "firebase-admin/firestore";

initializeApp();

const db = getFirestore();
const TRIAL_DAYS = 7;

type VerifiedUser = {
  uid: string;
  email: string;
  creationTime: Date;
};

async function verifyBearer(request: any): Promise<VerifiedUser> {
  const authorization = request.get("authorization") || "";

  if (!authorization.startsWith("Bearer ")) {
    throw new Error("UNAUTHENTICATED");
  }

  const idToken = authorization.substring("Bearer ".length).trim();

  const decoded = await getAuth().verifyIdToken(idToken, true);
  const user = await getAuth().getUser(decoded.uid);

  const creationTime = new Date(user.metadata.creationTime);

  if (Number.isNaN(creationTime.getTime())) {
    throw new Error("ACCOUNT_CREATION_TIME_UNAVAILABLE");
  }

  return {
    uid: user.uid,
    email: user.email ?? "",
    creationTime,
  };
}

function requireInstallationId(value: unknown): string {
  if (typeof value !== "string" ||
      !/^[a-f0-9]{32}$/i.test(value)) {
    throw new Error("INVALID_INSTALLATION_ID");
  }

  return value;
}

export const bootstrapAccount = onRequest(
  {
    region: "asia-south1",
    cors: false,
    invoker: "public",
  },
  async (request, response) => {
    if (request.method !== "POST") {
      response.status(405).json({ error: "METHOD_NOT_ALLOWED" });
      return;
    }

    try {
      const user = await verifyBearer(request);

      const installationId =
        requireInstallationId(request.body?.installationId);

      const userRef = db.collection("users").doc(user.uid);
      const entitlementRef =
        db.collection("entitlements").doc(user.uid);

      const installationRef =
        db.collection("installations").doc(installationId);

      const result = await db.runTransaction(async (transaction) => {
        const entitlementSnapshot =
          await transaction.get(entitlementRef);

        const now = Timestamp.now();

        let trialStartedAt: Timestamp;
        let trialEndsAt: Timestamp;
        let entitlementState: string;
        let premiumEnabled = false;
        let planCode = "none";

        if (!entitlementSnapshot.exists) {
          // Firebase Auth creation time is authoritative.
          // Reinstalling the desktop app cannot reset the trial.
          trialStartedAt =
            Timestamp.fromDate(user.creationTime);

          trialEndsAt =
            Timestamp.fromMillis(
              user.creationTime.getTime() +
              TRIAL_DAYS * 24 * 60 * 60 * 1000);

          entitlementState =
            now.toMillis() < trialEndsAt.toMillis()
              ? "trial"
              : "expired";

          transaction.create(entitlementRef, {
            uid: user.uid,
            trialStartedAt,
            trialEndsAt,
            entitlementState,
            premiumEnabled: false,
            planCode: "none",
            createdAt: FieldValue.serverTimestamp(),
            updatedAt: FieldValue.serverTimestamp(),
          });
        } else {
          const data = entitlementSnapshot.data() ?? {};

          trialStartedAt =
            data.trialStartedAt instanceof Timestamp
              ? data.trialStartedAt
              : Timestamp.fromDate(user.creationTime);

          trialEndsAt =
            data.trialEndsAt instanceof Timestamp
              ? data.trialEndsAt
              : Timestamp.fromMillis(
                  user.creationTime.getTime() +
                  TRIAL_DAYS * 24 * 60 * 60 * 1000);

          entitlementState =
            typeof data.entitlementState === "string"
              ? data.entitlementState
              : "expired";

          premiumEnabled =
            data.premiumEnabled === true;

          planCode =
            typeof data.planCode === "string"
              ? data.planCode
              : "none";
        }

        transaction.set(
          userRef,
          {
            uid: user.uid,
            email: user.email,
            updatedAt: FieldValue.serverTimestamp(),
          },
          { merge: true });

        transaction.set(
          installationRef,
          {
            installationId,
            uid: user.uid,
            platform:
              typeof request.body?.platform === "string"
                ? request.body.platform.substring(0, 32)
                : "Windows",
            osVersion:
              typeof request.body?.osVersion === "string"
                ? request.body.osVersion.substring(0, 128)
                : "",
            appVersion:
              typeof request.body?.appVersion === "string"
                ? request.body.appVersion.substring(0, 32)
                : "",
            lastSeenAt: FieldValue.serverTimestamp(),
          },
          { merge: true });

        const remainingMs =
          Math.max(
            0,
            trialEndsAt.toMillis() - now.toMillis());

        const trialDaysRemaining =
          entitlementState === "trial"
            ? Math.ceil(
                remainingMs / (24 * 60 * 60 * 1000))
            : 0;

        return {
          userId: user.uid,
          email: user.email,
          entitlementState,
          trialStartedAtUtc:
            trialStartedAt.toDate().toISOString(),
          trialEndsAtUtc:
            trialEndsAt.toDate().toISOString(),
          trialDaysRemaining,
          premiumEnabled,
          planCode,
        };
      });

      response.set("Cache-Control", "no-store");
      response.status(200).json(result);
    } catch (error: any) {
      const code = error?.message ?? "INTERNAL_ERROR";

      if (code === "UNAUTHENTICATED") {
        response.status(401).json({ error: code });
        return;
      }

      if (code === "INVALID_INSTALLATION_ID") {
        response.status(400).json({ error: code });
        return;
      }

      console.error("bootstrapAccount failed", error);
      response.status(500).json({ error: "INTERNAL_ERROR" });
    }
  });

