"use server";

import { revalidatePath } from "next/cache";
import { BackendError, backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";

export type CreateShiftState = { error: string } | null;

export async function createShift(
  _prevState: CreateShiftState,
  formData: FormData,
): Promise<CreateShiftState> {
  const breakStart = formData.get("breakStart");
  const breakEnd = formData.get("breakEnd");
  const allowedBreakMinutes = formData.get("allowedBreakMinutes");

  try {
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
  } catch (err) {
    if (err instanceof BackendError && err.status === 400) {
      return { error: "Could not add shift: please check the shift details and try again." };
    }
    throw err;
  }

  revalidatePath("/shifts");
  return null;
}

export async function deleteShift(id: string) {
  await backendFetch(`/api/shifts/${id}`, { method: "DELETE", token: await getToken() });
  revalidatePath("/shifts");
}
