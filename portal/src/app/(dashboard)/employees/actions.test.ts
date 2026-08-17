import { describe, it, expect, vi, beforeEach } from "vitest";

const cookieStore = vi.hoisted(() => {
  const store = new Map<string, string>();
  return {
    get: (name: string) => (store.has(name) ? { name, value: store.get(name)! } : undefined),
    set: (name: string, value: string) => {
      store.set(name, value);
    },
  };
});

vi.mock("next/headers", () => ({ cookies: () => cookieStore }));
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));

import { revalidatePath } from "next/cache";
import { SESSION_COOKIE } from "@/lib/constants";
import { createEmployee, deleteEmployee } from "./actions";

describe("employee actions", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    cookieStore.set(SESSION_COOKIE, "jwt-abc");
  });

  it("posts employee data with a null shiftId when none is selected", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "e1" }), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("employeeCode", "E001");
    formData.set("name", "Jane Doe");

    const state = await createEmployee(null, formData);

    const [, init] = fetchMock.mock.calls[0];
    const body = JSON.parse(init.body as string);
    expect(body).toEqual({ employeeCode: "E001", name: "Jane Doe", shiftId: null });
    expect(revalidatePath).toHaveBeenCalledWith("/employees");
    expect(state).toBeNull();
  });

  it("posts the selected shiftId when provided", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "e1" }), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("employeeCode", "E001");
    formData.set("name", "Jane Doe");
    formData.set("shiftId", "s1");

    await createEmployee(null, formData);

    const [, init] = fetchMock.mock.calls[0];
    const body = JSON.parse(init.body as string);
    expect(body.shiftId).toBe("s1");
  });

  it("returns an error state instead of throwing when the backend rejects a duplicate employee code", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("duplicate employee code", { status: 400 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("employeeCode", "E001");
    formData.set("name", "Jane Doe");

    const state = await createEmployee(null, formData);

    expect(state?.error).toMatch(/already in use/i);
    expect(revalidatePath).not.toHaveBeenCalled();
  });

  it("rethrows non-400 backend errors", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("boom", { status: 500 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("employeeCode", "E001");
    formData.set("name", "Jane Doe");

    await expect(createEmployee(null, formData)).rejects.toThrow();
    expect(revalidatePath).not.toHaveBeenCalled();
  });

  it("deletes an employee by id and revalidates", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await deleteEmployee("e1");

    expect(String(fetchMock.mock.calls[0][0])).toContain("/api/employees/e1");
    expect(revalidatePath).toHaveBeenCalledWith("/employees");
  });
});
