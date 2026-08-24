import Image from "next/image";
import { logout } from "./actions";
import { NavLinks } from "./NavLinks";
import { MobileTabBar } from "./MobileTabBar";

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen flex flex-col">
      <nav className="flex flex-wrap items-center gap-6 bg-ink px-6 py-3">
        <div className="flex items-center gap-2">
          <Image src="/sefed-icon.png" alt="" width={28} height={28} />
          <div className="leading-none">
            <div className="text-surface font-medium text-sm">Sefed</div>
            <div className="font-display text-accent text-[10px] tracking-[0.2em]">ATTENDANCE</div>
          </div>
        </div>
        <NavLinks />
        <form action={logout} className="ml-auto">
          <button type="submit" className="text-sm text-surface/70 hover:text-accent">
            Log out
          </button>
        </form>
      </nav>
      <main className="flex-1 bg-surface-muted pb-20 md:pb-0">{children}</main>
      <MobileTabBar />
    </div>
  );
}
