"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { NAV_LINKS } from "./navLinksData";

export function MobileTabBar() {
  const pathname = usePathname();

  return (
    <nav
      aria-label="Primary"
      className="md:hidden fixed inset-x-0 bottom-0 bg-surface border-t border-border flex pl-[env(safe-area-inset-left)] pr-[env(safe-area-inset-right)]"
    >
      {NAV_LINKS.map((link) => {
        const isActive = pathname.startsWith(link.href);
        return (
          <Link
            key={link.href}
            href={link.href}
            className={
              "flex-1 text-center text-xs pt-2 pb-[calc(0.5rem+env(safe-area-inset-bottom))] min-h-[44px] " +
              (isActive ? "text-accent font-medium" : "text-ink-soft")
            }
          >
            {link.mobileLabel ?? link.label}
          </Link>
        );
      })}
    </nav>
  );
}
