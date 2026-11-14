import { useState, useEffect, useRef } from "react";
import { API, kes, paymentName } from "./auth";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { apiFetch, problemText } from "./api";

// From GET /api/accounts. accountClass is the server's number:
// 2 = Equity, 4 = Expense.
type Account = {
  id: string;
  code: string;
  name: string;
  accountClass: number;
};

type Expense = {
  id: string;
  expenseDate: string;
  categoryName: string;
  isDrawings: boolean;
  amountMinor: number;
  paidFrom: string; // "Cash" or "Mpesa"
  note: string | null;
};

const DRAWINGS_CODE = "3100";

// The server's PaymentMethod numbers.
const PAID_FROM = [
  { value: 0, label: "Cash" },
  { value: 1, label: "M-Pesa" },
];

function dayString(date: Date) {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

// "Mon 19 Oct"
function dayLabel(day: string) {
  const [year, month, date] = day.split("-").map(Number);
  return new Date(year, month - 1, date).toLocaleDateString("en-KE", {
    weekday: "short",
    day: "numeric",
    month: "short",
  });
}

const NO_CONNECTION = "Can't reach Salio";

export default function ExpensesScreen({
  shopId,
  onBack,
  onExpired,
}: {
  shopId: string;
  onBack: () => void;
  onExpired: () => void;
}) {
  // The period listed below the form: this month so far.
  const [period] = useState(() => {
    const now = new Date();
    return {
      from: dayString(new Date(now.getFullYear(), now.getMonth(), 1)),
      to: dayString(now),
      name: now.toLocaleDateString("en-KE", { month: "long", year: "numeric" }),
    };
  });

  // ---- The categories ----
  const [accounts, setAccounts] = useState<Account[] | null>(null);
  const [accountsProblem, setAccountsProblem] = useState("");
  const [accountsAttempt, setAccountsAttempt] = useState(0);

  // ---- This month's entries ----
  const [entries, setEntries] = useState<Expense[] | null>(null);
  const [entriesProblem, setEntriesProblem] = useState("");
  const [entriesAttempt, setEntriesAttempt] = useState(0);

  // ---- The form ----
  const [accountId, setAccountId] = useState<string | null>(null);
  const [amountText, setAmountText] = useState(""); // shillings, as typed
  const [paidFrom, setPaidFrom] = useState<number | null>(null);
  const [expenseDate, setExpenseDate] = useState(() => dayString(new Date()));
  const [note, setNote] = useState("");

  const [saving, setSaving] = useState(false);
  const savingNow = useRef(false); // a lock that holds before React re-renders
  const [problem, setProblem] = useState("");
  const [saved, setSaved] = useState("");

  // ---- Adding a category ----
  const [adding, setAdding] = useState(false);
  const [newName, setNewName] = useState("");
  const [addProblem, setAddProblem] = useState("");
  const [addBusy, setAddBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;

    apiFetch(`${API}/api/accounts?organizationId=${shopId}`, { credentials: "include" })
      .then(async (response) => {
        if (cancelled) return;
        if (response.status === 401) {
          onExpired();
          return;
        }
        if (!response.ok) {
          setAccountsProblem(await problemText(response));
          return;
        }
        setAccounts(await response.json());
      })
      .catch(() => {
        if (!cancelled) setAccountsProblem("Can't reach Salio.");
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, accountsAttempt]);

  useEffect(() => {
    let cancelled = false;

    apiFetch(`${API}/api/expenses?organizationId=${shopId}&from=${period.from}&to=${period.to}`, {
      credentials: "include",
    })
      .then(async (response) => {
        if (cancelled) return;
        if (response.status === 401) {
          onExpired();
          return;
        }
        if (!response.ok) {
          setEntriesProblem(await problemText(response));
          return;
        }
        setEntries(await response.json());
      })
      .catch(() => {
        if (!cancelled) setEntriesProblem("Can't reach Salio.");
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, period, entriesAttempt]);

  function reloadEntries() {
    setEntries(null);
    setEntriesProblem("");
    setEntriesAttempt(entriesAttempt + 1);
  }

  // Shop costs: every expense account except 5000, which sales write.
  // Drawings: 3100, shown on its own because it is not a shop cost.
  const costs = (accounts ?? []).filter((a) => a.accountClass === 4 && a.code !== "5000");
  const drawings = (accounts ?? []).find((a) => a.code === DRAWINGS_CODE);
  const chosen = (accounts ?? []).find((a) => a.id === accountId);
  const amountMinor = Math.round((Number(amountText) || 0) * 100);

  // Two totals, never added together: the family's money is not a cost.
  let costsTotalMinor = 0;
  let drawingsTotalMinor = 0;
  for (const entry of entries ?? []) {
    if (entry.isDrawings) {
      drawingsTotalMinor = drawingsTotalMinor + entry.amountMinor;
    } else {
      costsTotalMinor = costsTotalMinor + entry.amountMinor;
    }
  }

  function choose(id: string) {
    setAccountId(id);
    setProblem("");
    setSaved("");
  }

  async function save() {
    if (savingNow.current) {
      return;
    }

    if (chosen === undefined) {
      setProblem("Choose a category.");
      return;
    }
    if (!(amountMinor > 0)) {
      setProblem("Amount must be more than 0.");
      return;
    }
    if (paidFrom === null) {
      setProblem("Choose Cash or M-Pesa.");
      return;
    }

    savingNow.current = true;
    setSaving(true);
    setProblem("");
    setSaved("");

    try {
      const response = await apiFetch(`${API}/api/expenses`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          organizationId: shopId,
          expenseDate,
          accountId: chosen.id,
          amountMinor,
          paidFrom,
          note: note.trim() === "" ? null : note.trim(),
        }),
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        // Everything typed stays, so nothing has to be entered again.
        setProblem(await problemText(response));
        return;
      }

      const how = PAID_FROM.find((p) => p.value === paidFrom)?.label;
      setSaved(`Saved: ${chosen.name}, KES ${kes(amountMinor)} from ${how}.`);

      setAccountId(null);
      setAmountText("");
      setPaidFrom(null);
      setNote("");
      reloadEntries();
    } catch {
      setProblem(NO_CONNECTION);
    } finally {
      savingNow.current = false;
      setSaving(false);
    }
  }

  async function addCategory() {
    if (newName.trim() === "") {
      setAddProblem("Type a category name.");
      return;
    }

    setAddBusy(true);
    setAddProblem("");

    try {
      const response = await apiFetch(`${API}/api/accounts`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ organizationId: shopId, name: newName.trim() }),
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        // The typed name stays, so it can be corrected.
        setAddProblem(await problemText(response));
        return;
      }

      const created: Account = await response.json();
      setAccounts([...(accounts ?? []), created].sort((a, b) => a.code.localeCompare(b.code)));
      setAccountId(created.id);
      setAdding(false);
      setNewName("");
    } catch {
      setAddProblem("Can't reach Salio. Category not added.");
    } finally {
      setAddBusy(false);
    }
  }

  const showAccountsLoading = useShowLoader(accounts === null && accountsProblem === "");
  const showEntriesLoading = useShowLoader(entries === null && entriesProblem === "");

  function categoryButton(account: Account) {
    const selected = account.id === accountId;
    return (
      <button
        key={account.id}
        type="button"
        onClick={() => choose(account.id)}
        aria-pressed={selected}
        className={
          "min-h-16 rounded-xl px-3 py-2 text-lg font-bold leading-tight " +
          (selected
            ? "bg-primary text-primary-foreground"
            : "border-2 border-border-strong bg-surface text-foreground")
        }
      >
        {account.name}
      </button>
    );
  }

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-12 rounded-lg border-2 border-border-strong px-4 text-base font-bold md:hidden"
        >
          ‹ More
        </button>
        <span className="text-xl font-bold">Money out</span>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto">
        <section className="space-y-5 bg-surface p-3">
          {/* 1. What it was for. */}
          <div>
            <h2 className="mb-2 text-xl font-bold">What was it for?</h2>

            {accounts === null && accountsProblem === "" && showAccountsLoading && (
              <SalioLoader size="inline" message="Loading categories…" />
            )}

            {accountsProblem !== "" && (
              <div>
                <p className="mb-2 text-lg font-semibold text-error">{accountsProblem}</p>
                <button
                  onClick={() => {
                    setAccountsProblem("");
                    setAccountsAttempt(accountsAttempt + 1);
                  }}
                  className="h-14 w-full rounded-xl bg-primary text-xl font-bold text-primary-foreground"
                >
                  Try again
                </button>
              </div>
            )}

            {accounts !== null && (
              <>
                <p className="mb-1 text-base font-semibold">Shop costs</p>
                <div className="grid grid-cols-2 gap-2">
                  {costs.map(categoryButton)}

                  {!adding && (
                    <button
                      type="button"
                      onClick={() => {
                        setAdding(true);
                        setAddProblem("");
                      }}
                      className="min-h-16 rounded-xl border-2 border-dashed border-primary px-3 text-lg font-bold text-primary"
                    >
                      + New category
                    </button>
                  )}
                </div>

                {/* A new category, without leaving the screen. */}
                {adding && (
                  <div className="mt-2 rounded-xl border-2 border-primary p-3">
                    <label className="block text-base md:text-sm font-semibold">
                      Category name
                      <input
                        value={newName}
                        onChange={(e) => {
                          setNewName(e.target.value);
                          setAddProblem("");
                        }}
                        maxLength={200}
                        placeholder="e.g. Security guard"
                        className="mt-1 h-14 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-xl
                          placeholder:text-muted-foreground focus:border-primary focus:outline-none"
                      />
                    </label>
                    {addProblem !== "" && (
                      <p className="mt-2 text-lg font-semibold text-error">{addProblem}</p>
                    )}
                    <div className="mt-2 grid grid-cols-2 gap-2">
                      <button
                        type="button"
                        onClick={() => {
                          setAdding(false);
                          setNewName("");
                          setAddProblem("");
                        }}
                        className="h-14 rounded-xl border-2 border-border-strong text-lg font-bold"
                      >
                        Cancel
                      </button>
                      <button
                        type="button"
                        onClick={addCategory}
                        disabled={addBusy}
                        className="h-14 rounded-xl bg-primary text-lg font-bold text-primary-foreground disabled:opacity-40"
                      >
                        {addBusy ? "Adding…" : "Add"}
                      </button>
                    </div>
                  </div>
                )}

                {/* Drawings looks different on purpose: it is the family's
                    money leaving the shop, not something the shop paid for. */}
                {drawings !== undefined && (
                  <>
                    <p className="mb-1 mt-4 text-base font-semibold">Not a shop cost</p>
                    <button
                      type="button"
                      onClick={() => choose(drawings.id)}
                      aria-pressed={accountId === drawings.id}
                      className={
                        "min-h-16 w-full rounded-xl px-3 py-2 text-left " +
                        (accountId === drawings.id
                          ? "bg-warning text-warning-foreground"
                          : "border-2 border-dashed border-warning-strong bg-background text-foreground")
                      }
                    >
                      <span className="block text-lg font-bold">Drawings</span>
                      <span className="block text-base">Money taken out</span>
                    </button>
                  </>
                )}
              </>
            )}
          </div>

          {/* 2. How much. */}
          <label className="block text-xl md:text-base font-bold">
            Amount (KES)
            <input
              type="number"
              inputMode="decimal"
              min="0"
              step="any"
              value={amountText}
              onChange={(e) => {
                setAmountText(e.target.value);
                setProblem("");
                setSaved("");
              }}
              className="mt-1 h-16 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-right font-money text-2xl
                focus:border-primary focus:outline-none"
            />
          </label>

          {/* 3. Where the money came from. */}
          <div>
            <h2 className="mb-2 text-xl font-bold">Paid from</h2>
            <div className="grid grid-cols-2 gap-2">
              {PAID_FROM.map((option) => (
                <button
                  key={option.value}
                  type="button"
                  onClick={() => {
                    setPaidFrom(option.value);
                    setProblem("");
                  }}
                  aria-pressed={paidFrom === option.value}
                  className={
                    "h-16 rounded-xl text-xl font-bold " +
                    (paidFrom === option.value
                      ? "bg-primary text-primary-foreground"
                      : "border-2 border-border-strong bg-surface text-foreground")
                  }
                >
                  {option.label}
                </button>
              ))}
            </div>
          </div>

          <div className="grid grid-cols-1 gap-3">
            <label className="block text-base md:text-sm font-semibold">
              Date
              <input
                type="date"
                value={expenseDate}
                max={dayString(new Date())}
                onChange={(e) => setExpenseDate(e.target.value)}
                className="mt-1 h-14 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
                  focus:border-primary focus:outline-none"
              />
            </label>
            <label className="block text-base md:text-sm font-semibold">
              Note (optional)
              <input
                value={note}
                onChange={(e) => setNote(e.target.value)}
                maxLength={300}
                className="mt-1 h-14 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
                  focus:border-primary focus:outline-none"
              />
            </label>
          </div>

          {saved !== "" && (
            <p role="status" className="rounded-lg bg-primary-soft px-3 py-3 text-lg font-semibold text-primary">
              {saved}
            </p>
          )}

          {problem !== "" && (
            <div role="alert" className="rounded-lg bg-error px-3 py-3 text-error-foreground">
              <p className="text-lg font-bold">Not saved</p>
              <p className="text-lg">{problem}</p>
            </div>
          )}

          <button
            onClick={save}
            disabled={saving || accounts === null}
            className="h-16 w-full rounded-xl bg-primary text-xl font-bold text-primary-foreground disabled:opacity-40"
          >
            {saving ? "Saving…" : "Save"}
          </button>
        </section>

        {/* This month so far. */}
        <section className="mt-3 border-t-2 border-border bg-surface">
          <h2 className="px-3 pt-3 text-xl font-bold">{period.name}</h2>

          <div className="grid grid-cols-2 gap-2 p-3">
            <div className="rounded-xl border-2 border-border-strong p-3">
              <p className="text-base font-semibold">Shop costs</p>
              <p className="font-money text-xl font-bold">
                {entries === null ? "—" : kes(costsTotalMinor)}
              </p>
            </div>
            <div className="rounded-xl border-2 border-dashed border-warning-strong p-3">
              <p className="text-base font-semibold">Drawings</p>
              <p className="font-money text-xl font-bold">
                {entries === null ? "—" : kes(drawingsTotalMinor)}
              </p>
            </div>
          </div>

          {entries === null && entriesProblem === "" && showEntriesLoading && (
            <SalioLoader size="inline" message="Loading money out…" />
          )}

          {entriesProblem !== "" && (
            <div className="px-3 pb-3">
              <p className="mb-2 text-lg font-semibold text-error">{entriesProblem}</p>
              <button
                onClick={reloadEntries}
                className="h-14 w-full rounded-xl bg-primary text-xl font-bold text-primary-foreground"
              >
                Try again
              </button>
            </div>
          )}

          {entries !== null && entries.length === 0 && (
            <p className="px-3 pb-3 text-lg">Nothing paid out this month.</p>
          )}

          {(entries ?? []).map((entry) => (
            <div key={entry.id} className="border-t border-border px-3 py-3">
              <div className="flex items-baseline justify-between gap-3">
                <span className="min-w-0 text-lg font-bold">
                  {entry.categoryName}
                  {entry.isDrawings && (
                    <span className="ml-2 rounded bg-warning px-1.5 py-0.5 text-sm font-bold text-warning-foreground">
                      family
                    </span>
                  )}
                </span>
                <span className="shrink-0 font-money text-lg font-bold">{kes(entry.amountMinor)}</span>
              </div>
              <p className="text-base">
                {dayLabel(entry.expenseDate)} · {paymentName(entry.paidFrom)}
                {entry.note !== null && ` · ${entry.note}`}
              </p>
            </div>
          ))}
        </section>
      </main>
    </div>
  );
}
