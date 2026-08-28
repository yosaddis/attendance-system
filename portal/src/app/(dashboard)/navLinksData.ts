import { Calendar, Clock, FileBarChart, LayoutDashboard, Users, type LucideIcon } from "lucide-react";

export type NavLink = { href: string; label: string; mobileLabel?: string; icon: LucideIcon };

export const NAV_LINKS: NavLink[] = [
  { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { href: "/attendance", label: "Attendance", icon: Clock },
  { href: "/employees", label: "Employees", mobileLabel: "Staff", icon: Users },
  { href: "/shifts", label: "Shifts", icon: Calendar },
  { href: "/reports", label: "Reports", icon: FileBarChart },
];
