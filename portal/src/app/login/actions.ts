"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { backendFetch } from "@/lib/backendFetch";
import { SESSION_COOKIE } from "@/lib/constants";
import type { LoginResult } from "@/lib/types";

export async function login(formData: FormData) {
  const email = String(formData.get("email") ?? "");
  const password = String(formData.get("password") ?? "");

  try {
    const result: LoginResult = await backendFetch("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
    const store = await cookies();
    store.set(SESSION_COOKIE, result.token, {
      httpOnly: true,
      secure: process.env.NODE_ENV === "production",
      sameSite: "lax",
      path: "/",
      maxAge: 60 * 60 * 12,
    });
  } catch {
    redirect("/login?error=1");
    return;
  }

  redirect("/attendance");
}
