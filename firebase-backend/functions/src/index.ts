import { createHash, randomBytes, timingSafeEqual } from "node:crypto";
import { onRequest } from "firebase-functions/v2/https";
import { selectActiveNotifications } from "./notifications/activeNotifications";
import { createAdminNotificationHandlers } from "./admin/notifications";
import { createAdminPricingHandler } from "./admin/pricing";
import { createAdminAppConfigHandler } from "./admin/appConfig";
import { createBootstrapAccount } from "./account/bootstrapAccount";
import { verifyBearer } from "./auth/bearerAuth";
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
export const getAdminOverview = onRequest(
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

export const listAdminUsers = onRequest(
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

export const getAdminUser = onRequest(
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

export const getAdminConfig = onRequest(
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

export const updateAdminConfig = onRequest(
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

export const getAdminDashboardV2 = onRequest(
  { region: "asia-south1", invoker: "public", cors: false },
  async (request, response) => {
    response.set("Cache-Control", "no-store");
    if (request.method !== "GET") { response.status(405).json({error:"METHOD_NOT_ALLOWED"}); return; }
    try {
      await requireAdmin(request);
      const [users, installs, trials, expired, monthly, yearly, payOk, payFail, pricingDoc, appDoc] = await Promise.all([
        db.collection("users").count().get(),
        db.collection("installations").count().get(),
        countWhere("entitlements","entitlementState","trial"),
        countWhere("entitlements","entitlementState","expired"),
        countWhere("entitlements","planCode","monthly"),
        countWhere("entitlements","planCode","yearly"),
        countWhere("payments","status","confirmed"),
        countWhere("payments","status","failed"),
        db.collection("config").doc("pricing").get(),
        db.collection("config").doc("app").get(),
      ]);
      response.status(200).json({
        counts:{users:users.data().count,installations:installs.data().count,trials,notSubscribed:expired,monthly,yearly,paymentsConfirmed:payOk,paymentsFailed:payFail},
        pricing:pricingDoc.exists?pricingDoc.data():{trialDays:7,monthlyPriceCents:149,yearlyPriceCents:599,currency:"USD",monthlyPriceProtectionMonths:12},
        appConfig:appDoc.exists?appDoc.data():{latestVersion:"",minimumVersion:"",cloudSyncEnabled:false,clientRefreshHours:48}
      });
    } catch(error:any){ sendAdminError(response,error); }
  });

export const listAdminAccountsV2 = onRequest(
  { region:"asia-south1", invoker:"public", cors:false },
  async (request,response) => {
    response.set("Cache-Control","no-store");
    if(request.method!=="GET"){response.status(405).json({error:"METHOD_NOT_ALLOWED"});return;}
    try{
      await requireAdmin(request);
      const category=safeText(request.query.category,32).toLowerCase();
      const limit=adminPageSize(request.query.limit);
      const cursor=safeText(request.query.cursor,160);
      let query:FirebaseFirestore.Query=db.collection("entitlements");
      if(category==="trial") query=query.where("entitlementState","==","trial");
      else if(category==="not_subscribed") query=query.where("entitlementState","==","expired");
      else if(category==="monthly") query=query.where("planCode","==","monthly");
      else if(category==="yearly") query=query.where("planCode","==","yearly");
      else throw new Error("INVALID_REQUEST");
      query=query.orderBy("__name__").limit(limit+1);
      if(cursor){const c=await db.collection("entitlements").doc(cursor).get();if(c.exists)query=query.startAfter(c);}
      const snap=await query.get();
      const hasMore=snap.docs.length>limit;
      const docs=snap.docs.slice(0,limit);
      const authResult=docs.length?await getAuth().getUsers(docs.map(d=>({uid:d.id}))):{users:[],notFound:[]};
      const authMap=new Map(authResult.users.map(u=>[u.uid,u]));
      response.status(200).json({
        accounts:docs.map(doc=>{const d=doc.data();const u=authMap.get(doc.id);return{
          uid:doc.id,email:u?.email??"",createdAtUtc:u?.metadata.creationTime??null,lastSignInAtUtc:u?.metadata.lastSignInTime??null,
          entitlementState:safeText(d.entitlementState,32)||"none",planCode:safeText(d.planCode,32)||"none",premiumEnabled:d.premiumEnabled===true,
          trialStartedAtUtc:isoFromTimestamp(d.trialStartedAt),trialEndsAtUtc:isoFromTimestamp(d.trialEndsAt),trialDaysAtSignup:Number(d.trialDaysAtSignup)||null,
          priceAtSignupCents:Number(d.priceAtSignupCents)||null,priceProtectedUntilUtc:isoFromTimestamp(d.priceProtectedUntil),currentPeriodEndUtc:isoFromTimestamp(d.currentPeriodEnd)
        }}),
        hasMore,nextCursor:hasMore&&docs.length?docs[docs.length-1].id:null
      });
    }catch(error:any){sendAdminError(response,error);}
  });

export const listAdminPaymentsV2 = onRequest(
  { region:"asia-south1", invoker:"public", cors:false },
  async (request,response)=>{
    response.set("Cache-Control","no-store");
    if(request.method!=="GET"){response.status(405).json({error:"METHOD_NOT_ALLOWED"});return;}
    try{
      await requireAdmin(request);
      const limit=adminPageSize(request.query.limit);const cursor=safeText(request.query.cursor,160);
      let query:FirebaseFirestore.Query=db.collection("payments").orderBy("createdAt","desc").limit(limit+1);
      if(cursor){const c=await db.collection("payments").doc(cursor).get();if(c.exists)query=query.startAfter(c);}
      const snap=await query.get();const hasMore=snap.docs.length>limit;const docs=snap.docs.slice(0,limit);
      response.status(200).json({payments:docs.map(doc=>{const d=doc.data();return{
        id:doc.id,uid:safeText(d.uid,160),email:safeText(d.email,320),planCode:safeText(d.planCode,32),amountCents:Number(d.amountCents)||0,currency:safeText(d.currency,8)||"USD",
        status:safeText(d.status,32)||"pending",gatewayReference:safeText(d.gatewayReference,160),failureReason:safeText(d.failureReason,300),createdAtUtc:isoFromTimestamp(d.createdAt)
      }}),hasMore,nextCursor:hasMore&&docs.length?docs[docs.length-1].id:null});
    }catch(error:any){sendAdminError(response,error);}
  });

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
