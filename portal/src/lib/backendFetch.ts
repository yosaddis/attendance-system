import { redirect } from "next/navigation";

const BACKEND_URL = process.env.BACKEND_API_URL ?? "http://localhost:8080";

export class BackendError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function backendFetch(
  path: string,
  init: RequestInit & { token?: string } = {},
): Promise<any> {
  const { token, ...rest } = init;
  const headers = new Headers(rest.headers);
  headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const response = await fetch(`${BACKEND_URL}${path}`, { ...rest, headers, cache: "no-store" });

  // A 401 on a request that carried a token means a previously-valid session is no
  // longer accepted (expired, or the backend restarted with a different signing key)
  // — send the user to a clean login instead of a stack-trace error page. A 401 with
  // no token (e.g. a failed login attempt itself) is a normal auth failure the caller
  // handles directly (see portal/src/app/login/actions.ts), not an expired session.
  //
  // Deliberately does NOT clear the session cookie here: backendFetch runs inside
  // Server Component rendering (every page's data fetch), and Next.js only allows
  // cookie mutations from a Server Action or Route Handler — attempting `cookies().
  // delete(...)` here throws "Cookies can only be modified in a Server Action or
  // Route Handler" and turns this into a 500 instead of a redirect. The stale cookie
  // is harmless: it keeps failing the same way on every request until the user logs
  // in again, at which point login/actions.ts's Server Action overwrites it.
  if (response.status === 401 && token) {
    redirect("/login");
  }

  if (!response.ok) {
    const text = await response.text().catch(() => "");
    throw new BackendError(response.status, text || response.statusText);
  }
  if (response.status === 204) return null;
  return response.json();
}
