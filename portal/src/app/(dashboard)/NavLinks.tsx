"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const NAV_LINKS = [
  { href: "/attendance", label: "Attendance" },
  { href: "/employees", label: "Employees" },
  { href: "/shifts", label: "Shifts" },
];

export function NavLinks() {
  const pathname = usePathname();

  return (
    <div className="flex gap-4">
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
