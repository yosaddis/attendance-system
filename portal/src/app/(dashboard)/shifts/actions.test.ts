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
import { createShift, deleteShift } from "./actions";

describe("shift actions", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    cookieStore.set(SESSION_COOKIE, "jwt-abc");
  });

  it("posts shift data and revalidates the shifts page", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "s1" }), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("name", "Day Shift");
    formData.set("startTime", "09:00:00");
    formData.set("endTime", "17:00:00");
    formData.set("graceMinutes", "10");
    formData.set("punchMode", "TwoPunch");

    const state = await createShift(null, formData);

    const [, init] = fetchMock.mock.calls[0];
    const body = JSON.parse(init.body as string);
    expect(body).toMatchObject({ name: "Day Shift", startTime: "09:00:00", punchMode: "TwoPunch" });
    expect(revalidatePath).toHaveBeenCalledWith("/shifts");
    expect(state).toBeNull();
  });

  it("returns an error state instead of throwing when the backend rejects the shift", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("bad shift", { status: 400 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("name", "Day Shift");
    formData.set("startTime", "09:00:00");
    formData.set("endTime", "17:00:00");
    formData.set("graceMinutes", "10");
    formData.set("punchMode", "TwoPunch");

    const state = await createShift(null, formData);

    expect(state?.error).toBeTruthy();
    expect(revalidatePath).not.toHaveBeenCalled();
  });

  it("deletes a shift by id and revalidates", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await deleteShift("s1");

    expect(String(fetchMock.mock.calls[0][0])).toContain("/api/shifts/s1");
    expect(revalidatePath).toHaveBeenCalledWith("/shifts");
  });
});
