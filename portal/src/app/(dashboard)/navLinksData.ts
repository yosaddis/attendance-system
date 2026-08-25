export type NavLink = { href: string; label: string; mobileLabel?: string };

export const NAV_LINKS: NavLink[] = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/attendance", label: "Attendance" },
  { href: "/employees", label: "Employees", mobileLabel: "Staff" },
  { href: "/shifts", label: "Shifts" },
  { href: "/reports", label: "Reports" },
];
