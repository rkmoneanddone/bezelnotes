const CONFIG_URL =
  "https://asia-south1-bezelstickynotes.cloudfunctions.net/publicSiteConfig";

function formatMoney(minor, currency) {
  const value = Number(minor || 0) / 100;

  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency: currency || "USD",
      maximumFractionDigits: Number.isInteger(value) ? 0 : 2
    }).format(value);
  }
  catch {
    return (currency || "USD") + " " +
      value.toFixed(Number.isInteger(value) ? 0 : 2);
  }
}

function looksLikeIndia() {
  try {
    const locale =
      (navigator.language || "").toUpperCase();

    const timeZone =
      Intl.DateTimeFormat()
        .resolvedOptions()
        .timeZone || "";

    return (
      locale.endsWith("-IN") ||
      timeZone === "Asia/Kolkata" ||
      timeZone === "Asia/Calcutta"
    );
  }
  catch {
    return false;
  }
}

function applyPlan(
  elementId,
  marketId,
  plan,
  marketName) {

  if (!plan) return;

  const priceElement =
    document.getElementById(elementId);

  const marketElement =
    document.getElementById(marketId);

  if (priceElement) {
    priceElement.textContent =
      formatMoney(
        plan.amountMinor,
        plan.currency);
  }

  if (marketElement) {
    marketElement.textContent =
      marketName;
  }
}

async function loadPublicConfig() {
  try {
    const response =
      await fetch(
        CONFIG_URL,
        { cache: "no-store" });

    if (!response.ok) return;

    const config =
      await response.json();

    if (config.latestVersion) {
      const versionElement =
        document.getElementById(
          "currentVersion");

      if (versionElement) {
        versionElement.textContent =
          "Version " +
          config.latestVersion;
      }
    }

    const india =
      looksLikeIndia();

    const market =
      india
        ? config.india
        : config.international;

    const marketName =
      india
        ? "India pricing"
        : "International pricing";

    if (market) {
      applyPlan(
        "sixMonthPrice",
        "sixMonthMarket",
        market.sixMonth,
        marketName);

      applyPlan(
        "yearlyPrice",
        "yearlyMarket",
        market.yearly,
        marketName);
    }
  }
  catch {
    // Safe fallback prices remain visible.
  }
}

const yearElement =
  document.getElementById("year");

if (yearElement) {
  yearElement.textContent =
    new Date().getFullYear();
}

loadPublicConfig();