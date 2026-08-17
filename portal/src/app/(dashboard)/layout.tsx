import Link from "next/link";
import { logout } from "./actions";

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen flex flex-col">
      <nav className="flex flex-wrap items-center gap-4 border-b p-4">
        <Link href="/attendance">Attendance</Link>
        <Link href="/employees">Employees</Link>
        <Link href="/shifts">Shifts</Link>
        <form action={logout} className="ml-auto">
          <button type="submit" className="text-sm text-gray-500">
            Log out
          </button>
        </form>
      </nav>
      <main className="flex-1">{children}</main>
    </div>
  );
}
