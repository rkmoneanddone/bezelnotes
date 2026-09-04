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
  // Preserve the original endpoint behavior:
  // Number(value) || fallback
  const numeric =
    Number(value) || fallback;

  return Math.min(
    maximum,
    Math.max(
      minimum,
      Math.floor(numeric)));
}

export function boundedPositiveMoneyCents(
  value: unknown,
  fallback: number
): number {
  // Preserve the original endpoint behavior:
  // Number(value) || fallback
  const numeric =
    Number(value) || fallback;

  return Math.max(
    1,
    Math.round(numeric));
}