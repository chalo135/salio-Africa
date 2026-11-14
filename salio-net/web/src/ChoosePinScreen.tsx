import { useState } from "react";
import { API } from "./auth";
import PinPad from "./PinPad";
import { apiFetch } from "./api";

// Offered after a password login on a phone with no PIN yet. Typed
// twice, so a slip of the finger does not become a PIN nobody knows.
export default function ChoosePinScreen({
  onChosen,
  onExpired,
}: {
  onChosen: () => void;
  onExpired: () => void;
}) {
  // The first entry, while the second is being typed. Empty means "first".
  const [firstPin, setFirstPin] = useState("");
  const [problem, setProblem] = useState("");
  const [busy, setBusy] = useState(false);

  // A new number gives a fresh, empty pad.
  const [padKey, setPadKey] = useState(0);

  // True if the server will not take a PIN from this phone at all. Its
  // reason is shown instead of the "next time" line.
  const [refused, setRefused] = useState(false);

  function startOver(message: string) {
    setFirstPin("");
    setProblem(message);
    setPadKey(padKey + 1);
  }

  async function entered(pin: string) {
    if (firstPin === "") {
      setFirstPin(pin);
      setProblem("");
      setPadKey(padKey + 1);
      return;
    }

    if (pin !== firstPin) {
      startOver("PINs didn't match");
      return;
    }

    setBusy(true);

    try {
      const response = await apiFetch(`${API}/api/auth/pin`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ pin }),
      });

      if (response.ok) {
        onChosen();
        return;
      }

      if (response.status === 401) {
        onExpired();
        return;
      }

      const body = await response.json().catch(() => null);

      if (response.status === 403) {
        setRefused(true);
      }

      startOver(body?.detail ?? "PIN not saved.");
    } catch {
      startOver("Can't reach Salio.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-full flex-col justify-center bg-background px-4 py-6 text-foreground">
      <h1 className="text-center text-2xl font-bold">
        {firstPin === "" ? "Choose PIN" : "Repeat PIN"}
      </h1>
      <p className="mb-6 mt-2 text-center text-base md:text-sm text-muted-foreground">
        Four digits, for this phone
      </p>

      {problem !== "" && (
        <p role="alert" className="mb-4 text-center text-base font-semibold text-error">
          {problem}
        </p>
      )}

      <PinPad key={padKey} onComplete={entered} disabled={busy} />

      {/* An offer, not a demand: skipping still opens the till. */}
      <button
        type="button"
        onClick={onChosen}
        disabled={busy}
        className="mx-auto mt-6 min-h-11 px-3 text-base font-bold text-primary underline"
      >
        Not now
      </button>
      {!refused && (
        <p className="mt-1 text-center text-sm text-muted-foreground">
          Next time: email and password
        </p>
      )}
    </div>
  );
}
