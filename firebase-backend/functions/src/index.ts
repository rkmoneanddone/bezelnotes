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

// ============================================================
// Google OAuth code exchange
// Secret stays in Google Secret Manager / Firebase Functions.
// The desktop client never receives GOOGLE_OAUTH_CLIENT_SECRET.
// ============================================================

import { defineSecret as defineGoogleOAuthSecret } from "firebase-functions/params";
import { onRequest as onGoogleOAuthRequest } from "firebase-functions/v2/https";

const googleOAuthClientSecret =
  defineGoogleOAuthSecret("GOOGLE_OAUTH_CLIENT_SECRET");

const googleOAuthClientId =
  "1042233953411-plqv1idc18ocainkkomip33cqc60ape9.apps.googleusercontent.com";

const firebaseWebApiKey =
  "AIzaSyAwuAYA0PcDoc1HtZDD4-Wcb5DDeaRuILE";

type GoogleCodeExchangeRequest = {
  code?: string;
  codeVerifier?: string;
  redirectUri?: string;
};

type FirebaseIdpResponse = {
  localId?: string;
  email?: string;
  idToken?: string;
  refreshToken?: string;
  expiresIn?: string;
  error?: {
    message?: string;
  };
};

export const exchangeGoogleCode =
  onGoogleOAuthRequest(
    {
      region: "asia-south1",
      invoker: "public",
      secrets: [googleOAuthClientSecret],
      timeoutSeconds: 30,
      memory: "256MiB",
    },
    async (request, response) => {
      response.set("Cache-Control", "no-store");

      if (request.method !== "POST") {
        response
          .status(405)
          .json({ error: "method_not_allowed" });
        return;
      }

      const body =
        (request.body ?? {}) as GoogleCodeExchangeRequest;

      const code =
        typeof body.code === "string"
          ? body.code.trim()
          : "";

      const codeVerifier =
        typeof body.codeVerifier === "string"
          ? body.codeVerifier.trim()
          : "";

      const redirectUri =
        typeof body.redirectUri === "string"
          ? body.redirectUri.trim()
          : "";

      if (
        code.length < 10 ||
        code.length > 4096 ||
        codeVerifier.length < 43 ||
        codeVerifier.length > 128 ||
        redirectUri.length > 200
      ) {
        response
          .status(400)
          .json({ error: "invalid_request" });
        return;
      }

      let parsedRedirect: URL;

      try {
        parsedRedirect = new URL(redirectUri);
      }
      catch {
        response
          .status(400)
          .json({ error: "invalid_redirect_uri" });
        return;
      }

      const loopbackHost =
        parsedRedirect.hostname === "127.0.0.1" ||
        parsedRedirect.hostname === "localhost";

      if (
        parsedRedirect.protocol !== "http:" ||
        !loopbackHost ||
        parsedRedirect.port.length === 0
      ) {
        response
          .status(400)
          .json({ error: "invalid_redirect_uri" });
        return;
      }

      const tokenBody =
        new URLSearchParams({
          client_id: googleOAuthClientId,
          client_secret: googleOAuthClientSecret.value(),
          code,
          code_verifier: codeVerifier,
          redirect_uri: redirectUri,
          grant_type: "authorization_code",
        });

      const googleResponse =
        await fetch(
          "https://oauth2.googleapis.com/token",
          {
            method: "POST",
            headers: {
              "Content-Type":
                "application/x-www-form-urlencoded",
            },
            body: tokenBody.toString(),
          });

      const googleJson =
        (await googleResponse.json()) as {
          id_token?: string;
          error?: string;
          error_description?: string;
        };

      if (
        !googleResponse.ok ||
        !googleJson.id_token
      ) {
        console.warn(
          "Google OAuth exchange rejected.",
          {
            status: googleResponse.status,
            error: googleJson.error,
          });

        response
          .status(401)
          .json({
            error: "google_exchange_failed",
            detail:
              googleJson.error_description ??
              googleJson.error ??
              "Google rejected the authorization code.",
          });
        return;
      }

      const firebaseUrl =
        "https://identitytoolkit.googleapis.com/v1/" +
        "accounts:signInWithIdp?key=" +
        encodeURIComponent(firebaseWebApiKey);

      const firebaseResponse =
        await fetch(
          firebaseUrl,
          {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
            },
            body: JSON.stringify({
              postBody:
                "id_token=" +
                encodeURIComponent(googleJson.id_token) +
                "&providerId=google.com",
              requestUri: "http://localhost",
              returnIdpCredential: true,
              returnSecureToken: true,
            }),
          });

      const firebaseJson =
        (await firebaseResponse.json()) as FirebaseIdpResponse;

      if (
        !firebaseResponse.ok ||
        !firebaseJson.idToken ||
        !firebaseJson.refreshToken ||
        !firebaseJson.localId
      ) {
        console.error(
          "Firebase Google sign-in exchange failed.",
          {
            status: firebaseResponse.status,
            message: firebaseJson.error?.message,
          });

        response
          .status(401)
          .json({
            error: "firebase_auth_exchange_failed",
            detail:
              firebaseJson.error?.message ??
              "Firebase rejected the Google identity.",
          });
        return;
      }

      response
        .status(200)
        .json({
          userId: firebaseJson.localId,
          email: firebaseJson.email ?? "",
          idToken: firebaseJson.idToken,
          refreshToken: firebaseJson.refreshToken,
          expiresIn: firebaseJson.expiresIn ?? "3600",
        });
    }
  );
