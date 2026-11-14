import { useRef, useState } from "react";
import { API, kes, type ItemWords } from "./auth";
import { refusalText } from "./importApi";
import PaidWithChoice from "./PaidWithChoice";
import { PAID_WITH, paidWithLabel } from "./paidWith";
import { apiFetch } from "./api";

export type Suggestion = { productId: string; name: string; sku: string; score: number };

export type InvoiceLine = {
  id: string;
  lineNumber: number;
  rawText: string;
  proposedName: string;
  proposedQuantity: number;
  proposedUnitCostMinor: number;
  matchedProductId?: string | null;
  suggestions: Suggestion[];
  accepted: boolean;
  // Not in the agreed view; read if the server sends them back.
  createNew?: boolean;
  newSellPriceMinor?: number | null;
};

export type InvoiceView = {
  id: string;
  status: string | number;
  isDemoExtraction: boolean;
  supplierName?: string | null;
  invoiceNumber?: string | null;
  invoiceDate?: string | null;
  extractedTotalMinor?: number | null;
  linesTotalMinor: number;
  totalsAgree: boolean;
  differenceMinor: number;
  lines: InvoiceLine[];
};

type Product = { id: string; sku: string; name: string };

// A line as it is being checked. Numbers kept as typed, as Stock in does.
type Edit = {
  id: string;
  lineNumber: number;
  rawText: string;
  suggestions: Suggestion[];
  name: string;
  quantityText: string;
  costText: string; // shillings
  matchedProductId: string | null;
  createNew: boolean;
  sellPriceText: string; // shillings, only for a new part
  accepted: boolean;
};

function todayHere() {
  return new Date().toLocaleDateString("en-CA");
}

function minorOf(text: string) {
  return Math.round((Number(text) || 0) * 100);
}

function quantityOf(edit: Edit) {
  return Number(edit.quantityText) || 0;
}

// Rounded as the server rounds a receipt line: half away from zero.
function lineTotalMinor(edit: Edit) {
  return Math.round(quantityOf(edit) * minorOf(edit.costText));
}

// "48,500", or "48,500.50" when there are cents: the PM's plain form.
function shillings(minor: number) {
  return (minor / 100).toLocaleString("en-KE", {
    minimumFractionDigits: minor % 100 === 0 ? 0 : 2,
    maximumFractionDigits: 2,
  });
}

// A score of 0.92 is 92%. A server that already sends 92 is shown as is.
function percent(score: number) {
  return `${Math.round(score <= 1 ? score * 100 : score)}%`;
}

function editsFrom(view: InvoiceView): Edit[] {
  return view.lines.map((line) => ({
    id: line.id,
    lineNumber: line.lineNumber,
    rawText: line.rawText,
    suggestions: line.suggestions,
    name: line.proposedName,
    quantityText: String(line.proposedQuantity),
    costText: String(line.proposedUnitCostMinor / 100),
    matchedProductId: line.createNew ? null : (line.matchedProductId ?? null),
    createNew: line.createNew ?? false,
    sellPriceText:
      line.newSellPriceMinor !== undefined && line.newSellPriceMinor !== null
        ? String(line.newSellPriceMinor / 100)
        : "",
    accepted: line.accepted,
  }));
}

// Checking what the reader found, line by line, before anything is
// written. Nothing changes the stock or the books until Commit.
export default function InvoiceReview({
  shopId,
  words,
  first,
  products,
  onCommitted,
  onDiscarded,
  onExpired,
}: {
  shopId: string;
  words: ItemWords;
  first: InvoiceView;
  products: Product[];
  onCommitted: (linesReceived: number, totalMinor: number, paidWith: string) => void;
  onDiscarded: () => void;
  onExpired: () => void;
}) {
  const [view, setView] = useState(first);
  const [supplierName, setSupplierName] = useState(first.supplierName ?? "");
  const [invoiceNumber, setInvoiceNumber] = useState(first.invoiceNumber ?? "");
  const [invoiceDate, setInvoiceDate] = useState(first.invoiceDate?.slice(0, 10) ?? "");
  const [edits, setEdits] = useState(() => editsFrom(first));

  const [searchingLine, setSearchingLine] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const [busy, setBusy] = useState("");
  const busyNow = useRef(false);
  const [problem, setProblem] = useState("");
  const [saved, setSaved] = useState("");

  const [committing, setCommitting] = useState(false);
  const [paidWith, setPaidWith] = useState<number | null>(null);
  const [receiptDate, setReceiptDate] = useState(() => {
    const date = first.invoiceDate?.slice(0, 10);
    return date && date <= todayHere() ? date : todayHere();
  });
  const [discarding, setDiscarding] = useState(false);

  // Set when the receipt date falls in a month whose return is filed: the
  // server's own words, and today's date to post it on instead.
  const [closedToday, setClosedToday] = useState<string | null>(null);

  // The lines total, live, as the lines are edited. Only accepted lines count.
  let linesTotal = 0;
  let acceptedCount = 0;
  for (const edit of edits) {
    if (edit.accepted) {
      linesTotal += lineTotalMinor(edit);
      acceptedCount++;
    }
  }

  const extracted = view.extractedTotalMinor ?? null;
  // Untouched lines: the server's own verdict. Edited: compare here.
  const agree =
    extracted === null ? false : linesTotal === view.linesTotalMinor ? view.totalsAgree : linesTotal === extracted;

  function update(index: number, changes: Partial<Edit>) {
    setProblem("");
    setSaved("");
    setEdits((before) => before.map((edit, i) => (i === index ? { ...edit, ...changes } : edit)));
  }

  // Said before committing, with the line named. The server checks again.
  function notReady() {
    if (acceptedCount === 0) {
      return "Accept at least one line";
    }
    for (const edit of edits) {
      if (!edit.accepted) continue;
      const label = `Line ${edit.lineNumber} (${edit.name.trim() || "no name"})`;
      if (edit.name.trim() === "") return `Line ${edit.lineNumber}: name needed`;
      if (!(quantityOf(edit) > 0)) return `${label}: quantity must be above zero`;
      if (!(minorOf(edit.costText) > 0)) return `${label}: cost must be above zero`;
      if (!edit.createNew && edit.matchedProductId === null) {
        return `${label}: which ${words.singular} is it?`;
      }
      if (edit.createNew && !(minorOf(edit.sellPriceText) > 0)) {
        return `${label}: price needed`;
      }
    }
    return "";
  }

  // Every line as typed, in the shape PUT and Commit both take.
  function linesBody() {
    return edits.map((edit) => ({
      id: edit.id,
      proposedName: edit.name.trim(),
      proposedQuantity: quantityOf(edit),
      proposedUnitCostMinor: minorOf(edit.costText),
      matchedProductId: edit.createNew ? null : edit.matchedProductId,
      createNew: edit.createNew,
      newSellPriceMinor: edit.createNew ? minorOf(edit.sellPriceText) : null,
      accepted: edit.accepted,
    }));
  }

  async function put(): Promise<boolean> {
    const body = {
      supplierName: supplierName.trim() === "" ? null : supplierName.trim(),
      invoiceNumber: invoiceNumber.trim() === "" ? null : invoiceNumber.trim(),
      invoiceDate: invoiceDate === "" ? null : invoiceDate,
      lines: linesBody(),
    };

    const response = await apiFetch(`${API}/api/supplier-invoices/${view.id}?organizationId=${shopId}`, {
      method: "PUT",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (response.status === 401) {
      onExpired();
      return false;
    }
    if (!response.ok) {
      setProblem(await refusalText(response, "Not saved"));
      return false;
    }

    // The server's totals for what was saved. The lines on screen stay as
    // typed: they are exactly what was sent.
    setView(await response.json());
    return true;
  }

  async function run(what: string, work: () => Promise<void>) {
    if (busyNow.current) return;
    busyNow.current = true;
    setBusy(what);
    setProblem("");
    setSaved("");
    try {
      await work();
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      busyNow.current = false;
      setBusy("");
    }
  }

  function save() {
    run("save", async () => {
      if (await put()) setSaved("Saved. Not in stock yet.");
    });
  }

  function openCommit() {
    const wrong = notReady();
    if (wrong !== "") {
      setProblem(wrong);
      return;
    }
    setProblem("");
    setCommitting(true);
  }

  // Post the receipt. "on" is the date to post it on: the one chosen, or
  // today when the chosen month is closed and the owner asks for today.
  function commit(on: string = receiptDate) {
    if (paidWith === null) {
      setProblem("Choose how you paid");
      return;
    }
    // The invoice endpoint takes the method's name, not its number.
    const paidWithName = PAID_WITH.find((p) => p.value === paidWith)?.name;
    run("commit", async () => {
      setClosedToday(null);
      if (!(await put())) return;
      const response = await apiFetch(`${API}/api/supplier-invoices/${view.id}/commit?organizationId=${shopId}`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        // The lines go with Commit: "create as a new part" and its price
        // are only kept by Commit, not by Save.
        body: JSON.stringify({ paidWith: paidWithName, receiptDate: on, lines: linesBody() }),
      });
      if (response.status === 401) {
        onExpired();
        return;
      }
      if (response.status === 409) {
        // A month with a filed return is closed. The server names it and
        // says what today is; offer that instead. No codes shown.
        try {
          const refused = await response.json();
          if (refused.closedPeriod === true && typeof refused.today === "string") {
            setProblem(
              typeof refused.detail === "string" && refused.detail !== ""
                ? refused.detail
                : "That month's return is filed",
            );
            setClosedToday(refused.today);
            return;
          }
          setProblem(typeof refused.detail === "string" && refused.detail !== "" ? refused.detail : "Nothing was received.");
        } catch {
          setProblem("Nothing was received.");
        }
        return;
      }
      if (!response.ok) {
        setProblem(await refusalText(response, "Nothing was received."));
        return;
      }
      const done: { linesReceived: number } = await response.json();
      onCommitted(done.linesReceived, linesTotal, paidWithLabel(paidWith));
    });
  }

  function discard() {
    run("discard", async () => {
      const response = await apiFetch(`${API}/api/supplier-invoices/${view.id}/discard?organizationId=${shopId}`, {
        method: "POST",
        credentials: "include",
      });
      if (response.status === 401) {
        onExpired();
        return;
      }
      if (!response.ok) {
        setProblem(await refusalText(response, "Not discarded"));
        return;
      }
      onDiscarded();
    });
  }

  const q = search.trim().toLowerCase();
  const found =
    q === ""
      ? []
      : products
          .filter((p) => p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q))
          .slice(0, 8);

  const problemBox = problem !== "" && (
    <div role="alert" className="mx-2 mt-2 rounded-lg bg-error px-3 py-2 text-error-foreground">
      <p className="text-base">{problem}</p>
    </div>
  );

  const inputClass =
    "mt-0.5 h-11 w-full rounded-lg border-2 border-border-strong bg-surface px-2 text-base focus:border-primary focus:outline-none";

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <main className="min-h-0 flex-1 overflow-y-auto">
        {view.isDemoExtraction && (
          <p
            role="alert"
            className="border-y-4 border-warning-strong bg-warning px-3 py-3 text-center text-xl font-bold uppercase tracking-wide text-warning-foreground"
          >
            Demo only. Not a real reading.
          </p>
        )}

        {/* The invoice itself. */}
        <section className="grid gap-2 border-b border-border bg-surface p-3 md:grid-cols-3">
          <label className="block text-sm font-semibold">
            Supplier
            <input value={supplierName} onChange={(e) => setSupplierName(e.target.value)} maxLength={200} className={inputClass} />
          </label>
          <label className="block text-sm font-semibold">
            Invoice number
            <input value={invoiceNumber} onChange={(e) => setInvoiceNumber(e.target.value)} maxLength={100} className={inputClass} />
          </label>
          <label className="block text-sm font-semibold">
            Invoice date
            <input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} className={inputClass} />
          </label>
        </section>

        <p className="px-3 pt-2 text-base font-bold">Check the lines</p>

        {edits.map((edit, index) => {
          const chosenElsewhere =
            edit.matchedProductId !== null && !edit.suggestions.some((s) => s.productId === edit.matchedProductId)
              ? products.find((p) => p.id === edit.matchedProductId)
              : undefined;
          const unmatched = edit.accepted && !edit.createNew && edit.matchedProductId === null;

          return (
            <div
              key={edit.id}
              className={"m-2 rounded-lg border bg-surface p-2 " + (edit.accepted ? "border-border-strong" : "border-border opacity-60")}
            >
              <div className="flex items-start gap-2">
                <label className="flex min-h-11 shrink-0 items-center gap-2 rounded-lg border-2 border-border-strong px-2 text-base font-bold">
                  <input
                    type="checkbox"
                    checked={edit.accepted}
                    onChange={(e) => update(index, { accepted: e.target.checked })}
                    className="h-5 w-5 accent-primary"
                  />
                  Accept
                </label>
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-bold">Line {edit.lineNumber}</p>
                  <p className="break-words font-mono text-sm text-muted-foreground">{edit.rawText}</p>
                </div>
              </div>

              {/* Name, quantity and cost: one row on a tablet, two on a phone. */}
              <div className="md:flex md:items-end md:gap-2">
                <label className="mt-2 block text-sm font-semibold md:min-w-0 md:flex-1">
                  Name
                  <input value={edit.name} onChange={(e) => update(index, { name: e.target.value })} className={inputClass} />
                </label>

                <div className="mt-2 flex items-end gap-2 md:w-96 md:shrink-0">
                  <label className="w-24 shrink-0 text-sm font-semibold">
                    Quantity
                    <input
                      type="number"
                      inputMode="decimal"
                      min="0"
                      step="any"
                      value={edit.quantityText}
                      onChange={(e) => update(index, { quantityText: e.target.value })}
                      className={inputClass + " text-center font-money"}
                    />
                  </label>
                  <label className="min-w-0 flex-1 text-sm font-semibold">
                    Cost (KES)
                    <input
                      type="number"
                      inputMode="decimal"
                      min="0"
                      step="any"
                      value={edit.costText}
                      onChange={(e) => update(index, { costText: e.target.value })}
                      className={inputClass + " text-right font-money"}
                    />
                  </label>
                  <span className="w-24 shrink-0 pb-2.5 text-right font-money text-base font-semibold">
                    {kes(lineTotalMinor(edit))}
                  </span>
                </div>
              </div>

              {/* Which part it is. Suggested, never decided. */}
              <div className="mt-2">
                <p className="text-sm font-semibold">Which {words.singular}?</p>
                <div className="mt-1 flex flex-wrap gap-1.5">
                  {edit.suggestions.map((s) => {
                    const on = !edit.createNew && edit.matchedProductId === s.productId;
                    return (
                      <button
                        key={s.productId}
                        onClick={() => update(index, { matchedProductId: s.productId, createNew: false })}
                        aria-pressed={on}
                        className={
                          "min-h-11 max-w-full rounded-lg px-2 text-left text-sm " +
                          (on ? "bg-primary text-primary-foreground" : "border-2 border-border-strong bg-surface")
                        }
                      >
                        <span className="font-bold">{s.name}</span> · {s.sku} ·{" "}
                        <span className="font-money font-bold">{percent(s.score)}</span>
                      </button>
                    );
                  })}
                  {chosenElsewhere && !edit.createNew && (
                    <span className="flex min-h-11 items-center rounded-lg bg-primary px-2 text-sm text-primary-foreground">
                      <span className="font-bold">{chosenElsewhere.name}</span>&nbsp;· {chosenElsewhere.sku} · chosen
                    </span>
                  )}
                  <button
                    onClick={() => {
                      setSearchingLine(searchingLine === edit.id ? null : edit.id);
                      setSearch("");
                    }}
                    className="min-h-11 rounded-lg border-2 border-dashed border-border-strong px-2 text-sm font-bold"
                  >
                    Search {words.plural}
                  </button>
                  <button
                    onClick={() => update(index, { createNew: true, matchedProductId: null })}
                    aria-pressed={edit.createNew}
                    className={
                      "min-h-11 rounded-lg px-2 text-sm font-bold " +
                      (edit.createNew ? "bg-primary text-primary-foreground" : "border-2 border-border-strong bg-surface")
                    }
                  >
                    New {words.singular}
                  </button>
                </div>

                {searchingLine === edit.id && (
                  <div className="mt-1.5 rounded-lg border border-border">
                    <input
                      value={search}
                      autoFocus
                      onChange={(e) => setSearch(e.target.value)}
                      placeholder={`Search ${words.plural}`}
                      className="h-11 w-full rounded-t-lg border-b border-border bg-surface px-2 text-base focus:outline-none"
                    />
                    {found.map((p) => (
                      <button
                        key={p.id}
                        onClick={() => {
                          update(index, { matchedProductId: p.id, createNew: false });
                          setSearchingLine(null);
                          setSearch("");
                        }}
                        className="flex min-h-11 w-full items-center justify-between gap-2 border-t border-border px-2 text-left active:bg-primary-soft"
                      >
                        <span className="min-w-0 truncate text-base">{p.name}</span>
                        <span className="shrink-0 text-sm text-muted-foreground">{p.sku}</span>
                      </button>
                    ))}
                    {q !== "" && found.length === 0 && <p className="p-2 text-sm text-muted-foreground">No match.</p>}
                  </div>
                )}

                {edit.createNew && (
                  <label className="mt-1.5 block text-sm font-semibold">
                    Price (KES)
                    <input
                      type="number"
                      inputMode="decimal"
                      min="0"
                      step="any"
                      value={edit.sellPriceText}
                      onChange={(e) => update(index, { sellPriceText: e.target.value })}
                      className={inputClass + " text-right font-money"}
                    />
                  </label>
                )}

                {unmatched && (
                  <p className="mt-1 text-sm font-semibold text-warning-strong">
                    Choose a {words.singular} above
                  </p>
                )}
              </div>
            </div>
          );
        })}
      </main>

      {/* Pinned: the totals check, then the buttons. */}
      <section className="shrink-0 border-t-2 border-border bg-surface">
        <div
          role="status"
          className={
            "mx-2 mt-2 rounded-lg px-3 py-2 text-base " +
            (agree ? "bg-primary-soft text-primary" : "border-2 border-warning-strong bg-warning text-warning-foreground")
          }
        >
          {extracted === null ? (
            <>
              Invoice total unclear. Lines{" "}
              <span className="font-money font-bold">{shillings(linesTotal)}</span>
            </>
          ) : (
            <>
              Invoice <span className="font-money font-bold">{shillings(extracted)}</span>, lines{" "}
              <span className="font-money font-bold">{shillings(linesTotal)}</span>
              {agree ? (
                <>. They agree.</>
              ) : (
                <>
                  .{" "}
                  <span className="rounded bg-error px-1.5 font-money font-bold text-error-foreground">
                    {shillings(Math.abs(extracted - linesTotal))} apart
                  </span>
                </>
              )}
            </>
          )}
        </div>

        {saved !== "" && (
          <p role="status" className="mx-2 mt-2 rounded-lg bg-primary-soft px-3 py-2 text-base font-semibold text-primary">
            {saved}
          </p>
        )}
        {!committing && problemBox}

        {discarding ? (
          <div className="m-2 space-y-2 rounded-lg border-2 border-warning-strong bg-warning p-2 text-warning-foreground">
            <p className="text-base font-bold">Discard this invoice?</p>
            <div className="flex gap-2">
              <button
                onClick={() => setDiscarding(false)}
                className="min-h-12 flex-1 rounded-lg border-2 border-border-strong bg-surface text-base font-bold text-foreground"
              >
                Keep it
              </button>
              <button
                onClick={discard}
                disabled={busy !== ""}
                className="min-h-12 flex-1 rounded-lg bg-error text-base font-bold text-error-foreground disabled:opacity-40"
              >
                {busy === "discard" ? "Discarding…" : "Discard"}
              </button>
            </div>
          </div>
        ) : (
          <div className="flex gap-2 p-2">
            <button
              onClick={() => setDiscarding(true)}
              disabled={busy !== ""}
              className="min-h-14 shrink-0 rounded-xl border-2 border-border-strong bg-surface px-3 text-base font-bold"
            >
              Discard
            </button>
            <button
              onClick={save}
              disabled={busy !== ""}
              className="min-h-14 flex-1 rounded-xl border-2 border-primary bg-surface px-2 text-base font-bold text-primary"
            >
              {busy === "save" ? "Saving…" : "Save"}
            </button>
            <button
              onClick={openCommit}
              disabled={busy !== ""}
              className="min-h-14 flex-1 rounded-xl bg-primary px-2 text-xl font-bold text-primary-foreground disabled:opacity-40"
            >
              Receive
            </button>
          </div>
        )}
      </section>

      {/* Commit: how it was paid and when it came, then one stock receipt. */}
      {committing && (
        <div className="fixed inset-0 z-20 flex items-end justify-center bg-foreground/50 md:items-center">
          <div role="dialog" aria-label="Receive stock" className="w-full max-w-md space-y-3 rounded-t-2xl bg-surface p-3 md:rounded-2xl">
            <p className="text-xl font-bold">Receive stock</p>
            <p className="text-base">
              {acceptedCount} {acceptedCount === 1 ? "line" : "lines"}, KES{" "}
              <span className="font-money font-bold">{kes(linesTotal)}</span>
              {supplierName.trim() !== "" && <> from {supplierName.trim()}</>}.
            </p>
            <PaidWithChoice
              value={paidWith}
              onChange={(value) => {
                setPaidWith(value);
                setProblem("");
                setClosedToday(null);
              }}
            />
            <label className="block text-sm font-semibold">
              Date received
              <input
                type="date"
                value={receiptDate}
                max={todayHere()}
                onChange={(e) => {
                  setReceiptDate(e.target.value);
                  setClosedToday(null);
                }}
                className={inputClass}
              />
            </label>
            {problem !== "" && (
              <p role="alert" className="rounded-lg bg-error px-3 py-2 text-base text-error-foreground">
                {problem}
              </p>
            )}
            {closedToday !== null && (
              <button
                onClick={() => commit(closedToday)}
                disabled={busy !== ""}
                className="min-h-14 w-full rounded-xl border-2 border-primary bg-surface text-base font-bold text-primary disabled:opacity-40"
              >
                {busy === "commit" ? "Receiving…" : "Receive today instead"}
              </button>
            )}
            <div className="flex gap-2">
              <button
                onClick={() => {
                  setCommitting(false);
                  setProblem("");
                  setClosedToday(null);
                }}
                disabled={busy !== ""}
                className="min-h-14 flex-1 rounded-xl border-2 border-border-strong bg-surface text-base font-bold"
              >
                Not yet
              </button>
              <button
                onClick={() => commit()}
                disabled={busy !== ""}
                className="min-h-14 flex-1 rounded-xl bg-primary text-xl font-bold text-primary-foreground disabled:opacity-40"
              >
                {busy === "commit" ? "Receiving…" : "Receive stock"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
