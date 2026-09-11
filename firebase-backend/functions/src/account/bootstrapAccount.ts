import { onRequest } from "firebase-functions/v2/https";
import {
  FieldValue,
  Timestamp,
  type Firestore,
} from "firebase-admin/firestore";

import { normalizePaymentConfig } from "../payments/paymentConfig";

type VerifiedUser = {
  uid: string;
  email: string;
  creationTime: Date;
};

type VerifyBearer =
  (request: any) => Promise<VerifiedUser>;

const TRIAL_DAYS = 7;

export function createBootstrapAccount(
  db: Firestore,
  verifyBearer: VerifyBearer
) {
function requireInstallationId(value: unknown): string {
  if (typeof value !== "string" ||
      !/^[a-f0-9]{32}$/i.test(value)) {
    throw new Error("INVALID_INSTALLATION_ID");
  }

  return value;
}

  return onRequest(
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
          // Trial config is read only once, when entitlement is first created.
          const pricingSnapshot =
            await transaction.get(
              db.collection("config").doc("pricing"));

          const configuredTrialDays =
            pricingSnapshot.exists &&
            Number.isInteger(pricingSnapshot.data()?.trialDays)
              ? Number(pricingSnapshot.data()?.trialDays)
              : TRIAL_DAYS;

          const trialDaysAtSignup =
            Math.min(90, Math.max(1, configuredTrialDays));

          trialStartedAt =
            Timestamp.fromDate(user.creationTime);

          trialEndsAt =
            Timestamp.fromMillis(
              user.creationTime.getTime() +
              trialDaysAtSignup * 24 * 60 * 60 * 1000);

          entitlementState =
            now.toMillis() < trialEndsAt.toMillis()
              ? "trial"
              : "expired";

          transaction.create(entitlementRef, {
            uid: user.uid,
            trialStartedAt,
            trialEndsAt,
            trialDaysAtSignup,
            entitlementState,
            premiumEnabled: false,
            planCode: "none",
            createdAt: FieldValue.serverTimestamp(),
            updatedAt: FieldValue.serverTimestamp(),
          });
        } else {
          const data = entitlementSnapshot.data() ?? {};

          const storedTrialDaysAtSignup =
            Number.isInteger(data.trialDaysAtSignup)
              ? Math.min(
                  90,
                  Math.max(
                    1,
                    Number(data.trialDaysAtSignup)))
              : TRIAL_DAYS;

          trialStartedAt =
            data.trialStartedAt instanceof Timestamp
              ? data.trialStartedAt
              : Timestamp.fromDate(user.creationTime);

          trialEndsAt =
            data.trialEndsAt instanceof Timestamp
              ? data.trialEndsAt
              : Timestamp.fromMillis(
                  trialStartedAt.toMillis() +
                  storedTrialDaysAtSignup *
                    24 * 60 * 60 * 1000);

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

          // Server authority: an unpaid trial cannot remain active
          // after its authoritative trialEndsAt timestamp.
          if (
            !premiumEnabled &&
            entitlementState === "trial" &&
            now.toMillis() >= trialEndsAt.toMillis()
          ) {
            entitlementState = "expired";

            transaction.set(
              entitlementRef,
              {
                entitlementState: "expired",
                updatedAt:
                  FieldValue.serverTimestamp(),
              },
              { merge: true });
          }
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

      const [
        pricingSnapshot,
        appConfigSnapshot,
        notificationSnapshot,
      ] = await Promise.all([
        db.collection("config").doc("pricing").get(),
        db.collection("config").doc("app").get(),
        db.collection("notifications")
          .where("active", "==", true)
          .limit(20)
          .get(),
      ]);

      const pricingData =
        pricingSnapshot.exists
          ? pricingSnapshot.data() ?? {}
          : {};

      const appConfigData =
        appConfigSnapshot.exists
          ? appConfigSnapshot.data() ?? {}
          : {};

      const paymentConfig =
        normalizePaymentConfig(
          appConfigData.paymentConfig);
      const nowMs = Date.now();

      const activeNotification: any =
        notificationSnapshot.docs
          .map((doc): any => ({
            id: doc.id,
            ...(doc.data() as Record<string, any>),
          }))
          .filter((item: any) => {
            const starts =
              item.startAt instanceof Timestamp
                ? item.startAt.toMillis()
                : 0;

            const expires =
              item.expiresAt instanceof Timestamp
                ? item.expiresAt.toMillis()
                : Number.MAX_SAFE_INTEGER;

            return starts <= nowMs && expires >= nowMs;
          })
          .sort((a: any, b: any) => {
            const aStart =
              a.startAt instanceof Timestamp
                ? a.startAt.toMillis()
                : 0;

            const bStart =
              b.startAt instanceof Timestamp
                ? b.startAt.toMillis()
                : 0;

            return bStart - aStart;
          })[0] ?? null;

      response.set("Cache-Control", "no-store");
      response.status(200).json({
        ...result,
        serverRefreshedAtUtc:
          new Date().toISOString(),
        pricing: {
          trialDays:
            Number.isInteger(pricingData.trialDays)
              ? Number(pricingData.trialDays)
              : 7,
          monthlyPriceCents:
            Number.isInteger(pricingData.monthlyPriceCents)
              ? Number(pricingData.monthlyPriceCents)
              : 149,
          yearlyPriceCents:
            Number.isInteger(pricingData.yearlyPriceCents)
              ? Number(pricingData.yearlyPriceCents)
              : 599,
          currency:
            typeof pricingData.currency === "string"
              ? pricingData.currency
              : "USD",
          monthlyPriceProtectionMonths:
            Number.isInteger(
              pricingData.monthlyPriceProtectionMonths)
              ? Number(
                  pricingData.monthlyPriceProtectionMonths)
              : 12,
        },
        appConfig: {
          latestVersion:
            typeof appConfigData.latestVersion === "string"
              ? appConfigData.latestVersion
              : "",
          minimumVersion:
            typeof appConfigData.minimumVersion === "string"
              ? appConfigData.minimumVersion
              : "",
          updateMessage:
            typeof appConfigData.updateMessage === "string"
              ? appConfigData.updateMessage.substring(0, 500)
              : "",
          updateUrl:
            typeof appConfigData.updateUrl === "string"
              ? appConfigData.updateUrl.substring(0, 500)
              : "",
          forceUpdate:
            appConfigData.forceUpdate === true,
          paymentsEnabled:
            appConfigData.paymentsEnabled === true,          cloudSyncEnabled:
            appConfigData.cloudSyncEnabled === true,
          clientRefreshHours:
            Number.isFinite(
              Number(appConfigData.clientRefreshHours))
              ? Math.min(
                  168,
                  Math.max(
                    1,
                    Number(appConfigData.clientRefreshHours)))
              : 48,
        },
        paymentConfig,
        notification:
          activeNotification
            ? {
                id: activeNotification.id,
                title:
                  typeof activeNotification.title === "string"
                    ? activeNotification.title
                    : "",
                message:
                  typeof activeNotification.message === "string"
                    ? activeNotification.message
                    : "",
                type:
                  typeof activeNotification.type === "string"
                    ? activeNotification.type
                    : "info",
                startAtUtc:
                  activeNotification.startAt instanceof Timestamp
                    ? activeNotification.startAt
                        .toDate()
                        .toISOString()
                    : null,
                expiresAtUtc:
                  activeNotification.expiresAt instanceof Timestamp
                    ? activeNotification.expiresAt
                        .toDate()
                        .toISOString()
                    : null,
              }
            : null,
      });
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
}
