import { createHash, randomBytes, timingSafeEqual } from "node:crypto";
import { onRequest } from "firebase-functions/v2/https";
import { selectActiveNotifications } from "./notifications/activeNotifications";
import { createAdminNotificationHandlers } from "./admin/notifications";
import { createAdminPricingHandler } from "./admin/pricing";
import { createAdminAppConfigHandler } from "./admin/appConfig";
import { createAdminReadHandlers } from "./admin/readModels";
import { createLegacyAdminHandlers } from "./admin/legacyHandlers";
import { createBootstrapAccount } from "./account/bootstrapAccount";
import { verifyBearer } from "./auth/bearerAuth";
import { createGoogleOAuthExchange } from "./auth/googleOAuth";
import { initializeApp } from "firebase-admin/app";
import { getAuth } from "firebase-admin/auth";
import {
  FieldValue,
  Timestamp,
  getFirestore,
} from "firebase-admin/firestore";

initializeApp();

const db = getFirestore();

export const bootstrapAccount =
  createBootstrapAccount(db, verifyBearer);

// ============================================================
// Google OAuth code exchange
// Secret stays in Google Secret Manager / Firebase Functions.
// The desktop client never receives GOOGLE_OAUTH_CLIENT_SECRET.
// ============================================================

export const getActiveNotification = onRequest(
  {
    region: "asia-south1",
    cors: false,
    invoker: "public",
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
      await verifyBearer(request);

      const snapshot =
        await db.collection("notifications")
          .where("active", "==", true)
          .limit(20)
          .get();

      const records =
        snapshot.docs.map((doc) => ({
          id: doc.id,
          ...(doc.data() as Record<string, unknown>),
        }));

      const notifications =
        selectActiveNotifications(
          records,
          Date.now());

      response.status(200).json({
        checkedAtUtc: new Date().toISOString(),
        notifications,
      });
    }
    catch (error: any) {
      const code = error?.message ?? "INTERNAL_ERROR";

      if (code === "UNAUTHENTICATED") {
        response.status(401).json({ error: code });
        return;
      }

      console.error(
        "getActiveNotification failed",
        error);

      response.status(500).json({
        error: "INTERNAL_ERROR",
      });
    }
  });
import { defineSecret as defineGoogleOAuthSecret } from "firebase-functions/params";

export const exchangeGoogleCode =
  createGoogleOAuthExchange();

// ============================================================
// ADMIN V1 - CONFIGURATION + MONITORING
// Custom claim admin=true is authoritative.
// ============================================================

const adminAccessPassword =
  defineGoogleOAuthSecret("ADMIN_ACCESS_PASSWORD");

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

export const verifyAdminPassword =
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
const legacyAdminHandlers =
  createLegacyAdminHandlers({
    db,
    requireAdmin,
    sendAdminError,
  });

export const getAdminOverview =
  legacyAdminHandlers.getAdminOverview;

export const listAdminUsers =
  legacyAdminHandlers.listAdminUsers;

export const getAdminUser =
  legacyAdminHandlers.getAdminUser;

export const getAdminConfig =
  legacyAdminHandlers.getAdminConfig;

export const updateAdminConfig =
  legacyAdminHandlers.updateAdminConfig;
// ============================================================
// ADMIN V2 - COMPACT MONITORING + CONFIGURATION
// LOW-CALL DESIGN: no polling; lazy tab loads; paged reads.
// ============================================================

const ADMIN_PAGE_DEFAULT = 25;
const ADMIN_PAGE_MAX = 100;

function adminPageSize(value: unknown): number {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return ADMIN_PAGE_DEFAULT;
  return Math.min(ADMIN_PAGE_MAX, Math.max(10, Math.floor(parsed)));
}

function safeText(value: unknown, maxLength = 500): string {
  return typeof value === "string" ? value.trim().substring(0, maxLength) : "";
}

function isoFromTimestamp(value: unknown): string | null {
  return value instanceof Timestamp ? value.toDate().toISOString() : null;
}

async function countWhere(collectionName: string, field: string, value: unknown): Promise<number> {
  const result = await db.collection(collectionName).where(field, "==", value).count().get();
  return result.data().count;
}

const adminReadHandlers =
  createAdminReadHandlers({
    db,
    requireAdmin,
    sendAdminError,
    safeText,
    isoFromTimestamp,
    adminPageSize,
    countWhere,
  });

export const getAdminDashboardV2 =
  adminReadHandlers.getAdminDashboardV2;

export const listAdminAccountsV2 =
  adminReadHandlers.listAdminAccountsV2;

export const listAdminPaymentsV2 =
  adminReadHandlers.listAdminPaymentsV2;
const adminNotificationHandlers =
  createAdminNotificationHandlers({
    db,
    requireAdmin,
    sendAdminError,
    safeText,
    isoFromTimestamp,
  });

export const listAdminNotificationsV2 =
  adminNotificationHandlers.listAdminNotificationsV2;

export const saveAdminNotificationV2 =
  adminNotificationHandlers.saveAdminNotificationV2;
export const updateAdminPricingV2 =
  createAdminPricingHandler({
    db,
    requireAdmin,
    sendAdminError,
    safeText,
  });

export const updateAdminAppConfigV2 =
  createAdminAppConfigHandler({
    db,
    requireAdmin,
    sendAdminError,
    safeText,
  });
