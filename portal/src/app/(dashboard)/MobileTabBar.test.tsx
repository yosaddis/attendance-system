import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("next/navigation", () => ({ usePathname: () => "/employees" }));

import { MobileTabBar } from "./MobileTabBar";

describe("MobileTabBar", () => {
  it("uses the shorter mobile label when one is provided", () => {
    render(<MobileTabBar />);

    expect(screen.getByRole("link", { name: "Staff" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Employees" })).not.toBeInTheDocument();
  });

  it("highlights the active tab based on the current pathname", () => {
    render(<MobileTabBar />);

    const activeLink = screen.getByRole("link", { name: "Staff" });
    expect(activeLink.className).toContain("text-accent");

    const inactiveLink = screen.getByRole("link", { name: "Dashboard" });
    expect(inactiveLink.className).toContain("text-ink-soft");
  });
});
