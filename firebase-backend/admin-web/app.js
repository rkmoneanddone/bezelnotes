const CONFIG_URL = "https://asia-south1-bezelstickynotes.cloudfunctions.net/publicSiteConfig";

function formatMoney(minor, currency) {
  const value = Number(minor || 0) / 100;
  try {
    return new Intl.NumberFormat("en-US", {
      style: "currency",
      currency: currency || "USD",
      maximumFractionDigits: Number.isInteger(value) ? 0 : 2
    }).format(value);
  } catch {
    return "$" + value.toFixed(Number.isInteger(value) ? 0 : 2);
  }
}

async function loadPublicConfig() {
  try {
    const response = await fetch(CONFIG_URL, { cache: "no-store" });
    if (!response.ok) return;
    const config = await response.json();

    if (config.latestVersion) {
      document.getElementById("currentVersion").textContent =
        "Version " + config.latestVersion;
    }

    if (config.sixMonth) {
      document.getElementById("sixMonthPrice").textContent =
        formatMoney(config.sixMonth.amountMinor, config.sixMonth.currency);
    }

    if (config.yearly) {
      document.getElementById("yearlyPrice").textContent =
        formatMoney(config.yearly.amountMinor, config.yearly.currency);
    }
  } catch {
    // Static fallback values remain visible if config is temporarily unavailable.
  }
}

document.getElementById("year").textContent = new Date().getFullYear();
loadPublicConfig();
