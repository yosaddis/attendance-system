"use server";

import { revalidatePath } from "next/cache";
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";

export async function createEmployee(formData: FormData) {
  const shiftId = formData.get("shiftId");

  await backendFetch("/api/employees", {
    method: "POST",
    token: await getToken(),
    body: JSON.stringify({
      employeeCode: String(formData.get("employeeCode")),
      name: String(formData.get("name")),
      shiftId: shiftId ? String(shiftId) : null,
    }),
  });
  revalidatePath("/employees");
}

export async function deleteEmployee(id: string) {
  await backendFetch(`/api/employees/${id}`, { method: "DELETE", token: await getToken() });
  revalidatePath("/employees");
}
