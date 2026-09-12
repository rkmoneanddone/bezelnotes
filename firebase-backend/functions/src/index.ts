import { onRequest } from "firebase-functions/v2/https";
import { selectActiveNotifications } from "./notifications/activeNotifications";
import { createAdminNotificationHandlers } from "./admin/notifications";
import { createAdminPricingHandler } from "./admin/pricing";
import { createAdminAppConfigHandler } from "./admin/appConfig";
import { createAdminReadHandlers } from "./admin/readModels";
import { createLegacyAdminHandlers } from "./admin/legacyHandlers";
import { createAdminAuthHandlers } from "./admin/auth";
import { createBootstrapAccount } from "./account/bootstrapAccount";
import { createPaymentCheckoutHandler } from "./payments/createCheckout";
import { verifyBearer } from "./auth/bearerAuth";
import { createGoogleOAuthExchange } from "./auth/googleOAuth";
import { initializeApp } from "firebase-admin/app";
import {
  FieldValue,
  Timestamp,
  getFirestore,
} from "firebase-admin/firestore";

initializeApp();

const db = getFirestore();

export const bootstrapAccount =
  createBootstrapAccount(db, verifyBearer);

export const createPaymentCheckout =
  createPaymentCheckoutHandler(
    db,
    verifyBearer);

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

export const exchangeGoogleCode =
  createGoogleOAuthExchange();

// ============================================================
// ADMIN V1 - CONFIGURATION + MONITORING
// Custom claim admin=true is authoritative.
// ============================================================

const adminAuthHandlers =
  createAdminAuthHandlers(db);

export const verifyAdminPassword =
  adminAuthHandlers.verifyAdminPassword;

const requireAdmin =
  adminAuthHandlers.requireAdmin;

const sendAdminError =
  adminAuthHandlers.sendAdminError;
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

export { publicSiteConfig } from "./public/publicSiteConfig";
