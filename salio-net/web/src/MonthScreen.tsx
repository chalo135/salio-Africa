import { useState, useEffect } from "react";
import { API, kes, type ItemWords } from "./auth";
import TaxScreen, { type FilingPack, type FiledReturn } from "./TaxScreen";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { apiFetch, problemText } from "./api";

// End of month, Owner only: App.tsx shows it to an Owner, and every endpoint
// behind it refuses a Cashier anyway. Read-only apart from recording that the
// tax was filed, which happens on the tax screen.

// From GET /api/reports/profit-and-loss. Only the parts this screen shows.
type CostRow = {
  accountId: string;
  name: string;
  amountMinor: number;
};

type ProfitAndLoss = {
  revenueTotalMinor: number;
  costOfSalesTotalMinor: number;
  grossProfitMinor: number;
  runningCostRows: CostRow[];
  runningCostsTotalMinor: number;
  netProfitMinor: number;
};

// From GET /api/expenses. Drawings are in this list, never in the profit
// and loss, so this is where the month's drawings are added up.
type Expense = {
  isDrawings: boolean;
  amountMinor: number;
};

type Figures = {
  // Which month these belong to, so a slow answer for another month is
  // never shown under this month's name.
  from: string;
  profitAndLoss: ProfitAndLoss;
  drawingsMinor: number;
  pack: FilingPack | null;
  packProblem: string;
};

// month is 1 to 12.
type Month = { year: number; month: number };

function twoDigits(n: number) {
  return String(n).padStart(2, "0");
}

function firstDayOf(m: Month) {
  return `${m.year}-${twoDigits(m.month)}-01`;
}

function lastDayOf(m: Month) {
  // Day 0 of the next month is the last day of this one.
  const days = new Date(m.year, m.month, 0).getDate();
  return `${m.year}-${twoDigits(m.month)}-${twoDigits(days)}`;
}

function monthBefore(m: Month): Month {
  return m.month === 1 ? { year: m.year - 1, month: 12 } : { year: m.year, month: m.month - 1 };
}

function monthAfter(m: Month): Month {
  return m.month === 12 ? { year: m.year + 1, month: 1 } : { year: m.year, month: m.month + 1 };
}

function monthLabelOf(m: Month) {
  return new Date(m.year, m.month - 1, 1).toLocaleDateString("en-KE", {
    month: "long",
    year: "numeric",
  });
}

// Today on this phone's calendar, as the server writes dates.
function todayText() {
  const now = new Date();
  return `${now.getFullYear()}-${twoDigits(now.getMonth() + 1)}-${twoDigits(now.getDate())}`;
}

export default function MonthScreen({
  shopId,
  userName,
  words,
  startAtTax = false,
  onBack,
  onExpired,
}: {
  shopId: string;
  userName: string;
  words: ItemWords;
  // True when opened from "Tax to file": this month, straight at its tax
  // part. The figures are loaded the same way either way.
  startAtTax?: boolean;
  onBack: () => void;
  onExpired: () => void;
}) {
  // Opens on last month: on the 1st, that is the month to close.
  const [month, setMonth] = useState<Month>(() => {
    const now = new Date();
    const thisMonth = { year: now.getFullYear(), month: now.getMonth() + 1 };
    return startAtTax ? thisMonth : monthBefore(thisMonth);
  });

  const [figures, setFigures] = useState<Figures | null>(null);
  const [problem, setProblem] = useState("");
  const [loadCount, setLoadCount] = useState(0);
  const [showingTax, setShowingTax] = useState(startAtTax);

  // Returns recorded while this screen is open, by the month's first day.
  // There is no endpoint to read them back, so this is all Salio can show.
  const [filed, setFiled] = useState<Record<string, FiledReturn>>({});

  const from = firstDayOf(month);
  const to = lastDayOf(month);
  const today = todayText();
  const monthLabel = monthLabelOf(month);
  const monthEnded = to < today;
  const isThisMonth = from <= today && today <= to;

  useEffect(() => {
    let current = true;
    const query = `organizationId=${shopId}&from=${from}&to=${to}`;

    async function load() {
      try {
        const [profitResponse, expensesResponse, packResponse] = await Promise.all([
          apiFetch(`${API}/api/reports/profit-and-loss?${query}`, { credentials: "include" }),
          apiFetch(`${API}/api/expenses?${query}`, { credentials: "include" }),
          apiFetch(`${API}/api/tax/filing-pack?${query}`, { credentials: "include" }),
        ]);

        if (!current) {
          return;
        }
        if ([profitResponse, expensesResponse, packResponse].some((r) => r.status === 401)) {
          onExpired();
          return;
        }
        if (!profitResponse.ok || !expensesResponse.ok) {
          setProblem("Figures didn't load.");
          return;
        }

        const profitAndLoss: ProfitAndLoss = await profitResponse.json();
        const expenses: Expense[] = await expensesResponse.json();

        let drawingsMinor = 0;
        for (const expense of expenses) {
          if (expense.isDrawings) {
            drawingsMinor = drawingsMinor + expense.amountMinor;
          }
        }

        // The tax can fail on its own (no rate for the month) without
        // hiding the rest of the month.
        const pack: FilingPack | null = packResponse.ok ? await packResponse.json() : null;
        const packProblem = packResponse.ok ? "" : await problemText(packResponse);

        if (current) {
          setFigures({ from, profitAndLoss, drawingsMinor, pack, packProblem });
          setProblem("");
        }
      } catch {
        if (current) {
          setProblem("Can't reach Salio.");
        }
      }
    }

    load();

    return () => {
      current = false;
    };
    // onExpired is a new function on every render of App.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, from, to, loadCount]);

  // Figures for another month are still loading, not this month's.
  const shown = figures !== null && figures.from === from ? figures : null;
  const showLoading = useShowLoader(shown === null && problem === "");

  if (showingTax && shown !== null && shown.pack !== null) {
    return (
      <TaxScreen
        shopId={shopId}
        userName={userName}
        monthLabel={monthLabel}
        from={from}
        to={to}
        monthEnded={monthEnded}
        pack={shown.pack}
        netProfitMinor={shown.profitAndLoss.netProfitMinor}
        filed={filed[from] ?? null}
        onFiled={(filedReturn) => setFiled({ ...filed, [from]: filedReturn })}
        onBack={() => setShowingTax(false)}
        onExpired={onExpired}
      />
    );
  }

  const lineClass = "flex items-baseline justify-between gap-3 py-1";

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold md:hidden"
        >
          ‹ More
        </button>
        <span className="text-base font-bold">Month end</span>
      </div>

      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface p-2">
        <button
          onClick={() => setMonth(monthBefore(month))}
          className="min-h-11 shrink-0 rounded-lg border-2 border-border-strong px-3 text-sm font-bold"
        >
          ‹ Earlier
        </button>
        <span className="min-w-0 flex-1 text-center text-lg font-bold">{monthLabel}</span>
        <button
          onClick={() => setMonth(monthAfter(month))}
          disabled={isThisMonth}
          className="min-h-11 shrink-0 rounded-lg border-2 border-border-strong px-3 text-sm font-bold
            disabled:opacity-40"
        >
          Later ›
        </button>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto px-3 py-3">
        {problem !== "" && (
          <div className="mb-3">
            <p className="text-base">{problem}</p>
            <button
              onClick={() => setLoadCount(loadCount + 1)}
              className="mt-2 min-h-11 rounded-lg border-2 border-primary px-3 text-base font-bold text-primary"
            >
              Try again
            </button>
          </div>
        )}

        {shown === null && problem === "" && showLoading && (
          <SalioLoader size="inline" message={`Loading ${monthLabel}…`} />
        )}

        {shown !== null && (
          <>
            {isThisMonth && (
              <p className="mb-3 text-base md:text-sm font-semibold text-muted-foreground">
                Month not over yet
              </p>
            )}

            {/* Salio has no stock count yet, so this is always true. */}
            <p className="mb-3 rounded-lg bg-warning px-3 py-2 text-base text-warning-foreground">
              <strong>Stock never counted.</strong> Profit assumes the shelf matches.
            </p>

            <section className="rounded-lg border border-border bg-surface px-3 py-2">
              <div className={lineClass}>
                <span className="text-base">Sales</span>
                <span className="font-money text-lg">
                  {kes(shown.profitAndLoss.revenueTotalMinor)}
                </span>
              </div>
              <div className={lineClass}>
                <span className="text-base">Cost of {words.plural}</span>
                <span className="font-money text-lg">
                  − {kes(shown.profitAndLoss.costOfSalesTotalMinor)}
                </span>
              </div>
              <div className={lineClass + " border-t border-border"}>
                <span className="text-base font-bold">Left after {words.plural}</span>
                <span className="font-money text-lg font-bold">
                  {kes(shown.profitAndLoss.grossProfitMinor)}
                </span>
              </div>

              <p className="mt-3 text-sm font-bold text-muted-foreground">Running costs</p>
              {shown.profitAndLoss.runningCostRows.length === 0 && (
                <p className="py-1 text-base md:text-sm text-muted-foreground">
                  None
                </p>
              )}
              {shown.profitAndLoss.runningCostRows.map((row) => (
                <div key={row.accountId} className={lineClass}>
                  <span className="min-w-0 text-base">{row.name}</span>
                  <span className="font-money text-lg">− {kes(row.amountMinor)}</span>
                </div>
              ))}
              <div className={lineClass + " border-t border-border"}>
                <span className="text-base font-bold">Total</span>
                <span className="font-money text-lg font-bold">
                  − {kes(shown.profitAndLoss.runningCostsTotalMinor)}
                </span>
              </div>

              <div className="mt-2 flex items-baseline justify-between gap-3 border-t-2 border-foreground pt-2">
                <span className="text-lg font-bold">
                  {shown.profitAndLoss.netProfitMinor < 0 ? "What the shop lost" : "What the shop made"}
                </span>
                <span className="font-money text-2xl font-bold">
                  KES {kes(Math.abs(shown.profitAndLoss.netProfitMinor))}
                </span>
              </div>
            </section>

            {/* Separate on purpose: drawings are not a shop cost. */}
            <section className="mt-4 rounded-lg border border-border bg-surface px-3 py-2">
              <div className={lineClass}>
                <span className="text-base font-bold">Family drawings</span>
                <span className="font-money text-lg font-bold">KES {kes(shown.drawingsMinor)}</span>
              </div>
              <p className="text-sm text-muted-foreground">
                Not a shop cost
              </p>
            </section>

            {shown.pack !== null ? (
              <button
                onClick={() => setShowingTax(true)}
                className="mt-4 flex min-h-16 w-full items-center gap-3 rounded-lg border-2 border-primary
                  bg-surface px-3 py-2 text-left active:bg-primary-soft"
              >
                <span className="min-w-0 flex-1">
                  <span className="block text-base font-bold">Turnover Tax</span>
                  <span className="block text-sm text-muted-foreground">
                    {filed[from] !== undefined
                      ? "Filed. " + monthLabel + " is closed."
                      : "Check and record filing"}
                  </span>
                </span>
                <span className="shrink-0 font-money text-lg font-bold">
                  KES {kes(shown.pack.taxDueMinor)}
                </span>
                <span className="shrink-0 text-2xl text-muted-foreground" aria-hidden="true">
                  ›
                </span>
              </button>
            ) : (
              <div className="mt-4 rounded-lg border border-border bg-surface px-3 py-2">
                <p className="text-base font-bold">Turnover Tax unavailable</p>
                <p className="text-base">{shown.packProblem}</p>
              </div>
            )}
          </>
        )}
      </main>
    </div>
  );
}
