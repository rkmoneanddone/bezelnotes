import { createHash, randomBytes, timingSafeEqual } from "node:crypto";
import { defineSecret } from "firebase-functions/params";
import { onRequest } from "firebase-functions/v2/https";
import { getAuth } from "firebase-admin/auth";
import {
  Timestamp,
  type Firestore,
} from "firebase-admin/firestore";

export function createAdminAuthHandlers(db: Firestore) {
const adminAccessPassword =
  defineSecret("ADMIN_ACCESS_PASSWORD");

async function requireAdminAuth(request: any) {
  const authorization = request.get("authorization") || "";

  if (!authorization.startsWith("Bearer ")) {
    throw new Error("UNAUTHENTICATED");
  }

  const idToken =
    authorization.substring("Bearer ".length).trim();

  const decoded =
    await getAuth().verifyIdToken(idToken, true);

  return decoded;
}

function hashAdminSession(token: string): string {
  return createHash("sha256")
    .update(token, "utf8")
    .digest("hex");
}

const allowedAdminEmail = "rohitmallick85@gmail.com";

async function requireAdminClaim(
  request: any
): Promise<any> {
  const decoded =
    await requireAdminAuth(request);

  if (decoded.admin !== true) {
    throw new Error("ADMIN_REQUIRED");
  }

  const email =
    typeof decoded.email === "string"
      ? decoded.email.trim().toLowerCase()
      : "";

  if (email !== allowedAdminEmail) {
    throw new Error("ADMIN_EMAIL_NOT_ALLOWED");
  }

  return decoded;
}

async function requireAdmin(
  request: any
): Promise<any> {
  const decoded =
    await requireAdminClaim(request);

  const sessionToken =
    typeof request.get("x-admin-session") === "string"
      ? request.get("x-admin-session").trim()
      : "";

  if (sessionToken.length < 32) {
    throw new Error("ADMIN_PASSWORD_REQUIRED");
  }

  const sessionHash =
    hashAdminSession(sessionToken);

  const sessionDoc =
    await db.collection("adminSessions")
      .doc(sessionHash)
      .get();

  if (!sessionDoc.exists) {
    throw new Error("ADMIN_PASSWORD_REQUIRED");
  }

  const data =
    sessionDoc.data() ?? {};

  const expiresAt =
    data.expiresAt instanceof Timestamp
      ? data.expiresAt
      : null;

  if (
    data.uid !== decoded.uid ||
    !expiresAt ||
    expiresAt.toMillis() <= Date.now()
  ) {
    throw new Error("ADMIN_SESSION_EXPIRED");
  }

  return decoded;
}

function sendAdminError(response: any, error: any) {
  const code = error?.message ?? "INTERNAL_ERROR";

  if (code === "UNAUTHENTICATED") {
    response.status(401).json({error: code});
    return;
  }

    if (code === "ADMIN_EMAIL_NOT_ALLOWED") {
    response.status(403).json({
      error: "ADMIN_EMAIL_NOT_ALLOWED",
    });
    return;
  }
if (code === "ADMIN_REQUIRED") {
    response.status(403).json({error: code});
    return;
  }
  if (
    code === "ADMIN_PASSWORD_REQUIRED" ||
    code === "ADMIN_SESSION_EXPIRED"
  ) {
    response.status(403).json({ error: code });
    return;
  }

  if (code === "INVALID_REQUEST") {
    response.status(400).json({error: code});
    return;
  }

  console.error("admin function failed", error);
  response.status(500).json({error: "INTERNAL_ERROR"});
}

  const verifyAdminPassword =
  onRequest(
    {
      region: "asia-south1",
      invoker: "public",
      cors: false,
      secrets: [adminAccessPassword],
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
        const decoded =
          await requireAdminClaim(request);

        const supplied =
          typeof request.body?.password === "string"
            ? request.body.password
            : "";

        const expected =
          adminAccessPassword.value();

        const suppliedBuffer =
          Buffer.from(supplied, "utf8");

        const expectedBuffer =
          Buffer.from(expected, "utf8");

        const matches =
          suppliedBuffer.length === expectedBuffer.length &&
          suppliedBuffer.length > 0 &&
          timingSafeEqual(
            suppliedBuffer,
            expectedBuffer);

        if (!matches) {
          response.status(403).json({
            error: "INVALID_ADMIN_PASSWORD",
          });
          return;
        }

        const rawSession =
          randomBytes(32)
            .toString("base64url");

        const sessionHash =
          hashAdminSession(rawSession);

        const now =
          Timestamp.now();

        const expiresAt =
          Timestamp.fromMillis(
            now.toMillis() +
            30 * 60 * 1000);

        await db.collection("adminSessions")
          .doc(sessionHash)
          .set({
            uid: decoded.uid,
            email:
              typeof decoded.email === "string"
                ? decoded.email
                : "",
            createdAt: now,
            expiresAt,
          });

        response.status(200).json({
          sessionToken: rawSession,
          expiresAtUtc:
            expiresAt.toDate().toISOString(),
        });
      }
      catch (error: any) {
        sendAdminError(response, error);
      }
    });
  return {
    verifyAdminPassword,
    requireAdmin,
    sendAdminError,
  };
}