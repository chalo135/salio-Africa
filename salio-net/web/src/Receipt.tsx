import { kes, paymentName, qty } from "./auth";

export type ReceiptLine = {
  name: string;
  sku?: string;
  quantity: number;
  unitPriceMinor: number;
};

export type ReceiptPayment = {
  method: string; // "Cash", "Mpesa"
  amountMinor: number;
};

// One receipt, used in two places: straight after a sale at the till, and
// again from the sales list for any past sale. Do not write a second one.
//
// The cashier turns the phone round to show the customer, so everything is
// sized to be read at arm's length: no small text, no grey text.
export default function Receipt({
  shopName,
  saleNumber,
  occurredAt,
  lines,
  payments,
  totalMinor,
  soldBy,
  customerName,
  customerBalanceMinor,
  justSold = false,
  onDone,
}: {
  shopName: string;
  saleNumber: string;
  occurredAt: string;
  lines: ReceiptLine[];
  payments: ReceiptPayment[];
  // The server's total, not one added up here: it is the figure the sale
  // was booked at.
  totalMinor: number;
  soldBy?: string | null;
  // A credit sale: who owes it, and what they owe now with this sale.
  // The balance is only known straight after the sale, at the till.
  customerName?: string | null;
  customerBalanceMinor?: number | null;
  // Straight after a sale at the till, not a past sale opened from the list:
  // the receipt rises into place and a check mark draws once, the sign that
  // the money landed.
  justSold?: boolean;
  onDone: () => void;
}) {
  const when = new Date(occurredAt).toLocaleString("en-KE", {
    weekday: "short",
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });

  return (
    <div className={"flex h-full flex-col bg-background text-foreground" + (justSold ? " motion-rise" : "")}>
      <header className="shrink-0 border-b-2 border-border bg-surface px-4 py-4 text-center">
        {justSold && (
          <svg
            width="40"
            height="40"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
            strokeLinecap="round"
            strokeLinejoin="round"
            className="mx-auto mb-2 text-primary"
            role="img"
            aria-label="Sale saved"
          >
            <circle cx="12" cy="12" r="10.5" strokeWidth="1.5" />
            <path className="motion-check" d="M7 12.5l3.5 3.5L17 9" />
          </svg>
        )}
        <p className="text-xl font-bold">{shopName}</p>
        <p className="mt-1 font-money text-lg font-bold">{saleNumber}</p>
        <p className="mt-1 font-money text-base">{when}</p>
      </header>

      <main className="min-h-0 flex-1 overflow-y-auto bg-surface">
        {lines.map((line, index) => (
          <div key={index} className="border-b border-border px-4 py-3">
            <p className="text-lg font-semibold">{line.name}</p>
            {line.sku !== undefined && <p className="text-base">{line.sku}</p>}
            <div className="mt-1 flex items-baseline justify-between gap-3 text-lg">
              <span className="font-money">
                {qty(line.quantity)} × {kes(line.unitPriceMinor)}
              </span>
              <span className="font-money font-bold">
                {kes(Math.round(line.quantity * line.unitPriceMinor))}
              </span>
            </div>
          </div>
        ))}
      </main>

      <section className="shrink-0 border-t-2 border-border bg-surface px-4 py-3">
        <div className="flex items-baseline justify-between">
          <span className="text-xl font-bold">Total</span>
          <span className="font-money text-2xl font-bold">KES {kes(totalMinor)}</span>
        </div>

        <div className="mt-2 space-y-1 border-t border-border pt-2">
          {payments.map((payment, index) => (
            <div key={index} className="flex justify-between text-lg">
              <span>
                {payment.method === "Credit" ? "On credit" : "Paid " + paymentName(payment.method)}
              </span>
              <span className="font-money font-semibold">{kes(payment.amountMinor)}</span>
            </div>
          ))}
        </div>

        {/* Credit is said plainly, with the name, so nobody reads this as
            paid. The balance is what the customer owes now, in total. */}
        {customerName != null && (
          <div className="mt-2 rounded-xl border-2 border-warning-strong px-3 py-2">
            <p className="text-lg font-bold">CREDIT: not paid</p>
            <p className="text-lg">Owed by {customerName}</p>
            {customerBalanceMinor != null && (
              <div className="mt-1 flex items-baseline justify-between">
                <span className="text-lg font-semibold">Total owed</span>
                <span className="font-money text-xl font-bold">KES {kes(customerBalanceMinor)}</span>
              </div>
            )}
          </div>
        )}

        {soldBy != null && <p className="mt-2 text-base">Served by {soldBy}</p>}

        <button
          onClick={onDone}
          className="mt-3 h-14 w-full rounded-xl bg-primary text-xl font-bold text-primary-foreground"
        >
          Done
        </button>
      </section>
    </div>
  );
}
