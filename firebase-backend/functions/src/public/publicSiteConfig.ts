import { getFirestore } from "firebase-admin/firestore";
import { onRequest } from "firebase-functions/v2/https";

import { normalizePaymentConfig } from "../payments/paymentConfig";

export const publicSiteConfig = onRequest(
  {
    region: "asia-south1",
    invoker: "public",
    cors: true,
    timeoutSeconds: 15,
    memory: "128MiB",
  },
  async (_request, response) => {
    response.set("Cache-Control", "public, max-age=300, s-maxage=300");

    try {
      const db = getFirestore();
      const snapshot = await db.collection("config").doc("app").get();
      const data = snapshot.exists ? snapshot.data() ?? {} : {};
      const paymentConfig = normalizePaymentConfig(data.paymentConfig);

      response.status(200).json({
        latestVersion:
          typeof data.latestVersion === "string"
            ? data.latestVersion.slice(0, 40)
            : "",
        trialDays: 7,
        sixMonth: {
          enabled: paymentConfig.sixMonth.international.enabled,
          amountMinor: paymentConfig.sixMonth.international.amountMinor,
          currency: paymentConfig.sixMonth.international.currency,
        },
        yearly: {
          enabled: paymentConfig.yearly.international.enabled,
          amountMinor: paymentConfig.yearly.international.amountMinor,
          currency: paymentConfig.yearly.international.currency,
        },
      });
    } catch (error) {
      console.error("publicSiteConfig failed", error);
      response.status(500).json({ error: "CONFIG_UNAVAILABLE" });
    }
  });
