import Image from "next/image";
import { login } from "./actions";

export default async function LoginPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { error } = await searchParams;

  const errorMessage =
    error === "unreachable"
      ? "Unable to reach the server. Please try again."
      : error === "role"
        ? "This account doesn't have access to the portal."
        : error
          ? "Invalid email or password."
          : null;

  return (
    <div className="min-h-screen flex items-center justify-center p-4 bg-surface-muted">
      <form
        action={login}
        className="w-full max-w-sm space-y-4 bg-surface border border-border rounded-lg shadow-md p-8"
      >
        <div className="flex flex-col items-center gap-3 mb-2">
          <Image src="/sefed-icon-dark.png" alt="" width={48} height={48} />
          <h1 className="text-ink text-center">
            Sign in to <span className="font-display">Sefed Attendance</span>
          </h1>
        </div>
        {errorMessage && (
          <p className="bg-danger-bg text-danger text-sm rounded-md px-3 py-2 border border-danger/20">
            {errorMessage}
          </p>
        )}
        <label className="flex flex-col text-sm gap-1 text-ink-soft">
          Email
          <input
            type="email"
            name="email"
            required
            className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
          />
        </label>
        <label className="flex flex-col text-sm gap-1 text-ink-soft">
          Password
          <input
            type="password"
            name="password"
            required
            className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
          />
        </label>
        <button
          type="submit"
          className="w-full bg-accent hover:bg-accent-hover text-accent-ink font-medium rounded-md px-3 py-2 transition-colors"
        >
          Sign in
        </button>
      </form>
    </div>
  );
}
