import { useState, useEffect, useRef } from "react";
import { API, kes, qty, type ItemWords } from "./auth";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { apiFetch, problemText } from "./api";

// What was on the shelves on the day the shop started using Salio. Owner
// only: it is stock value (locked rule 13). The server books it as stock
// the owner brought in, and refuses it once the shop has made a sale.

// What this screen needs from /api/products.
type Product = {
  id: string;
  sku: string;
  name: string;
  sellPriceMinor: number;
  quantityOnHand: number;
};

// Quantity and cost are kept as the text typed, so "2." or an empty box
// mid-edit stays on screen as typed. They become numbers only to add up
// and to send.
type OpeningLine = {
  productId: string;
  name: string;
  sku: string;
  sellPriceMinor: number;
  quantityOnHand: number; // what Salio showed when the line was added
  quantityText: string;
  costText: string; // shillings, as typed: "380" means KES 380.00
};

// Today as the shop counts it, from the phone's clock. Not toISOString,
// which is UTC and would call anything before 3am yesterday.
function todayHere() {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${now.getFullYear()}-${month}-${day}`;
}

// "2 Nov 2026"
function dayLabel(day: string) {
  const [year, month, date] = day.split("-").map(Number);
  return new Date(year, month - 1, date).toLocaleDateString("en-KE", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

function quantityOf(line: OpeningLine) {
  return Number(line.quantityText) || 0;
}

// The server keeps three decimals and refuses more. "2.5" is fine,
// "2.5555" is not.
function tooManyDecimals(line: OpeningLine) {
  const parts = line.quantityText.trim().split(".");
  return parts.length > 1 && parts[1].length > 3;
}

// Shillings typed, cents sent. Rounded, so 10.505 cannot become a fraction
// of a cent.
function costMinorOf(line: OpeningLine) {
  return Math.round((Number(line.costText) || 0) * 100);
}

function lineTotalMinor(line: OpeningLine) {
  return Math.round(quantityOf(line) * costMinorOf(line));
}

export default function OpeningStockScreen({
  shopId,
  words,
  onBack,
  onOpenParts,
  onSaved,
  onExpired,
}: {
  shopId: string;
  words: ItemWords;
  onBack: () => void;
  onOpenParts: () => void; // a new shop has nothing to count yet
  onSaved: () => void; // reloads the till's shelf
  onExpired: () => void;
}) {
  // Its own copy of the shelf, so it knows when the list has loaded: a new
  // shop's empty list and a list still on its way look the same otherwise.
  const [products, setProducts] = useState<Product[] | null>(null);
  const [loadProblem, setLoadProblem] = useState("");
  const [loadCount, setLoadCount] = useState(0);

  const [openingDate, setOpeningDate] = useState(todayHere);
  const [search, setSearch] = useState("");
  const [lines, setLines] = useState<OpeningLine[]>([]);

  // True while the "are you sure" box is showing in place of Save.
  const [confirming, setConfirming] = useState(false);
  const [saving, setSaving] = useState(false);

  // The real lock against a double tap: it changes at once, without
  // waiting for a render.
  const savingNow = useRef(false);
  const [problem, setProblem] = useState("");
  const [saved, setSaved] = useState("");

  useEffect(() => {
    let current = true;

    apiFetch(`${API}/api/products?organizationId=${shopId}`, { credentials: "include" })
      .then(async (response) => {
        if (!current) {
          return;
        }
        if (response.status === 401) {
          onExpired();
          return;
        }
        if (!response.ok) {
          setLoadProblem("List didn't load.");
          return;
        }
        setProducts(await response.json());
        setLoadProblem("");
      })
      .catch(() => {
        if (current) {
          setLoadProblem("Can't reach Salio.");
        }
      });

    // A slow answer for an old request must not overwrite a newer one.
    return () => {
      current = false;
    };
    // onExpired is a new function on every render of App; the list only
    // needs loading when the shop changes or a reload is asked for.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, loadCount]);

  const q = search.trim().toLowerCase();
  const matches =
    q === "" || products === null
      ? []
      : products.filter(
          (p) => p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q),
        );

  let totalMinor = 0;
  for (const line of lines) {
    totalMinor = totalMinor + lineTotalMinor(line);
  }

  function addLine(product: Product) {
    setSearch("");
    setProblem("");
    setSaved("");
    setConfirming(false);

    // One line per product: the server refuses the same one twice.
    if (lines.some((line) => line.productId === product.id)) {
      return;
    }

    setLines([
      ...lines,
      {
        productId: product.id,
        name: product.name,
        sku: product.sku,
        sellPriceMinor: product.sellPriceMinor,
        quantityOnHand: product.quantityOnHand,
        quantityText: "",
        costText: "",
      },
    ]);
  }

  function updateLine(index: number, changes: Partial<OpeningLine>) {
    setProblem("");
    setConfirming(false);
    setLines(lines.map((line, i) => (i === index ? { ...line, ...changes } : line)));
  }

  function removeLine(index: number) {
    setProblem("");
    setConfirming(false);
    setLines(lines.filter((_, i) => i !== index));
  }

  // Refused here, before sending, with the line named.
  function checkBeforeSending() {
    if (openingDate === "") {
      return "Choose a date";
    }

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const label = `Line ${i + 1} (${line.name})`;

      if (!(quantityOf(line) > 0)) {
        return `${label}: quantity must be above zero`;
      }

      if (tooManyDecimals(line)) {
        return `${label}: at most 3 decimal places`;
      }

      if (!(costMinorOf(line) > 0)) {
        return `${label}: cost must be above zero`;
      }
    }

    return "";
  }

  // Save asks first: opening stock goes into the books and cannot be
  // edited afterwards.
  function askToSave() {
    const notReady = checkBeforeSending();

    if (notReady !== "") {
      setProblem(notReady);
      return;
    }

    setProblem("");
    setConfirming(true);
  }

  async function save() {
    if (savingNow.current) {
      return;
    }

    savingNow.current = true;
    setSaving(true);
    setProblem("");
    setSaved("");

    const body = {
      organizationId: shopId,
      openingDate,
      lines: lines.map((line) => ({
        productId: line.productId,
        quantity: quantityOf(line),
        unitCostMinor: costMinorOf(line),
      })),
    };

    try {
      const response = await apiFetch(`${API}/api/opening-stock`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        // Keep every typed line, so nothing has to be entered again.
        setConfirming(false);
        setProblem(await problemText(response));
        return;
      }

      const result: { openingDate: string; totalCostMinor: number; lineCount: number } =
        await response.json();

      const what = result.lineCount === 1 ? words.singular : words.plural;
      setSaved(
        `Opening stock saved: ${result.lineCount} ${what}, KES ${kes(result.totalCostMinor)}, dated ${dayLabel(result.openingDate)}.`,
      );

      setLines([]);
      setConfirming(false);

      // This screen's list and the till's shelf both show the new
      // quantities. Neither is awaited.
      setLoadCount(loadCount + 1);
      onSaved();
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      savingNow.current = false;
      setSaving(false);
    }
  }

  const showLoading = useShowLoader(products === null && loadProblem === "");

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold md:hidden"
        >
          ‹ More
        </button>
        <span className="text-base font-bold">Opening stock</span>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto">
        <section className="space-y-3 border-b border-border bg-surface p-3">
          <p className="text-base">
            What was already on your shelves when you started. Nobody paid for it today.
          </p>

          {/* The rule, in full, before anything is typed. */}
          <div className="rounded-lg bg-warning px-3 py-2 text-base text-warning-foreground">
            <p className="font-bold">Once per {words.singular} only</p>
            <p>Closes after your first sale</p>
          </div>

          <label className="block text-sm font-semibold">
            Opening date
            <input
              type="date"
              value={openingDate}
              max={todayHere()}
              onChange={(e) => {
                setOpeningDate(e.target.value);
                setConfirming(false);
              }}
              className="mt-1 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-base
                focus:border-primary focus:outline-none"
            />
          </label>
        </section>

        {loadProblem !== "" && (
          <div className="border-b border-border bg-surface p-3">
            <p className="text-base">{loadProblem}</p>
            <button
              onClick={() => setLoadCount(loadCount + 1)}
              className="mt-2 min-h-11 rounded-lg border-2 border-primary px-3 text-base font-bold text-primary"
            >
              Try again
            </button>
          </div>
        )}

        {products === null && loadProblem === "" && showLoading && (
          <SalioLoader size="inline" message="Loading…" />
        )}

        {/* A new shop: nothing to count until its things are in Salio. */}
        {products !== null && products.length === 0 && (
          <div className="border-b border-border bg-surface p-3">
            <p className="text-base font-semibold">Nothing here yet</p>
            <p className="text-base">Add {words.plural} first</p>
            <button
              onClick={onOpenParts}
              className="mt-2 h-12 w-full rounded-lg bg-primary text-lg font-bold text-primary-foreground"
            >
              Open Inventory
            </button>
          </div>
        )}

        {/* Find a product the way the till does: name or SKU. */}
        {products !== null && products.length > 0 && (
          <section className="border-b border-border bg-surface p-2">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={`Search ${words.plural}`}
              className="h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
                placeholder:text-muted-foreground focus:border-primary focus:outline-none"
            />
          </section>
        )}

        {matches.map((product) => (
          <button
            key={product.id}
            onClick={() => addLine(product)}
            className="flex min-h-14 w-full items-center justify-between gap-3 border-b
              border-border bg-surface px-3 py-2 text-left active:bg-primary-soft"
          >
            <span className="min-w-0">
              <span className="block truncate text-base font-semibold">{product.name}</span>
              <span className="text-sm text-muted-foreground">
                {product.sku} · <span className="font-money">{qty(product.quantityOnHand)}</span> in stock
              </span>
            </span>
            <span className="shrink-0 text-base font-bold text-primary">+ Add</span>
          </button>
        ))}

        {q !== "" && products !== null && matches.length === 0 && (
          <p className="p-3 text-base md:text-sm text-muted-foreground">No match.</p>
        )}

        {/* The lines: one per product. */}
        {lines.map((line, index) => {
          const dearerThanPrice = costMinorOf(line) > line.sellPriceMinor;

          return (
            <div key={line.productId} className="border-b border-border bg-surface px-3 py-2">
              <div className="flex items-start justify-between gap-2">
                <span className="min-w-0">
                  <span className="block text-base font-semibold">
                    {index + 1}. {line.name}
                  </span>
                  <span className="text-sm text-muted-foreground">
                    {line.sku} · sells at <span className="font-money">{kes(line.sellPriceMinor)}</span>
                  </span>
                </span>
                <button
                  onClick={() => removeLine(index)}
                  className="h-11 w-11 shrink-0 rounded-lg border-2 border-border-strong text-xl font-bold"
                  aria-label={"Remove " + line.name}
                >
                  ×
                </button>
              </div>

              <div className="mt-2 flex items-end gap-2">
                <label className="w-24 shrink-0 text-sm font-semibold">
                  Quantity
                  <input
                    type="number"
                    inputMode="decimal"
                    min="0"
                    step="any"
                    value={line.quantityText}
                    onChange={(e) => updateLine(index, { quantityText: e.target.value })}
                    className="mt-0.5 h-11 w-full rounded-lg border-2 border-border-strong px-2 text-center font-money text-base"
                  />
                </label>
                <label className="min-w-0 flex-1 text-sm font-semibold">
                  Cost (KES)
                  <input
                    type="number"
                    inputMode="decimal"
                    min="0"
                    step="any"
                    value={line.costText}
                    onChange={(e) => updateLine(index, { costText: e.target.value })}
                    className="mt-0.5 h-11 w-full rounded-lg border-2 border-border-strong px-2 text-right font-money text-base"
                  />
                </label>
                <span className="w-24 shrink-0 pb-2.5 text-right font-money text-base font-semibold">
                  {kes(lineTotalMinor(line))}
                </span>
              </div>

              {/* A hint, not a check: the server does not refuse a second
                  opening stock for the same part before the first sale. If
                  Salio already shows some, this would add to it. */}
              {line.quantityOnHand > 0 && (
                <p className="mt-1 text-sm font-semibold text-warning-strong">
                  Already {qty(line.quantityOnHand)} in stock. This adds to it.
                </p>
              )}

              {/* Warn, never block, as Stock in does. */}
              {dearerThanPrice && (
                <p className="mt-1 text-sm font-semibold text-warning-strong">
                  Costs more than its price
                </p>
              )}
            </div>
          );
        })}

        {lines.length === 0 && q === "" && saved === "" && products !== null && products.length > 0 && (
          <p className="p-4 text-base md:text-sm text-muted-foreground">Nothing added yet.</p>
        )}
      </main>

      {/* Pinned: the running total and Save. */}
      <section className="shrink-0 border-t-2 border-border bg-surface">
        {saved !== "" && (
          <p role="status" className="mx-2 mt-2 rounded-lg bg-primary-soft px-3 py-2 text-base font-semibold text-primary">
            {saved}
          </p>
        )}

        {problem !== "" && (
          <div role="alert" className="mx-2 mt-2 rounded-lg bg-error px-3 py-2 text-error-foreground">
            <p className="text-base font-bold">Not saved</p>
            <p className="text-base">{problem}</p>
          </div>
        )}

        <div className="flex items-baseline justify-between gap-2 px-3 py-2">
          <span className="text-base font-semibold">Shelf value</span>
          <span className="whitespace-nowrap font-money text-2xl font-bold">KES {kes(totalMinor)}</span>
        </div>

        <div className="px-2 pb-2">
          {confirming ? (
            <div className="space-y-2">
              <p className="rounded-lg bg-warning px-3 py-2 text-base text-warning-foreground">
                {lines.length} {lines.length === 1 ? words.singular : words.plural}, KES {kes(totalMinor)}, dated{" "}
                {dayLabel(openingDate)}. Can't be changed later.
              </p>
              <button
                onClick={save}
                disabled={saving}
                className="h-14 w-full rounded-xl bg-primary text-xl font-bold
                  text-primary-foreground disabled:opacity-40"
              >
                {saving ? "Saving…" : "Yes, save"}
              </button>
              <button
                onClick={() => setConfirming(false)}
                disabled={saving}
                className="h-12 w-full rounded-xl border-2 border-border-strong text-lg font-bold disabled:opacity-40"
              >
                Go back
              </button>
            </div>
          ) : (
            <button
              onClick={askToSave}
              disabled={lines.length === 0 || saving}
              className="h-14 w-full rounded-xl bg-primary text-xl font-bold
                text-primary-foreground disabled:opacity-40"
            >
              Save
            </button>
          )}
        </div>
      </section>
    </div>
  );
}
