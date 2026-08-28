"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { BackendError, backendFetch } from "@/lib/backendFetch";
import { SESSION_COOKIE } from "@/lib/constants";
import type { LoginResult } from "@/lib/types";

export async function login(formData: FormData) {
  const email = String(formData.get("email") ?? "");
  const password = String(formData.get("password") ?? "");

  let result: LoginResult;
  try {
    result = await backendFetch("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
  } catch (err) {
    if (err instanceof BackendError && err.status === 401) {
      redirect("/login?error=invalid");
      return;
    }
    // Network failure, DNS failure, or a non-401 backend error: the credentials
    // may well be correct, so don't tell the user they aren't.
    redirect("/login?error=unreachable");
    return;
  }

  if (result.role !== "TenantAdmin") {
    // Valid credentials, but this account has no access to the portal's
    // TenantAdmin-only pages. Don't set a session cookie for it.
    redirect("/login?error=role");
    return;
  }

  const store = await cookies();
  store.set(SESSION_COOKIE, result.token, {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 60 * 60 * 12,
  });

  redirect("/dashboard");
}
