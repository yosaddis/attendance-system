import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";

vi.mock("@/lib/backendFetch", () => ({ backendFetch: vi.fn() }));
vi.mock("@/lib/session", () => ({ getToken: vi.fn().mockResolvedValue("jwt-abc") }));

import { backendFetch } from "@/lib/backendFetch";
import EmployeesPage from "./page";

const shifts = [
  {
    id: "s1",
    name: "Day Shift",
    startTime: "09:00:00",
    endTime: "17:00:00",
    graceMinutes: 10,
    punchMode: "TwoPunch",
    breakStart: null,
    breakEnd: null,
    allowedBreakMinutes: null,
  },
];

const employees = [
  { id: "e1", employeeCode: "E001", name: "Jane Doe", shiftId: "s1", hasFingerprint: true },
  { id: "e2", employeeCode: "E002", name: "John Smith", shiftId: null, hasFingerprint: false },
];

describe("EmployeesPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(backendFetch).mockImplementation((path: string) =>
      Promise.resolve(path.startsWith("/api/shifts") ? shifts : employees),
    );
  });

  it("renders in Create mode when there is no edit param", async () => {
    render(await EmployeesPage({ searchParams: Promise.resolve({}) }));

    expect(screen.getByRole("button", { name: /add employee/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /save changes/i })).not.toBeInTheDocument();
  });

  it("prefills the form in Edit mode when edit matches a known employee", async () => {
    render(await EmployeesPage({ searchParams: Promise.resolve({ edit: "e1" }) }));

    expect(screen.getByRole("button", { name: /save changes/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/^name$/i)).toHaveValue("Jane Doe");
  });

  it("falls back to Create mode when edit references an unknown employee id", async () => {
    render(await EmployeesPage({ searchParams: Promise.resolve({ edit: "does-not-exist" }) }));

    expect(screen.getByRole("button", { name: /add employee/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /save changes/i })).not.toBeInTheDocument();
  });

  it("renders Edit links with the correct href in both the desktop table and mobile cards", async () => {
    render(await EmployeesPage({ searchParams: Promise.resolve({}) }));

    const table = screen.getByRole("table");
    const cards = screen.getByTestId("mobile-cards");

    const tableEditLinks = within(table).getAllByRole("link", { name: /edit/i });
    const cardEditLinks = within(cards).getAllByRole("link", { name: /edit/i });

    expect(tableEditLinks.map((l) => l.getAttribute("href"))).toEqual([
      "/employees?edit=e1",
      "/employees?edit=e2",
    ]);
    expect(cardEditLinks.map((l) => l.getAttribute("href"))).toEqual([
      "/employees?edit=e1",
      "/employees?edit=e2",
    ]);
  });

  it("renders a Delete button for each employee in both the desktop table and mobile cards", async () => {
    render(await EmployeesPage({ searchParams: Promise.resolve({}) }));

    const table = screen.getByRole("table");
    const cards = screen.getByTestId("mobile-cards");

    expect(within(table).getAllByRole("button", { name: /delete/i })).toHaveLength(2);
    expect(within(cards).getAllByRole("button", { name: /delete/i })).toHaveLength(2);
  });

  it("shows a Not Enrolled badge only for employees without a fingerprint template", async () => {
    render(await EmployeesPage({ searchParams: Promise.resolve({}) }));

    const table = screen.getByRole("table");
    const cards = screen.getByTestId("mobile-cards");

    expect(within(table).getAllByText(/not enrolled/i)).toHaveLength(1);
    expect(within(cards).getAllByText(/not enrolled/i)).toHaveLength(1);

    // Pin the polarity, not just the count: the badge must appear on John
    // Smith's row/card (hasFingerprint: false) and not on Jane Doe's
    // (hasFingerprint: true).
    const enrolledRow = within(table).getByRole("row", { name: /Jane Doe/i });
    const unenrolledRow = within(table).getByRole("row", { name: /John Smith/i });
    expect(within(enrolledRow).queryByText(/not enrolled/i)).not.toBeInTheDocument();
    expect(within(unenrolledRow).getByText(/not enrolled/i)).toBeInTheDocument();

    const enrolledCardInfo = within(cards).getByText("Jane Doe").parentElement as HTMLElement;
    const unenrolledCardInfo = within(cards).getByText("John Smith").parentElement as HTMLElement;
    expect(within(enrolledCardInfo).queryByText(/not enrolled/i)).not.toBeInTheDocument();
    expect(within(unenrolledCardInfo).getByText(/not enrolled/i)).toBeInTheDocument();
  });
});
