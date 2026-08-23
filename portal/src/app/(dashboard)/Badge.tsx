export function Badge({ children }: { children: React.ReactNode }) {
  return (
    <span className="inline-block bg-danger-bg text-danger border border-danger/20 rounded px-2 py-0.5 text-xs mr-1">
      {children}
    </span>
  );
}
