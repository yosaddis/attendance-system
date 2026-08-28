"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { NAV_LINKS } from "./navLinksData";

export function NavLinks() {
  const pathname = usePathname();

  return (
    <div className="hidden md:flex gap-4">
      {NAV_LINKS.map((link) => {
        const isActive = pathname.startsWith(link.href);
        return (
          <Link
            key={link.href}
            href={link.href}
            className={
              isActive
                ? "text-accent border-b-2 border-accent pb-1"
                : "text-surface/80 hover:text-accent pb-1 border-b-2 border-transparent"
            }
          >
            {link.label}
          </Link>
        );
      })}
    </div>
  );
}
