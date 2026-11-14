import { useState, useEffect } from "react";
import { API, kes, paymentName } from "./auth";
import Receipt from "./Receipt";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { apiFetch } from "./api";

type SalePayment = {
  method: string; // "Cash", "Mpesa", "Card", "Credit"
  amountMinor: number;
};

type SaleRow = {
  id: string;
  saleNumber: string;
  saleDate: string; // "2026-09-26"
  occurredAt: string;
  totalMinor: number;
  soldBy: string | null;
  payments: SalePayment[];
};

type SaleLine = {
  productName: string;
  sku: string;
  quantity: number;
  unitPriceMinor: number;
};

type SaleDetail = SaleRow & {
  lines: SaleLine[];
  customerName: string | null; // set only on a credit sale
};

type RangeKind = "today" | "yesterday" | "week" | "month" | "custom";

// Which days are showing. from and to are both "YYYY-MM-DD", both inclusive,
// exactly as the API takes them.
type Range = { kind: RangeKind; from: string; to: string };

const QUICK_CHOICES: { kind: RangeKind; label: string }[] = [
  { kind: "today", label: "Today" },
  { kind: "yesterday", label: "Yesterday" },
  { kind: "week", label: "This week" },
  { kind: "month", label: "This month" },
  { kind: "custom", label: "Pick dates" },
];

// A date as the shop counts it, from the phone's own clock. Not toISOString,
// which is UTC and would call anything before 3am yesterday.
function dayString(date: Date) {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

// The other way: "2026-09-26" as local midnight. new Date("2026-09-26")
// would read it as UTC.
function parseDay(day: string) {
  const [year, month, date] = day.split("-").map(Number);
  return new Date(year, month - 1, date);
}

function rangeFor(kind: Exclude<RangeKind, "custom">): Range {
  const now = new Date();
  const today = dayString(now);

  if (kind === "yesterday") {
    const yesterday = dayString(
      new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1),
    );
    return { kind, from: yesterday, to: yesterday };
  }

  if (kind === "week") {
    // Weeks start on Monday. getDay() is 0 on Sunday, so Sunday is 6 days in.
    const daysIn = (now.getDay() + 6) % 7;
    const monday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - daysIn);
    return { kind, from: dayString(monday), to: today };
  }

  if (kind === "month") {
    return { kind, from: dayString(new Date(now.getFullYear(), now.getMonth(), 1)), to: today };
  }

  return { kind, from: today, to: today };
}

// "Fri 25 Sep"
function dayLabel(day: string) {
  return parseDay(day).toLocaleDateString("en-KE", {
    weekday: "short",
    day: "numeric",
    month: "short",
  });
}

// What the header says is showing, so last month's takings are never read
// as today's. Null for today: that is the default and needs no warning.
function periodLabel(range: Range) {
  if (range.kind === "today") {
    return null;
  }

  const days =
    range.from === range.to
      ? dayLabel(range.from)
      : `${dayLabel(range.from)} – ${dayLabel(range.to)}`;

  const name = QUICK_CHOICES.find((choice) => choice.kind === range.kind)?.label;

  return range.kind === "custom" ? days : `${name} · ${days}`;
}

// "09:33", not the whole timestamp.
function timeOf(occurredAt: string) {
  return new Date(occurredAt).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
}

// "Cash", or "Cash + M-Pesa" when the customer split the payment.
function howPaid(payments: SalePayment[]) {
  return payments.map((p) => paymentName(p.method)).join(" + ");
}

export default function SalesScreen({
  shopId,
  shopName,
  onExpired,
}: {
  shopId: string;
  shopName: string;
  onExpired: () => void;
}) {
  // Today is what opens.
  const [range, setRange] = useState<Range>(() => rangeFor("today"));

  // Bumped by "Try again", so the same range can be asked for twice.
  const [attempt, setAttempt] = useState(0);

  // The last answer, labelled with the request it answers. Anything whose
  // label does not match what is being asked for now is treated as not here
  // yet, so a slow answer for "This month" can never land under "Today".
  const [result, setResult] = useState<{
    key: string;
    sales: SaleRow[];
    problem: string;
  } | null>(null);

  const [openSale, setOpenSale] = useState<SaleDetail | null>(null);
  const [openingId, setOpeningId] = useState<string | null>(null);
  const [detailProblem, setDetailProblem] = useState("");

  const isToday = range.kind === "today";
  const rangeProblem =
    range.from === "" || range.to === ""
      ? "Pick both dates."
      : range.to < range.from
        ? "End is before start"
        : "";

  const requestKey = `${shopId}|${range.from}|${range.to}|${attempt}`;
  const loading = rangeProblem === "" && (result === null || result.key !== requestKey);
  const current = !loading && rangeProblem === "" ? result : null;
  const showLoading = useShowLoader(loading);
  const sales = current?.sales ?? [];
  const problem = current?.problem ?? "";

  // True only when the rows on screen really are the rows for this range.
  const showing = current !== null && problem === "";

  useEffect(() => {
    if (rangeProblem !== "") {
      return;
    }

    const key = `${shopId}|${range.from}|${range.to}|${attempt}`;
    let cancelled = false;

    const failed = (message: string) => {
      if (!cancelled) {
        setResult({ key, sales: [], problem: message });
      }
    };

    apiFetch(
      `${API}/api/sales?organizationId=${shopId}&from=${range.from}&to=${range.to}`,
      { credentials: "include" },
    )
      .then(async (response) => {
        if (cancelled) {
          return;
        }

        if (response.status === 401) {
          onExpired();
          return;
        }

        if (!response.ok) {
          failed("Sales didn't load.");
          return;
        }

        const data: SaleRow[] = await response.json();

        if (!cancelled) {
          setResult({ key, sales: data, problem: "" });
        }
      })
      // An empty list and a dead connection look identical on screen, so
      // this one is worth saying out loud.
      .catch(() => failed("Can't reach Salio"));

    // The range changed before the answer came: drop the answer.
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, range.from, range.to, attempt, rangeProblem]);

  function choose(kind: RangeKind) {
    setDetailProblem("");

    if (kind === "custom") {
      // Start from whatever is showing, so the pickers open somewhere sensible.
      setRange({ kind, from: range.from, to: range.to });
      return;
    }

    setRange(rangeFor(kind));
  }

  async function openDetail(id: string) {
    setOpeningId(id);
    setDetailProblem("");

    try {
      const response = await apiFetch(`${API}/api/sales/${id}?organizationId=${shopId}`, {
        credentials: "include",
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        setDetailProblem("Sale didn't open.");
        return;
      }

      setOpenSale(await response.json());
    } catch {
      setDetailProblem("Can't reach Salio.");
    } finally {
      setOpeningId(null);
    }
  }

  // The takings for the range showing, added up from the rows below. Cash
  // is its own figure: it is what the drawer is counted against, so it must
  // never be buried inside the total.
  let takingsMinor = 0;
  let cashMinor = 0;
  let mpesaMinor = 0;

  // Sold on credit: in the sales total, but no money came in for it. Its
  // own line, so it is never counted as if it were in the drawer.
  let creditMinor = 0;

  for (const sale of sales) {
    takingsMinor = takingsMinor + sale.totalMinor;

    for (const payment of sale.payments) {
      if (payment.method === "Cash") {
        cashMinor = cashMinor + payment.amountMinor;
      }

      if (payment.method === "Mpesa") {
        mpesaMinor = mpesaMinor + payment.amountMinor;
      }

      if (payment.method === "Credit") {
        creditMinor = creditMinor + payment.amountMinor;
      }
    }
  }

  // Never 0.00 while loading or after a failure: that would read as a day
  // with no takings.
  const figure = (minor: number) => (showing ? kes(minor) : "—");

  const period = periodLabel(range);
  const spansDays = range.from !== range.to;

  // ---------- one sale: the same receipt the till shows ----------
  if (openSale !== null) {
    return (
      <Receipt
        shopName={shopName}
        saleNumber={openSale.saleNumber}
        occurredAt={openSale.occurredAt}
        lines={openSale.lines.map((line) => ({
          name: line.productName,
          sku: line.sku,
          quantity: line.quantity,
          unitPriceMinor: line.unitPriceMinor,
        }))}
        payments={openSale.payments}
        totalMinor={openSale.totalMinor}
        soldBy={openSale.soldBy}
        customerName={openSale.customerName}
        onDone={() => setOpenSale(null)}
      />
    );
  }

  // ---------- the list ----------
  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex min-h-11 shrink-0 items-center gap-2 border-b border-border bg-surface px-3 py-1.5">
        <span className="text-base font-bold">{isToday ? "Today" : "Sales"}</span>
      </div>

      {/* Which days. Scrolls sideways on a narrow phone rather than wrapping. */}
      <div className="shrink-0 border-b border-border bg-surface">
        <div className="flex gap-1 overflow-x-auto p-1.5">
          {QUICK_CHOICES.map((choice) => (
            <button
              key={choice.kind}
              onClick={() => choose(choice.kind)}
              className={
                "min-h-11 shrink-0 rounded-lg px-3 text-sm font-bold " +
                (range.kind === choice.kind
                  ? "bg-primary text-primary-foreground"
                  : "border-2 border-border-strong bg-surface text-foreground")
              }
            >
              {choice.label}
            </button>
          ))}
        </div>

        {range.kind === "custom" && (
          <div className="flex gap-2 px-2 pb-2">
            <label className="min-w-0 flex-1 text-sm font-semibold">
              From
              <input
                type="date"
                value={range.from}
                onChange={(e) => setRange({ ...range, from: e.target.value })}
                className="mt-0.5 h-11 w-full rounded-lg border-2 border-border-strong bg-surface px-2 text-base
                  focus:border-primary focus:outline-none"
              />
            </label>
            <label className="min-w-0 flex-1 text-sm font-semibold">
              To
              <input
                type="date"
                value={range.to}
                onChange={(e) => setRange({ ...range, to: e.target.value })}
                className="mt-0.5 h-11 w-full rounded-lg border-2 border-border-strong bg-surface px-2 text-base
                  focus:border-primary focus:outline-none"
              />
            </label>
          </div>
        )}
      </div>

      {/* What the drawer is counted against, for exactly the rows below. */}
      <section className="shrink-0 border-b-2 border-border bg-surface px-3 py-2">
        {period !== null && (
          <p className="mb-1 inline-block rounded bg-primary-soft px-1.5 py-0.5 text-sm font-bold text-primary">
            {period}
          </p>
        )}
        <div className="flex items-baseline justify-between">
          <span className="text-base font-semibold">Takings</span>
          <span className="font-money text-2xl font-bold">KES {figure(takingsMinor)}</span>
        </div>
        <div className="mt-1 flex justify-between text-base">
          <span className="text-muted-foreground">Cash</span>
          <span className="font-money font-semibold">{figure(cashMinor)}</span>
        </div>
        <div className="flex justify-between text-base">
          <span className="text-muted-foreground">M-Pesa</span>
          <span className="font-money font-semibold">{figure(mpesaMinor)}</span>
        </div>
        {showing && creditMinor > 0 && (
          <div className="flex justify-between text-base">
            <span className="text-muted-foreground">Unpaid credit</span>
            <span className="font-money font-semibold">{figure(creditMinor)}</span>
          </div>
        )}
      </section>

      <main className="min-h-0 flex-1 overflow-y-auto">
        {rangeProblem !== "" && (
          <p className="p-4 text-base font-semibold text-error">{rangeProblem}</p>
        )}

        {showLoading && <SalioLoader size="inline" message="Loading sales…" />}

        {problem !== "" && (
          <div className="p-4">
            <p className="mb-3 text-base font-semibold text-error">{problem}</p>
            <button
              onClick={() => setAttempt(attempt + 1)}
              className="h-12 w-full rounded-xl bg-primary text-base font-bold text-primary-foreground"
            >
              Try again
            </button>
          </div>
        )}

        {showing && sales.length === 0 && (
          <p className="p-4 text-base md:text-sm text-muted-foreground">
            {isToday ? "No sales yet today." : "No sales in this period."}
          </p>
        )}

        {detailProblem !== "" && (
          <p className="px-3 py-2 text-base font-semibold text-error">{detailProblem}</p>
        )}

        {showing &&
          sales.map((sale, index) => (
            <div key={sale.id}>
              {/* Over more than one day, a time alone is ambiguous: head each
                  day's rows with its date. */}
              {spansDays && (index === 0 || sales[index - 1].saleDate !== sale.saleDate) && (
                <p className="border-b border-border bg-background px-3 py-1 text-sm font-bold text-muted-foreground">
                  {dayLabel(sale.saleDate)}
                </p>
              )}
              <button
                onClick={() => openDetail(sale.id)}
                disabled={openingId !== null}
                className="flex min-h-14 w-full items-center justify-between gap-3 border-b
                  border-border bg-surface px-3 py-2 text-left active:bg-primary-soft"
              >
                <span className="min-w-0">
                  <span className="font-money block text-base font-semibold">
                    {timeOf(sale.occurredAt)}
                  </span>
                  <span className="mt-0.5 block truncate text-sm text-muted-foreground">
                    {howPaid(sale.payments)}
                    {sale.soldBy !== null && ` · ${sale.soldBy}`}
                  </span>
                </span>
                <span className="font-money shrink-0 text-lg font-bold">
                  {openingId === sale.id ? "…" : kes(sale.totalMinor)}
                </span>
              </button>
            </div>
          ))}
      </main>
    </div>
  );
}
