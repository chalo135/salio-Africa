import { useState } from "react";
import { API, type SignedInUser } from "./auth";
import { apiFetch } from "./api";

// The only screen a person on a temporary password ever sees. No tabs, no
// back button, no skip: the server refuses everything else until this is
// done (PasswordChangeGate), so there is nowhere else to go.
//
// Done once, often with the owner watching, so everything is large and
// every message says exactly what to do next.
export default function ChoosePasswordScreen({
  userName,
  onChanged,
  onExpired,
}: {
  userName: string;
  onChanged: (user: SignedInUser) => void;
  onExpired: () => void;
}) {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState("");

  async function save(event: React.FormEvent) {
    event.preventDefault();

    if (busy) {
      return;
    }

    if (current === "") {
      setProblem("Type the temporary password.");
      return;
    }
    if (next.length < 8) {
      setProblem("At least 8 characters");
      return;
    }
    if (next !== confirm) {
      setProblem("New passwords don't match.");
      return;
    }

    setBusy(true);
    setProblem("");

    try {
      const response = await apiFetch(`${API}/api/auth/change-password`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ currentPassword: current, newPassword: next }),
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        // Wrong current password, too many tries: the server says which.
        const body = await response.json().catch(() => null);
        setProblem(body?.detail ?? "Password not changed.");
        return;
      }

      onChanged(await response.json());
    } catch {
      setProblem("Can't reach Salio. Not changed.");
    } finally {
      setBusy(false);
    }
  }

  const fieldClass =
    "mt-1 h-16 w-full rounded-xl border-2 border-border-strong bg-surface px-4 text-2xl " +
    "focus:border-primary focus:outline-none";

  return (
    <div className="min-h-full bg-background px-4 py-6 text-foreground">
      <form onSubmit={save} className="mx-auto w-full max-w-md">
        <h1 className="text-2xl font-bold">Choose password</h1>
        <p className="mt-2 text-lg">
          Welcome, {userName}
        </p>

        <label className="mt-6 block text-xl md:text-base font-bold">
          Temporary password
          <input
            type={show ? "text" : "password"}
            value={current}
            onChange={(e) => {
              setCurrent(e.target.value);
              setProblem("");
            }}
            autoComplete="current-password"
            autoCapitalize="none"
            autoCorrect="off"
            className={fieldClass}
          />
        </label>

        <label className="mt-5 block text-xl md:text-base font-bold">
          New password
          <span className="block text-lg font-normal">At least 8 characters</span>
          <input
            type={show ? "text" : "password"}
            value={next}
            onChange={(e) => {
              setNext(e.target.value);
              setProblem("");
            }}
            autoComplete="new-password"
            autoCapitalize="none"
            autoCorrect="off"
            className={fieldClass}
          />
        </label>

        <label className="mt-5 block text-xl md:text-base font-bold">
          Confirm password
          <input
            type={show ? "text" : "password"}
            value={confirm}
            onChange={(e) => {
              setConfirm(e.target.value);
              setProblem("");
            }}
            autoComplete="new-password"
            autoCapitalize="none"
            autoCorrect="off"
            className={fieldClass}
          />
        </label>

        <button
          type="button"
          onClick={() => setShow(!show)}
          aria-pressed={show}
          className="mt-4 min-h-12 rounded-lg px-1 text-lg font-bold text-primary"
        >
          {show ? "Hide passwords" : "Show passwords"}
        </button>

        {problem !== "" && (
          <p role="alert" className="mt-4 rounded-xl bg-error px-4 py-3 text-lg font-semibold text-error-foreground">
            {problem}
          </p>
        )}

        <button
          type="submit"
          disabled={busy}
          className="mt-6 h-16 w-full rounded-xl bg-primary text-2xl font-bold text-primary-foreground disabled:opacity-40"
        >
          {busy ? "Saving…" : "Save"}
        </button>
      </form>
    </div>
  );
}
