"use server";

import { revalidatePath } from "next/cache";
import { BackendError, backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";

export type CreateEmployeeState = { error: string } | null;

export async function createEmployee(
  _prevState: CreateEmployeeState,
  formData: FormData,
): Promise<CreateEmployeeState> {
  const shiftId = formData.get("shiftId");

  try {
    await backendFetch("/api/employees", {
      method: "POST",
      token: await getToken(),
      body: JSON.stringify({
        employeeCode: String(formData.get("employeeCode")),
        name: String(formData.get("name")),
        shiftId: shiftId ? String(shiftId) : null,
      }),
    });
  } catch (err) {
    if (err instanceof BackendError && err.status === 400) {
      return { error: "Could not add employee: that employee code is already in use." };
    }
    throw err;
  }

  revalidatePath("/employees");
  return null;
}

export async function deleteEmployee(id: string) {
  await backendFetch(`/api/employees/${id}`, { method: "DELETE", token: await getToken() });
  revalidatePath("/employees");
}
