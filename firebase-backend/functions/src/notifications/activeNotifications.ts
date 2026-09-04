export type NotificationRecord = {
  id: string;
  title?: unknown;
  message?: unknown;
  type?: unknown;
  startAtUtc?: unknown;
  expiresAtUtc?: unknown;
  [key: string]: unknown;
};

export type ActiveNotificationDto = {
  id: string;
  title: string;
  message: string;
  type: string;
  startAtUtc: string | null;
  expiresAtUtc: string | null;
};

function parseUtcMillis(
  value: unknown,
  fallback: number
): number {
  if (typeof value !== "string" ||
      value.trim().length === 0) {
    return fallback;
  }

  const parsed = Date.parse(value);

  return Number.isNaN(parsed)
    ? fallback
    : parsed;
}

function normalizeUtc(
  value: unknown
): string | null {
  if (typeof value !== "string" ||
      value.trim().length === 0) {
    return null;
  }

  const parsed = new Date(value);

  return Number.isNaN(parsed.getTime())
    ? null
    : parsed.toISOString();
}

export function selectActiveNotifications(
  records: NotificationRecord[],
  nowMs: number
): ActiveNotificationDto[] {
  return records
    .filter((item) => {
      const starts =
        parseUtcMillis(
          item.startAtUtc,
          0);

      const expires =
        parseUtcMillis(
          item.expiresAtUtc,
          Number.MAX_SAFE_INTEGER);

      return starts <= nowMs && expires >= nowMs;
    })
    .sort((a, b) => {
      const aStart =
        parseUtcMillis(
          a.startAtUtc,
          0);

      const bStart =
        parseUtcMillis(
          b.startAtUtc,
          0);

      return bStart - aStart;
    })
    .map((item) => ({
      id: item.id,
      title:
        typeof item.title === "string"
          ? item.title
          : "",
      message:
        typeof item.message === "string"
          ? item.message
          : "",
      type:
        typeof item.type === "string"
          ? item.type
          : "info",
      startAtUtc:
        normalizeUtc(item.startAtUtc),
      expiresAtUtc:
        normalizeUtc(item.expiresAtUtc),
    }));
}