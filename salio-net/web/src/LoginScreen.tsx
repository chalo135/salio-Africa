import { useState } from "react";
import { API, type SignedInUser } from "./auth";

export default function LoginScreen({
  onSignedIn,
  onCreateShop,
}: {
  onSignedIn: (user: SignedInUser) => void;
  // Only passed in development, so this link cannot exist in a real build.
  onCreateShop?: () => void;
}) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [problem, setProblem] = useState("");
  const [busy, setBusy] = useState(false);

  async function signIn(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setProblem("");

    try {
      const response = await fetch(`${API}/api/auth/login`, {
        method: "POST",
        credentials: "include", // stores the login cookie
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });

      if (response.ok) {
        onSignedIn(await response.json());
        return;
      }

      // The server says the same thing for a wrong email and a wrong
      // password on purpose. Show it as it came.
      const body = await response.json().catch(() => null);
      setProblem(body?.detail ?? "Could not sign in.");
    } catch {
      setProblem("Could not reach the till server. Is the API running?");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-dvh flex-col justify-center bg-background px-4 text-foreground">
      <form onSubmit={signIn} className="mx-auto w-full max-w-sm">
        <h1 className="mb-6 text-2xl font-bold">Salio</h1>

        <label className="mb-1 block text-sm font-semibold" htmlFor="email">
          Email
        </label>
        <input
          id="email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          // A phone keyboard capitalises the first letter, and the server
          // matches email exactly as it was created.
          autoCapitalize="none"
          autoCorrect="off"
          autoComplete="username"
          className="mb-4 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg"
        />

        <label className="mb-1 block text-sm font-semibold" htmlFor="password">
          Password
        </label>
        {/* Show lets the cashier check what they typed on a phone keyboard,
            where one wrong tap is easy to miss behind the dots. */}
        <div className="relative mb-4">
          <input
            id="password"
            type={showPassword ? "text" : "password"}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            autoCapitalize="none"
            autoCorrect="off"
            className="h-12 w-full rounded-lg border-2 border-border-strong bg-surface pl-3 pr-20 text-lg"
          />
          <button
            type="button"
            onClick={() => setShowPassword(!showPassword)}
            aria-pressed={showPassword}
            aria-controls="password"
            className="absolute inset-y-0 right-0 min-w-16 px-3 text-base font-bold text-primary"
          >
            {showPassword ? "Hide" : "Show"}
          </button>
        </div>

        {problem !== "" && (
          <p className="mb-4 text-base font-semibold text-error">{problem}</p>
        )}

        <button
          type="submit"
          disabled={busy || email === "" || password === ""}
          className="h-14 w-full rounded-xl bg-primary text-xl font-bold
            text-primary-foreground disabled:opacity-40"
        >
          {busy ? "Signing in…" : "Sign in"}
        </button>

        {onCreateShop !== undefined && (
          <button
            type="button"
            onClick={onCreateShop}
            className="mt-3 min-h-11 w-full rounded-lg border-2 border-border-strong
              text-base font-bold"
          >
            Create a shop (dev)
          </button>
        )}
      </form>
    </div>
  );
}
