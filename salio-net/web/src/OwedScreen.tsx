import { useState, useEffect, useRef } from "react";
import { API, kes, paymentName } from "./auth";
import { type Customer } from "./CustomerPicker";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { apiFetch, problemText } from "./api";

type StatementLine = {
  date: string;
  kind: string; // "Sale", "Payment" or "Adjustment"
  reference: string; // sale number, or "Cash" / "Mpesa"
  chargedMinor: number;
  paidMinor: number;
  balanceAfterMinor: number;
};

type Statement = {
  customerId: string;
  name: string;
  phone: string;
  balanceMinor: number;
  lines: StatementLine[];
};

// The server's PaymentMethod numbers.
const PAID_WITH = [
  { value: 0, label: "Cash" },
  { value: 1, label: "M-Pesa" },
];

function dayString(date: Date) {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

// "Mon 19 Oct 2026"
function dayLabel(day: string) {
  const [year, month, date] = day.split("-").map(Number);
  return new Date(year, month - 1, date).toLocaleDateString("en-KE", {
    weekday: "short",
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

// Who owes the shop what. Owner only: App.tsx shows it to an Owner, and the
// statement and payment endpoints refuse a Cashier as well.
export default function OwedScreen({
  shopId,
  onExpired,
}: {
  shopId: string;
  onExpired: () => void;
}) {
  // ---- The list ----
  const [customers, setCustomers] = useState<Customer[] | null>(null);
  const [listProblem, setListProblem] = useState("");
  const [listAttempt, setListAttempt] = useState(0);

  // ---- One customer ----
  const [openId, setOpenId] = useState<string | null>(null);
  const [statement, setStatement] = useState<Statement | null>(null);
  const [statementProblem, setStatementProblem] = useState("");
  const [statementAttempt, setStatementAttempt] = useState(0);

  // ---- Recording a payment ----
  const [amountText, setAmountText] = useState(""); // shillings, as typed
  const [paidWith, setPaidWith] = useState<number | null>(null);
  const [paymentDate, setPaymentDate] = useState(() => dayString(new Date()));
  const [saving, setSaving] = useState(false);
  const savingNow = useRef(false); // a lock that holds before React re-renders
  const [paymentProblem, setPaymentProblem] = useState("");
  const [paymentSaved, setPaymentSaved] = useState("");

  useEffect(() => {
    let cancelled = false;

    apiFetch(`${API}/api/customers?organizationId=${shopId}`, { credentials: "include" })
      .then(async (response) => {
        if (cancelled) return;
        if (response.status === 401) {
          onExpired();
          return;
        }
        if (!response.ok) {
          setListProblem(await problemText(response));
          return;
        }
        setCustomers(await response.json());
      })
      .catch(() => {
        if (!cancelled) setListProblem("Can't reach Salio.");
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, listAttempt]);

  useEffect(() => {
    if (openId === null) {
      return;
    }

    let cancelled = false;

    apiFetch(`${API}/api/customers/${openId}/statement?organizationId=${shopId}`, {
      credentials: "include",
    })
      .then(async (response) => {
        if (cancelled) return;
        if (response.status === 401) {
          onExpired();
          return;
        }
        if (!response.ok) {
          setStatementProblem(await problemText(response));
          return;
        }
        setStatement(await response.json());
      })
      .catch(() => {
        if (!cancelled) setStatementProblem("Can't reach Salio.");
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, openId, statementAttempt]);

  function reloadAll() {
    setStatement(null);
    setStatementProblem("");
    setStatementAttempt(statementAttempt + 1);
    setCustomers(null);
    setListProblem("");
    setListAttempt(listAttempt + 1);
  }

  function open(id: string) {
    setOpenId(id);
    setStatement(null);
    setStatementProblem("");
    setAmountText("");
    setPaidWith(null);
    setPaymentDate(dayString(new Date()));
    setPaymentProblem("");
    setPaymentSaved("");
  }

  async function recordPayment() {
    if (savingNow.current || statement === null) {
      return;
    }

    const amountMinor = Math.round((Number(amountText) || 0) * 100);

    if (!(amountMinor > 0)) {
      setPaymentProblem("Amount must be more than 0.");
      return;
    }
    if (paidWith === null) {
      setPaymentProblem("Choose Cash or M-Pesa.");
      return;
    }

    savingNow.current = true;
    setSaving(true);
    setPaymentProblem("");
    setPaymentSaved("");

    try {
      const response = await apiFetch(`${API}/api/customer-payments`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          organizationId: shopId,
          customerId: statement.customerId,
          amountMinor,
          paymentDate,
          paidWith,
        }),
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        // What was typed stays. The server says the balance if it was too much.
        setPaymentProblem(await problemText(response));
        return;
      }

      const result: { balanceAfterMinor: number } = await response.json();
      const how = PAID_WITH.find((p) => p.value === paidWith)?.label;
      setPaymentSaved(
        `Received KES ${kes(amountMinor)} (${how}) from ${statement.name}. ` +
          `Still owes KES ${kes(result.balanceAfterMinor)}.`,
      );
      setAmountText("");
      setPaidWith(null);
      reloadAll();
    } catch {
      setPaymentProblem("Can't reach Salio");
    } finally {
      savingNow.current = false;
      setSaving(false);
    }
  }

  const showListLoading = useShowLoader(customers === null && listProblem === "");
  const showStatementLoading = useShowLoader(
    openId !== null && statement === null && statementProblem === "",
  );

  // ---------- one customer ----------
  if (openId !== null) {
    return (
      <div className="flex h-full flex-col bg-background text-foreground">
        <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
          <button
            onClick={() => setOpenId(null)}
            className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold"
          >
            ‹ Owed
          </button>
          <span className="min-w-0 truncate text-base font-bold">{statement?.name ?? ""}</span>
        </div>

        <main className="min-h-0 flex-1 overflow-y-auto">
          {statement === null && statementProblem === "" && showStatementLoading && (
            <SalioLoader size="inline" message="Loading statement…" />
          )}

          {statementProblem !== "" && (
            <div className="p-4">
              <p className="mb-3 text-base font-semibold text-error">{statementProblem}</p>
              <button
                onClick={() => {
                  setStatementProblem("");
                  setStatementAttempt(statementAttempt + 1);
                }}
                className="h-12 w-full rounded-xl bg-primary text-base font-bold text-primary-foreground"
              >
                Try again
              </button>
            </div>
          )}

          {statement !== null && (
            <>
              <section className="border-b-2 border-border bg-surface px-3 py-3">
                <p className="text-base">{statement.phone}</p>
                <div className="mt-1 flex items-baseline justify-between">
                  <span className="text-lg font-semibold">Owes</span>
                  <span className="font-money text-2xl font-bold">KES {kes(statement.balanceMinor)}</span>
                </div>
              </section>

              {/* Oldest first, with the balance after each. */}
              {statement.lines.map((line, index) => (
                <div key={index} className="border-b border-border bg-surface px-3 py-2">
                  <div className="flex items-baseline justify-between gap-3">
                    <span className="min-w-0 text-base font-semibold">
                      {line.kind === "Sale"
                        ? `Credit sale ${line.reference}`
                        : line.kind === "Payment"
                          ? `Paid, ${paymentName(line.reference)}`
                          : "Adjustment"}
                    </span>
                    <span className="shrink-0 font-money text-base font-bold">
                      {line.chargedMinor > 0 ? `+${kes(line.chargedMinor)}` : `−${kes(line.paidMinor)}`}
                    </span>
                  </div>
                  <div className="flex items-baseline justify-between gap-3 text-sm text-muted-foreground">
                    <span>{dayLabel(line.date)}</span>
                    <span>
                      balance <span className="font-money font-semibold">{kes(line.balanceAfterMinor)}</span>
                    </span>
                  </div>
                </div>
              ))}

              {statement.lines.length === 0 && (
                <p className="p-4 text-base md:text-sm text-muted-foreground">Nothing on credit yet.</p>
              )}
            </>
          )}
        </main>

        {/* Record payment: a part payment is normal. */}
        {statement !== null && (
          <section className="shrink-0 space-y-2 border-t-2 border-border bg-surface p-2">
            {paymentSaved !== "" && (
              <p role="status" className="rounded-lg bg-primary-soft px-3 py-2 text-base font-semibold text-primary">
                {paymentSaved}
              </p>
            )}

            {paymentProblem !== "" && (
              <div role="alert" className="rounded-lg bg-error px-3 py-2 text-error-foreground">
                <p className="text-base font-bold">Payment NOT saved</p>
                <p className="text-base">{paymentProblem}</p>
              </div>
            )}

            <div className="flex gap-2">
              <label className="min-w-0 flex-1 text-sm font-semibold">
                Amount (KES)
                <input
                  type="number"
                  inputMode="decimal"
                  min="0"
                  step="any"
                  value={amountText}
                  onChange={(e) => {
                    setAmountText(e.target.value);
                    setPaymentProblem("");
                    setPaymentSaved("");
                  }}
                  className="mt-0.5 h-12 w-full rounded-lg border-2 border-border-strong px-2 text-right font-money text-xl
                    focus:border-primary focus:outline-none"
                />
              </label>
              <label className="w-40 shrink-0 text-sm font-semibold">
                Date
                <input
                  type="date"
                  value={paymentDate}
                  max={dayString(new Date())}
                  onChange={(e) => setPaymentDate(e.target.value)}
                  className="mt-0.5 h-12 w-full rounded-lg border-2 border-border-strong px-2 text-base
                    focus:border-primary focus:outline-none"
                />
              </label>
            </div>

            <div className="grid grid-cols-2 gap-2">
              {PAID_WITH.map((option) => (
                <button
                  key={option.value}
                  type="button"
                  onClick={() => {
                    setPaidWith(option.value);
                    setPaymentProblem("");
                  }}
                  aria-pressed={paidWith === option.value}
                  className={
                    "h-12 rounded-xl text-lg font-bold " +
                    (paidWith === option.value
                      ? "bg-primary text-primary-foreground"
                      : "border-2 border-border-strong bg-surface text-foreground")
                  }
                >
                  {option.label}
                </button>
              ))}
            </div>

            <button
              onClick={recordPayment}
              disabled={saving}
              className="h-14 w-full rounded-xl bg-primary text-xl font-bold text-primary-foreground disabled:opacity-40"
            >
              {saving ? "Saving…" : "Record payment"}
            </button>
          </section>
        )}
      </div>
    );
  }

  // ---------- the list ----------
  // Only customers who owe something, largest first.
  const owing = (customers ?? [])
    .filter((c) => c.balanceMinor > 0)
    .sort((a, b) => b.balanceMinor - a.balanceMinor);

  let totalOwedMinor = 0;
  for (const customer of owing) {
    totalOwedMinor = totalOwedMinor + customer.balanceMinor;
  }

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex min-h-11 shrink-0 items-center gap-2 border-b border-border bg-surface px-3 py-1.5">
        <span className="text-base font-bold">Owed</span>
      </div>

      <section className="shrink-0 border-b-2 border-border bg-surface px-3 py-2">
        <div className="flex items-baseline justify-between">
          <span className="text-base font-semibold">Total owed</span>
          <span className="font-money text-2xl font-bold">
            KES {customers === null ? "—" : kes(totalOwedMinor)}
          </span>
        </div>
      </section>

      <main className="min-h-0 flex-1 overflow-y-auto">
        {customers === null && listProblem === "" && showListLoading && (
          <SalioLoader size="inline" message="Loading customers…" />
        )}

        {listProblem !== "" && (
          <div className="p-4">
            <p className="mb-3 text-base font-semibold text-error">{listProblem}</p>
            <button
              onClick={() => {
                setListProblem("");
                setListAttempt(listAttempt + 1);
              }}
              className="h-12 w-full rounded-xl bg-primary text-base font-bold text-primary-foreground"
            >
              Try again
            </button>
          </div>
        )}

        {customers !== null && owing.length === 0 && (
          <p className="p-4 text-base md:text-sm text-muted-foreground">Nobody owes anything.</p>
        )}

        {owing.map((customer) => (
          <button
            key={customer.id}
            onClick={() => open(customer.id)}
            className="flex min-h-14 w-full items-center justify-between gap-3 border-b
              border-border bg-surface px-3 py-2 text-left active:bg-primary-soft"
          >
            <span className="min-w-0">
              <span className="block truncate text-base font-semibold">{customer.name}</span>
              <span className="text-sm text-muted-foreground">{customer.phone}</span>
            </span>
            <span className="shrink-0 font-money text-lg font-bold">{kes(customer.balanceMinor)}</span>
          </button>
        ))}
      </main>
    </div>
  );
}
