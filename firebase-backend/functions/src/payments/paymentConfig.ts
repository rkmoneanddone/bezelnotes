export type PaymentPlanCode =
  | "six_month"
  | "yearly";

export type PaymentMarket =
  | "india"
  | "international";

export type PaymentProvider =
  | "dodo";

export interface PaymentPlanConfig {
  enabled: boolean;
  amountMinor: number;
  currency: string;
  provider: PaymentProvider;
  providerProductId: string;
}

export type DodoEnvironment =
  | "test"
  | "live";

export interface PaymentConfig {
  enabled: boolean;
  environment: DodoEnvironment;
  checkoutReturnUrl: string;
  checkoutCancelUrl: string;
  customerPortalUrl: string;
  indiaProvider: PaymentProvider;
  internationalProvider: PaymentProvider;
  sixMonth: {
    india: PaymentPlanConfig;
    international: PaymentPlanConfig;
  };
  yearly: {
    india: PaymentPlanConfig;
    international: PaymentPlanConfig;
  };
}

export const PAYMENT_SECRET_NAMES = {
  dodoApiKey: "DODO_PAYMENTS_API_KEY",
  dodoWebhookSecret: "DODO_PAYMENTS_WEBHOOK_SECRET",
} as const;

/*
 * Safe operational defaults.
 *
 * All values remain configurable through config/app.paymentConfig.
 * Secret values never belong in Firestore.
 *
 * India:
 *   INR 399 every 6 months
 *   INR 699 every 12 months
 *
 * International:
 *   USD 6 every 6 months
 *   USD 9 every 12 months
 */
export const DEFAULT_PAYMENT_CONFIG: PaymentConfig = {
  enabled: true,
  environment: "test",
  checkoutReturnUrl:
    "https://bezelstickynotes.web.app/payment/return",
  checkoutCancelUrl:
    "https://bezelstickynotes.web.app/payment/cancel",
  customerPortalUrl:
    "https://bezelstickynotes.web.app/account/billing",
  indiaProvider: "dodo",
  internationalProvider: "dodo",
  sixMonth: {
    india: {
      enabled: true,
      amountMinor: 39900,
      currency: "INR",
      provider: "dodo",
      providerProductId:
        "pdt_0NnQGA8yriwqSjQkA4n7W",
    },
    international: {
      enabled: true,
      amountMinor: 600,
      currency: "USD",
      provider: "dodo",
      providerProductId:
        "pdt_0NnQH3R1lePy4qNQj0Gmk",
    },
  },
  yearly: {
    india: {
      enabled: true,
      amountMinor: 69900,
      currency: "INR",
      provider: "dodo",
      providerProductId:
        "pdt_0NnQGaBTFSnbl1765WTO7",
    },
    international: {
      enabled: true,
      amountMinor: 900,
      currency: "USD",
      provider: "dodo",
      providerProductId:
        "pdt_0NnQHDrQkSbZYxDBVi6nt",
    },
  },
};

function toBoolean(
  value: unknown,
  fallback: boolean,
): boolean {
  return typeof value === "boolean"
    ? value
    : fallback;
}

function toStringValue(
  value: unknown,
  fallback: string,
): string {
  return typeof value === "string"
    ? value.trim()
    : fallback;
}

function toPositiveInt(
  value: unknown,
  fallback: number,
): number {
  return Number.isInteger(value) &&
    Number(value) > 0
    ? Number(value)
    : fallback;
}

function toProvider(
  value: unknown,
  fallback: PaymentProvider,
): PaymentProvider {
  return value === "dodo"
    ? value
    : fallback;
}

function toEnvironment(
  value: unknown,
  fallback: DodoEnvironment,
): DodoEnvironment {
  return value === "live" ||
    value === "test"
    ? value
    : fallback;
}

function toUrl(
  value: unknown,
  fallback: string,
): string {
  const text =
    toStringValue(value, fallback);

  if (!text) {
    return fallback;
  }

  try {
    const parsed = new URL(text);

    return parsed.protocol === "https:"
      ? parsed.toString()
      : fallback;
  }
  catch {
    return fallback;
  }
}

function isRealDodoProductId(
  value: unknown,
): value is string {
  return typeof value === "string" &&
    /^pdt_[A-Za-z0-9]+$/.test(value.trim());
}

function normalizePlan(
  raw: unknown,
  fallback: PaymentPlanConfig,
): PaymentPlanConfig {
  const source =
    raw &&
    typeof raw === "object"
      ? raw as Record<string, unknown>
      : {};

  /*
   * Old Bezel builds stored placeholder product IDs.
   * If the product ID is still a placeholder, migrate the
   * complete plan to the current safe default. Once an admin
   * saves a real pdt_ ID, every field is fully configurable.
   */
  if (!isRealDodoProductId(
    source.providerProductId)) {
    return {
      ...fallback,
      enabled: toBoolean(
        source.enabled,
        fallback.enabled),
    };
  }

  return {
    enabled: toBoolean(
      source.enabled,
      fallback.enabled,
    ),
    amountMinor: toPositiveInt(
      source.amountMinor,
      fallback.amountMinor,
    ),
    currency: toStringValue(
      source.currency,
      fallback.currency,
    ).toUpperCase(),
    provider: toProvider(
      source.provider,
      fallback.provider,
    ),
    providerProductId:
      toStringValue(
        source.providerProductId,
        fallback.providerProductId),
  };
}

export function normalizePaymentConfig(
  raw: unknown,
): PaymentConfig {
  const source =
    raw &&
    typeof raw === "object"
      ? raw as Record<string, unknown>
      : {};

  const sixMonth =
    source.sixMonth &&
    typeof source.sixMonth === "object"
      ? source.sixMonth as Record<string, unknown>
      : {};

  const yearly =
    source.yearly &&
    typeof source.yearly === "object"
      ? source.yearly as Record<string, unknown>
      : {};

  return {
    enabled: toBoolean(
      source.enabled,
      DEFAULT_PAYMENT_CONFIG.enabled,
    ),
    environment: toEnvironment(
      source.environment,
      DEFAULT_PAYMENT_CONFIG.environment,
    ),
    checkoutReturnUrl: toUrl(
      source.checkoutReturnUrl,
      DEFAULT_PAYMENT_CONFIG.checkoutReturnUrl,
    ),
    checkoutCancelUrl: toUrl(
      source.checkoutCancelUrl,
      DEFAULT_PAYMENT_CONFIG.checkoutCancelUrl,
    ),
    customerPortalUrl: toUrl(
      source.customerPortalUrl,
      DEFAULT_PAYMENT_CONFIG.customerPortalUrl,
    ),
    indiaProvider: toProvider(
      source.indiaProvider,
      DEFAULT_PAYMENT_CONFIG.indiaProvider,
    ),
    internationalProvider: toProvider(
      source.internationalProvider,
      DEFAULT_PAYMENT_CONFIG.internationalProvider,
    ),
    sixMonth: {
      india: normalizePlan(
        sixMonth.india,
        DEFAULT_PAYMENT_CONFIG.sixMonth.india,
      ),
      international: normalizePlan(
        sixMonth.international,
        DEFAULT_PAYMENT_CONFIG.sixMonth.international,
      ),
    },
    yearly: {
      india: normalizePlan(
        yearly.india,
        DEFAULT_PAYMENT_CONFIG.yearly.india,
      ),
      international: normalizePlan(
        yearly.international,
        DEFAULT_PAYMENT_CONFIG.yearly.international,
      ),
    },
  };
}