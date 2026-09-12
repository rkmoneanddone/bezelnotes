export type PaymentPlanCode =
  | "six_month"
  | "yearly";

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

/**
 * Secret Manager names only.
 *
 * IMPORTANT:
 * These are secret IDENTIFIERS, never secret values.
 * The values must be stored in Firebase / Google Secret Manager
 * and must never be committed to Git, Firestore, desktop config,
 * appsettings, environment files, or the WPF executable.
 */
export const PAYMENT_SECRET_NAMES = {
  dodoApiKey: "DODO_PAYMENTS_API_KEY",
  dodoWebhookSecret: "DODO_PAYMENTS_WEBHOOK_SECRET",
} as const;

/**
 * Safe defaults only.
 *
 * Prices are stored in minor units:
 * 6-month recurring subscription => USD 5.00
 * Yearly recurring subscription  => USD 9.00
 * All markets use Dodo Payments
 * Test product IDs are placeholders until Dodo products are created
 *
 * providerProductId is intentionally blank until products/plans
 * are created in Dodo and their safe IDs are saved in
 * Firestore config.
 */
export const DEFAULT_PAYMENT_CONFIG: PaymentConfig = {
  enabled: false,
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
      amountMinor: 500,
      currency: "USD",
      provider: "dodo",
      providerProductId: "dodo_test_6_month_product",
    },
    international: {
      enabled: true,
      amountMinor: 500,
      currency: "USD",
      provider: "dodo",
      providerProductId: "dodo_test_6_month_product",
    },
  },
  yearly: {
    india: {
      enabled: true,
      amountMinor: 900,
      currency: "USD",
      provider: "dodo",
      providerProductId: "dodo_test_yearly_product",
    },
    international: {
      enabled: true,
      amountMinor: 900,
      currency: "USD",
      provider: "dodo",
      providerProductId: "dodo_test_yearly_product",
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

function normalizePlan(
  raw: unknown,
  fallback: PaymentPlanConfig,
): PaymentPlanConfig {
  const source =
    raw &&
    typeof raw === "object"
      ? raw as Record<string, unknown>
      : {};

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
    providerProductId: toStringValue(
      source.providerProductId,
      fallback.providerProductId,
    ),
  };
}

/**
 * Normalizes Firestore config/payments safely.
 *
 * Firestore may contain:
 * - prices
 * - currencies
 * - provider routing
 * - enabled flags
 * - Dodo product IDs
 * - Dodo product IDs
 *
 * Firestore MUST NOT contain gateway secrets.
 */
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