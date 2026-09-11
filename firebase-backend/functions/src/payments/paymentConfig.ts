export type PaymentPlanCode =
  | "six_month"
  | "yearly";

export type PaymentProvider =
  | "razorpay"
  | "dodo";

export interface PaymentPlanConfig {
  enabled: boolean;
  amountMinor: number;
  currency: string;
  provider: PaymentProvider;
  providerProductId: string;
}

export interface PaymentConfig {
  enabled: boolean;
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
  razorpayKeyId: "RAZORPAY_KEY_ID",
  razorpayKeySecret: "RAZORPAY_KEY_SECRET",
  razorpayWebhookSecret: "RAZORPAY_WEBHOOK_SECRET",
  dodoApiKey: "DODO_PAYMENTS_API_KEY",
  dodoWebhookSecret: "DODO_PAYMENTS_WEBHOOK_SECRET",
} as const;

/**
 * Safe defaults only.
 *
 * Prices are stored in minor units:
 * INR 299.00 => 29900 paise
 * INR 499.00 => 49900 paise
 * USD 3.00   => 300 cents
 * USD 5.00   => 500 cents
 *
 * providerProductId is intentionally blank until products/plans
 * are created in Razorpay / Dodo and their safe IDs are saved in
 * Firestore config.
 */
export const DEFAULT_PAYMENT_CONFIG: PaymentConfig = {
  enabled: false,
  indiaProvider: "razorpay",
  internationalProvider: "dodo",
  sixMonth: {
    india: {
      enabled: true,
      amountMinor: 29900,
      currency: "INR",
      provider: "razorpay",
      providerProductId: "",
    },
    international: {
      enabled: true,
      amountMinor: 300,
      currency: "USD",
      provider: "dodo",
      providerProductId: "",
    },
  },
  yearly: {
    india: {
      enabled: true,
      amountMinor: 49900,
      currency: "INR",
      provider: "razorpay",
      providerProductId: "",
    },
    international: {
      enabled: true,
      amountMinor: 500,
      currency: "USD",
      provider: "dodo",
      providerProductId: "",
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
  return value === "razorpay" ||
    value === "dodo"
    ? value
    : fallback;
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
 * - Razorpay plan IDs
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