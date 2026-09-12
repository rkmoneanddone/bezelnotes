import { onRequest } from "firebase-functions/v2/https";
import { getAuth } from "firebase-admin/auth";
import type { Firestore } from "firebase-admin/firestore";
import { normalizePaymentConfig } from "../payments/paymentConfig";

type RequireAdmin =
  (request: any) => Promise<any>;

type SendAdminError =
  (response: any, error: any) => void;

type SafeText =
  (value: unknown, maxLength?: number) => string;

type IsoFromTimestamp =
  (value: unknown) => string | null;

type AdminPageSize =
  (value: unknown) => number;

type CountWhere =
  (
    collectionName: string,
    field: string,
    value: unknown
  ) => Promise<number>;

export function createAdminReadHandlers(options: {
  db: Firestore;
  requireAdmin: RequireAdmin;
  sendAdminError: SendAdminError;
  safeText: SafeText;
  isoFromTimestamp: IsoFromTimestamp;
  adminPageSize: AdminPageSize;
  countWhere: CountWhere;
}) {
  const {
    db,
    requireAdmin,
    sendAdminError,
    safeText,
    isoFromTimestamp,
    adminPageSize,
    countWhere,
  } = options;
  const getAdminDashboardV2 = onRequest(
  { region: "asia-south1", invoker: "public", cors: false },
  async (request, response) => {
    response.set("Cache-Control", "no-store");
    if (request.method !== "GET") { response.status(405).json({error:"METHOD_NOT_ALLOWED"}); return; }
    try {
      await requireAdmin(request);
      const [users, installs, trials, expired, sixMonth, yearly, payOk, payFail, pricingDoc, appDoc] = await Promise.all([
        db.collection("users").count().get(),
        db.collection("installations").count().get(),
        countWhere("entitlements","entitlementState","trial"),
        countWhere("entitlements","entitlementState","expired"),
        countWhere("entitlements","planCode","six_month"),
        countWhere("entitlements","planCode","yearly"),
        countWhere("payments","status","confirmed"),
        countWhere("payments","status","failed"),
        db.collection("config").doc("pricing").get(),
        db.collection("config").doc("app").get(),
      ]);
      response.status(200).json({
        counts:{users:users.data().count,installations:installs.data().count,trials,notSubscribed:expired,sixMonth,yearly,paymentsConfirmed:payOk,paymentsFailed:payFail},
        pricing:pricingDoc.exists?pricingDoc.data():{trialDays:7,monthlyPriceCents:149,yearlyPriceCents:599,currency:"USD",monthlyPriceProtectionMonths:12},
        appConfig:appDoc.exists?{...appDoc.data(),paymentConfig:normalizePaymentConfig(appDoc.data()?.paymentConfig)}:{latestVersion:"",minimumVersion:"",cloudSyncEnabled:false,clientRefreshHours:48,paymentConfig:normalizePaymentConfig(undefined)}
      });
    } catch(error:any){ sendAdminError(response,error); }
  });

  const listAdminAccountsV2 = onRequest(
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
      else if(category==="six_month") query=query.where("planCode","==","six_month");
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

  const listAdminPaymentsV2 = onRequest(
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
  return {
    getAdminDashboardV2,
    listAdminAccountsV2,
    listAdminPaymentsV2,
  };
}