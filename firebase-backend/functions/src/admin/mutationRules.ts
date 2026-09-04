export function requirePostMethod(
  request: any,
  response: any
): boolean {
  if (request.method === "POST") {
    return true;
  }

  response.status(405).json({
    error: "METHOD_NOT_ALLOWED",
  });

  return false;
}

export function boundedInteger(
  value: unknown,
  fallback: number,
  minimum: number,
  maximum: number
): number {
  const numeric = Number(value);

  const normalized =
    Number.isFinite(numeric)
      ? Math.floor(numeric)
      : fallback;

  return Math.min(
    maximum,
    Math.max(
      minimum,
      normalized));
}

export function boundedPositiveMoneyCents(
  value: unknown,
  fallback: number
): number {
  const numeric = Number(value);

  const normalized =
    Number.isFinite(numeric)
      ? Math.round(numeric)
      : fallback;

  return Math.max(
    1,
    normalized);
}