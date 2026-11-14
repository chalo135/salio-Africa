import { useState } from "react";
import { API, type LoginResult } from "./auth";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { apiFetch, problemText, CANT_REACH } from "./api";

export default function LoginScreen({
  message = "",
  locked = false,
  onSignedIn,
}: {
  // Why the person is here, for example after too many wrong PINs.
  message?: string;
  // True when this is the lock screen on a phone with no PIN: the till and
  // its carts are kept behind it.
  locked?: boolean;
  onSignedIn: (user: LoginResult) => void;
}) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [problem, setProblem] = useState(message);
  const [busy, setBusy] = useState(false);

  // Covers the form while the sign-in is waiting. The form stays underneath,
  // so a refused sign-in shows its error with the email still typed.
  const showSigningIn = useShowLoader(busy);

  async function signIn(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setProblem("");

    try {
      const response = await apiFetch(
        `${API}/api/auth/login`,
        {
          method: "POST",
          credentials: "include", // stores the login cookie
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ email, password }),
        },
        // Give up after 20 seconds rather than spin for ever.
        20000,
      );

      if (response.ok) {
        onSignedIn(await response.json());
        return;
      }

      // The server says the same thing for a wrong email and a wrong
      // password on purpose. Show it as it came.
      setProblem(await problemText(response, "Couldn't sign in"));
    } catch {
      setProblem(CANT_REACH);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-full flex-col justify-center bg-background px-4 text-foreground">
      {showSigningIn && <SalioLoader size="full" message="Signing in…" />}

      <form onSubmit={signIn} className="mx-auto w-full max-w-sm">
        <h1 className={locked ? "text-2xl font-bold" : "mb-6 text-2xl font-bold"}>
          {locked ? "Locked" : "Sign in"}
        </h1>
        {locked && (
          <p className="mb-6 mt-1 text-base text-muted-foreground">
            Your carts are kept
          </p>
        )}

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

        {/* Sign in first, always. A new shop is set up from the setup link
            the owner was sent (it carries the code), so this is one quiet
            line, not a button. Not shown on the lock screen. */}
        {!locked && (
          <p className="mt-4 text-center text-sm text-muted-foreground">
            New shop? Use your link.
          </p>
        )}
      </form>
    </div>
  );
}
