"use server";

import { revalidatePath } from "next/cache";
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";

export async function createShift(formData: FormData) {
  const breakStart = formData.get("breakStart");
  const breakEnd = formData.get("breakEnd");
  const allowedBreakMinutes = formData.get("allowedBreakMinutes");

  await backendFetch("/api/shifts", {
    method: "POST",
    token: await getToken(),
    body: JSON.stringify({
      name: String(formData.get("name")),
      startTime: String(formData.get("startTime")),
      endTime: String(formData.get("endTime")),
      graceMinutes: Number(formData.get("graceMinutes") ?? 0),
      punchMode: String(formData.get("punchMode")),
      breakStart: breakStart ? String(breakStart) : null,
      breakEnd: breakEnd ? String(breakEnd) : null,
      allowedBreakMinutes: allowedBreakMinutes ? Number(allowedBreakMinutes) : null,
    }),
  });
  revalidatePath("/shifts");
}

export async function deleteShift(id: string) {
  await backendFetch(`/api/shifts/${id}`, { method: "DELETE", token: await getToken() });
  revalidatePath("/shifts");
}
