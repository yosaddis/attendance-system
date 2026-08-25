import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("next/navigation", () => ({ usePathname: () => "/employees" }));

import { MobileTabBar } from "./MobileTabBar";
import { NAV_LINKS } from "./navLinksData";

describe("MobileTabBar", () => {
  it("renders an icon for every link", () => {
    render(<MobileTabBar />);

    for (const link of NAV_LINKS) {
      const linkEl = screen.getByRole("link", { name: link.mobileLabel ?? link.label });
      expect(linkEl.querySelector("svg")).not.toBeNull();
    }
  });

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
