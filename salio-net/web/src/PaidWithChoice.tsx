// How a supplier was paid: three buttons, one chosen. Shared by Stock in,
// the import screen and the invoice commit, so a delivery is described the
// same way everywhere.

import { PAID_WITH } from "./paidWith";

// No default on purpose. Booking a credit delivery as cash takes money out
// of the drawer that never left it, so the shopkeeper has to choose.
export default function PaidWithChoice({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (value: number) => void;
}) {
  return (
    <div>
      <span className="block text-sm font-semibold">Paid with</span>
      <div className="mt-1 grid grid-cols-3 gap-1.5">
        {PAID_WITH.map((option) => (
          <button
            key={option.value}
            type="button"
            onClick={() => onChange(option.value)}
            aria-pressed={value === option.value}
            className={
              "min-h-12 rounded-lg text-base font-bold " +
              (value === option.value
                ? "bg-primary text-primary-foreground"
                : "border-2 border-border-strong bg-surface text-foreground")
            }
          >
            {option.label}
          </button>
        ))}
      </div>
    </div>
  );
}
