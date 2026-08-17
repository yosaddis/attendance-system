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

  if (!response.ok) {
    const text = await response.text().catch(() => "");
    throw new BackendError(response.status, text || response.statusText);
  }
  if (response.status === 204) return null;
  return response.json();
}
