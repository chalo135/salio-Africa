import { useState, useEffect, useRef } from "react";

// Four dots and a keypad. Calls onComplete as soon as the 4th digit goes in.
// To clear it (a wrong PIN, starting over), the parent gives it a new key,
// which starts a fresh pad.
//
// Keys are big on purpose: the people using this are in their fifties, on
// a phone, often at a busy counter.
export default function PinPad({
  onComplete,
  disabled = false,
}: {
  onComplete: (pin: string) => void;
  disabled?: boolean;
}) {
  const [digits, setDigits] = useState("");

  // The same digits, read by key presses. Two keys typed before the screen
  // redraws would both see the old state and one would be lost.
  const typed = useRef("");

  function change(next: string) {
    typed.current = next;
    setDigits(next);
  }

  function press(digit: string) {
    if (disabled || typed.current.length >= 4) {
      return;
    }

    const next = typed.current + digit;
    change(next);

    if (next.length === 4) {
      onComplete(next);
    }
  }

  function remove() {
    if (!disabled) {
      change(typed.current.slice(0, -1));
    }
  }

  // A laptop keyboard works too: digits and Backspace. Subscribed again on
  // every render, so the handler always sees the current props.
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key.length === 1 && event.key >= "0" && event.key <= "9") {
        press(event.key);
      } else if (event.key === "Backspace") {
        remove();
      }
    }

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  const keyClass =
    "h-16 rounded-xl border-2 border-border-strong bg-surface font-bold active:bg-primary-soft disabled:opacity-40";

  return (
    <div className="mx-auto w-full max-w-xs">
      <div className="mb-6 flex justify-center gap-4" aria-hidden="true">
        {[0, 1, 2, 3].map((i) => (
          <span
            key={i}
            className={
              "h-5 w-5 rounded-full border-2 " +
              (i < digits.length ? "border-foreground bg-foreground" : "border-border-strong")
            }
          />
        ))}
      </div>
      <p className="sr-only" aria-live="polite">
        {digits.length} of 4 digits entered
      </p>

      <div className="grid grid-cols-3 gap-3">
        {["1", "2", "3", "4", "5", "6", "7", "8", "9"].map((digit) => (
          <button
            key={digit}
            type="button"
            onClick={() => press(digit)}
            disabled={disabled}
            className={keyClass + " text-2xl"}
          >
            {digit}
          </button>
        ))}
        <span />
        <button
          type="button"
          onClick={() => press("0")}
          disabled={disabled}
          className={keyClass + " text-2xl"}
        >
          0
        </button>
        <button type="button" onClick={remove} disabled={disabled} className={keyClass + " text-lg"}>
          Delete
        </button>
      </div>
    </div>
  );
}
