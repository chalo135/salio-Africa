import { kes } from "./auth";
import { belowLowest, lowerInterval, priceSteps } from "./priceSteps";

// The price of one cart line, tapped, never typed. ASKING first, STOP last
// (only when a stop is known), the chosen one filled green as AGREED. LOWER
// goes one gap further down per tap: the line turns red and warns, but the
// sale is never blocked - a till that refuses a sale gets worked around.
export default function CartPriceButtons({
  name,
  askingMinor,
  stopMinor,
  agreedMinor,
  costMinor,
  onAgree,
}: {
  name: string;
  askingMinor: number;
  stopMinor: number | null;
  agreedMinor: number;
  // Only when the server sent the buying price. Absent: the line is absent.
  costMinor: number | null | undefined;
  onAgree: (priceMinor: number) => void;
}) {
  const steps = priceSteps(askingMinor, stopMinor);
  const lowest = steps[steps.length - 1];
  const hasStopButton = stopMinor !== null && steps.length >= 2;
  const below = belowLowest(askingMinor, stopMinor, agreedMinor);

  // One gap below whichever is lower: the lowest button or the price now.
  const lowerFrom = Math.min(agreedMinor, lowest);
  const nextLower = lowerFrom - lowerInterval(steps);
  const canLower = nextLower >= 0;

  const showCost = costMinor !== null && costMinor !== undefined && costMinor > 0;
  const keep = showCost ? agreedMinor - costMinor : 0;

  return (
    <div className="mt-1">
      <div className="flex flex-wrap gap-1" role="group" aria-label={"Price for " + name}>
        {steps.map((price, index) => {
          const agreed = price === agreedMinor;
          let label = "";
          if (agreed) {
            label = "AGREED";
          } else if (index === 0) {
            label = "ASKING";
          } else if (hasStopButton && index === steps.length - 1) {
            label = "STOP";
          }

          return (
            <button
              key={price}
              onClick={() => onAgree(price)}
              aria-pressed={agreed}
              className={
                "min-h-11 min-w-16 rounded-lg border-2 px-2 leading-tight " +
                (agreed
                  ? "border-primary bg-primary text-primary-foreground"
                  : "border-border-strong bg-surface")
              }
            >
              <span className="block text-[11px] font-bold">{label || " "}</span>
              <span className="block font-money text-sm font-semibold">{kes(price)}</span>
            </button>
          );
        })}

        {/* A price under every button: shown as the agreed one, in red. */}
        {below && (
          <span className="min-h-11 min-w-16 rounded-lg border-2 border-error bg-error px-2 leading-tight text-error-foreground">
            <span className="block text-[11px] font-bold">AGREED</span>
            <span className="block font-money text-sm font-semibold">{kes(agreedMinor)}</span>
          </span>
        )}

        {canLower && (
          <button
            onClick={() => onAgree(nextLower)}
            className="min-h-11 rounded-lg border-2 border-error px-2 text-sm font-bold text-error"
          >
            LOWER ↓
          </button>
        )}
      </div>

      {below && (
        <p className="mt-1 text-sm font-semibold text-error">
          {stopMinor !== null ? "Below the stop price" : "Below the asking price"}
        </p>
      )}

      {/* The oil case: the shop's own margin puts the stop over the
          asking price, so even the full price is under it. */}
      {stopMinor !== null && stopMinor > askingMinor && (
        <p className="mt-1 text-sm font-semibold text-warning-strong">
          Asking is under the stop ({kes(stopMinor)})
        </p>
      )}

      {showCost && (
        <p className="mt-1 text-sm text-muted-foreground">
          You bought it at {kes(costMinor)} · at {kes(agreedMinor)} you {keep >= 0 ? "keep" : "lose"}{" "}
          {kes(keep >= 0 ? keep : -keep)}
        </p>
      )}
    </div>
  );
}
