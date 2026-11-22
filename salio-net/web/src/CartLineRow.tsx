import { useEffect, useRef, useState, type ReactNode } from "react";
import { kes } from "./auth";
import { ladderSteps } from "./priceSteps";

// One cart line, quiet by default:
//
//   Battery 12V          [−]  1  [+]        1,500.00      ×
//   Last bought at 1,350 · you keep 150
//
// Nothing is typed. The quantity is − and +, never below 1. Tapping the
// price opens the ladder; tapping a step sets the price and closes it.
// Going under the stop is allowed and is not announced.
export default function CartLineRow({
  name,
  quantity,
  agreedMinor,
  askingMinor,
  stopMinor,
  costMinor,
  canSeeCost,
  note,
  onQuantity,
  onPrice,
  onRemove,
}: {
  name: string;
  quantity: number;
  agreedMinor: number;
  askingMinor: number;
  // Already clamped: null when there is none, or it is not under asking.
  stopMinor: number | null;
  // What a person last paid for one, only when this person may see it.
  costMinor: number | null | undefined;
  // CostVisibilityRules.CanSeeCost, as the server answered it.
  canSeeCost: boolean;
  // Anything the line must still say (stock below zero, rule 16).
  note: ReactNode;
  onQuantity: (quantity: number) => void;
  onPrice: (priceMinor: number) => void;
  onRemove: () => void;
}) {
  const [ladderOpen, setLadderOpen] = useState(false);

  // On a phone the cart shows only a few lines' height, so an opened ladder
  // could sit below what is in view. Bring it in.
  const ladderRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (ladderOpen) {
      ladderRef.current?.scrollIntoView({ block: "nearest" });
    }
  }, [ladderOpen]);

  const steps = ladderSteps(askingMinor, stopMinor);

  // Someone who cannot see the buying price still needs to know where to
  // stop. Someone who can has the better number already.
  const markStop = !canSeeCost && stopMinor !== null;

  const showCost = canSeeCost && costMinor !== null && costMinor !== undefined && costMinor > 0;
  const keep = showCost ? agreedMinor - costMinor : 0;

  return (
    <div className="motion-line-in border-b border-border px-2 py-1.5">
      <div className="flex items-center gap-1.5">
        <span className="block min-w-0 flex-1 truncate text-base">{name}</span>

        <button
          onClick={() => onQuantity(Math.max(1, quantity - 1))}
          disabled={quantity <= 1}
          aria-label={"One fewer " + name}
          className="h-11 w-10 shrink-0 rounded-lg border-2 border-border-strong text-xl font-bold disabled:opacity-40"
        >
          −
        </button>
        <span className="w-7 shrink-0 text-center font-money text-base font-semibold" aria-label="Quantity">
          {quantity}
        </span>
        <button
          onClick={() => onQuantity(quantity + 1)}
          aria-label={"One more " + name}
          className="h-11 w-10 shrink-0 rounded-lg border-2 border-border-strong text-xl font-bold"
        >
          +
        </button>

        <button
          onClick={() => setLadderOpen(!ladderOpen)}
          aria-expanded={ladderOpen}
          aria-label={"Price for " + name + ": " + kes(agreedMinor) + " each"}
          className="h-11 min-w-20 shrink-0 rounded-lg border border-border px-1.5 text-right font-money text-base font-semibold"
        >
          {kes(agreedMinor * quantity)}
        </button>

        <button
          onClick={onRemove}
          className="h-11 w-11 shrink-0 rounded-lg border-2 border-border-strong text-xl font-bold"
          aria-label={"Remove " + name}
        >
          ×
        </button>
      </div>

      {showCost && (
        <p className="mt-0.5 text-sm text-muted-foreground">
          Last bought at <span className="font-money">{kes(costMinor)}</span> · you keep{" "}
          <span className={"font-money" + (keep < 0 ? " text-error" : "")}>{kes(keep)}</span>
        </p>
      )}

      {note}

      {ladderOpen && (
        <div ref={ladderRef} className="mt-1.5 flex flex-wrap gap-1" role="group" aria-label={"Prices for " + name}>
          {steps.map((price) => {
            const chosen = price === agreedMinor;
            const isStop = markStop && price === stopMinor;

            return (
              <button
                key={price}
                onClick={() => {
                  onPrice(price);
                  setLadderOpen(false);
                }}
                aria-pressed={chosen}
                className={
                  "min-h-11 min-w-16 rounded-lg border-2 px-2 leading-tight " +
                  (chosen ? "border-primary bg-primary text-primary-foreground" : "border-border-strong bg-surface")
                }
              >
                <span className="block font-money text-sm font-semibold">{kes(price)}</span>
                {isStop && <span className="block text-[11px] opacity-70">stop</span>}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
