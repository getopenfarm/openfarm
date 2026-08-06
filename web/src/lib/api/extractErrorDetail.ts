/** Backend errors surface as RFC 7807 ProblemDetails on the raw fetch Response httpClient throws
 * (CLAUDE.md: "Errors surface uniformly as RFC 7807 ProblemDetails") — extracts the operator-facing
 * `detail` instead of a generic message. */
export async function extractErrorDetail(
  error: unknown,
  fallback: string,
): Promise<string> {
  if (error instanceof Response) {
    try {
      const problem = await error.json();
      if (typeof problem?.detail === 'string' && problem.detail.length > 0) {
        return problem.detail;
      }
    } catch {
      // Response body wasn't JSON — fall through to the generic fallback.
    }
  }
  return fallback;
}
