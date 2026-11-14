import { useState } from "react";
import { API, type SignedInUser } from "./auth";
import PinPad from "./PinPad";
import { apiFetch } from "./api";

// The server's own sentence, if it sent one. Never anything else from the
// answer: no codes, no raw text.
function plainDetail(body: { detail?: unknown } | null) {
  return typeof body?.detail === "string" && body.detail !== "" && body.detail.length < 300 ? body.detail : null;
}

// A trusted phone whose login has ended: the person's PIN opens it again.
// Five wrong PINs and the server forgets this phone; then it is email and
// password.
//
// Each kind of failure says what actually happened. A wrong PIN is only
// ever called a wrong PIN when the server said so; a broken server or a
// lost connection never pretends to be one.
export default function PinUnlockScreen({
  name,
  locked = false,
  onUnlocked,
  onUsePassword,
}: {
  name: string;
  // True when this is the lock screen: the till and its carts are kept
  // behind it, and say so.
  locked?: boolean;
  onUnlocked: (user: SignedInUser) => void;
  // The message the password screen should show, or "" for none.
  onUsePassword: (message: string) => void;
}) {
  const [problem, setProblem] = useState("");
  const [busy, setBusy] = useState(false);
  const [padKey, setPadKey] = useState(0);

  // Tries left after the last wrong PIN, or null before any. When it was 1
  // and the server then says "use your password", the PINs ran out.
  const [triesLeft, setTriesLeft] = useState<number | null>(null);

  function tryAgain(message: string) {
    setProblem(message);
    setPadKey(padKey + 1);
  }

  async function unlock(pin: string) {
    setBusy(true);

    try {
      const response = await apiFetch(
        `${API}/api/auth/pin-unlock`,
        {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ pin }),
        },
        // Give up after 20 seconds rather than leave the pad dead.
        20000,
      );

      if (response.ok) {
        onUnlocked(await response.json());
        return;
      }

      const body = await response.json().catch(() => null);

      // The server no longer trusts this phone: too many wrong PINs, or it
      // was signed out, or the person was removed. Only the password helps.
      if (response.status === 401 && body?.signInWithPassword) {
        onUsePassword(
          triesLeft === 1
            ? "Too many wrong PINs"
            : "Sign in with your password",
        );
        return;
      }

      if (response.status === 401 && typeof body?.attemptsLeft === "number") {
        const left: number = body.attemptsLeft;
        setTriesLeft(left);
        tryAgain(`Wrong PIN. ${left} ${left === 1 ? "try" : "tries"} left.`);
        return;
      }

      if (response.status === 429) {
        tryAgain(plainDetail(body) ?? "Wait a minute, then retry");
        return;
      }

      // 500 and up: the server broke while checking. Not a wrong PIN, and
      // no try was used up.
      if (response.status >= 500) {
        tryAgain(
          plainDetail(body) ??
            "Salio had a problem. Not a wrong PIN.",
        );
        return;
      }

      tryAgain(plainDetail(body) ?? "That didn't work. Not a wrong PIN.");
    } catch {
      // The request never reached the server.
      tryAgain("Can't reach Salio. Not a wrong PIN.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-full flex-col justify-center bg-background px-4 py-6 text-foreground">
      <p className="text-center text-base text-muted-foreground">
        {locked ? "Locked. Carts are kept." : "Salio"}
      </p>
      <h1 className="mb-6 text-center text-2xl font-bold">{name}</h1>

      {problem !== "" && (
        <p role="alert" className="mb-4 text-center text-base font-semibold text-error">
          {problem}
        </p>
      )}

      <PinPad key={padKey} onComplete={unlock} disabled={busy} />

      <button
        type="button"
        onClick={() => onUsePassword("")}
        className="mx-auto mt-6 min-h-11 px-3 text-base font-bold text-primary underline"
      >
        Email instead
      </button>
    </div>
  );
}
