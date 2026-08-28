import { NextRequest } from "next/server";
import { getToken } from "@/lib/session";

const BACKEND_URL = process.env.BACKEND_API_URL ?? "http://localhost:8080";

export async function GET(request: NextRequest) {
  const from = request.nextUrl.searchParams.get("from") ?? "";
  const to = request.nextUrl.searchParams.get("to") ?? "";
  const token = await getToken();

  const backendResponse = await fetch(
    `${BACKEND_URL}/api/attendance/report/export?from=${from}&to=${to}`,
    { headers: token ? { Authorization: `Bearer ${token}` } : {}, cache: "no-store" },
  );

  if (!backendResponse.ok) {
    return new Response(await backendResponse.text(), { status: backendResponse.status });
  }

  return new Response(backendResponse.body, {
    headers: {
      "Content-Type": "text/csv",
      "Content-Disposition": backendResponse.headers.get("Content-Disposition") ?? "attachment",
    },
  });
}
