"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { SESSION_COOKIE } from "@/lib/constants";

export async function logout() {
  const store = await cookies();
  store.delete(SESSION_COOKIE);
  redirect("/login");
}
