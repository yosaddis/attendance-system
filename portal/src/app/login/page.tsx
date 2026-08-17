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
    <div className="min-h-screen flex items-center justify-center p-4">
      <form action={login} className="w-full max-w-sm space-y-4 border rounded p-6">
        <h1 className="text-xl font-semibold">ZAK Attendance — Sign in</h1>
        {errorMessage && <p className="text-red-600 text-sm">{errorMessage}</p>}
        <label className="flex flex-col text-sm gap-1">
          Email
          <input type="email" name="email" required className="border rounded px-2 py-1" />
        </label>
        <label className="flex flex-col text-sm gap-1">
          Password
          <input type="password" name="password" required className="border rounded px-2 py-1" />
        </label>
        <button type="submit" className="w-full bg-blue-600 text-white rounded px-3 py-2">
          Sign in
        </button>
      </form>
    </div>
  );
}
