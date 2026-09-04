import { defineSecret } from "firebase-functions/params";
import { onRequest } from "firebase-functions/v2/https";

const googleOAuthClientSecret =
  defineSecret("GOOGLE_OAUTH_CLIENT_SECRET");
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

export function createGoogleOAuthExchange() {
  return onRequest(
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
}
