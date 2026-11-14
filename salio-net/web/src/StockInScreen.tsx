import { useState, useRef } from "react";
import { API, kes, qty } from "./auth";
import PaidWithChoice from "./PaidWithChoice";
import { paidWithLabel } from "./paidWith";
import { apiFetch, problemText } from "./api";

// What this screen needs to know about a product. The till loads the shelf;
// this screen searches the same list.
type Product = {
  id: string;
  sku: string;
  name: string;
  sellPriceMinor: number;
};

// Quantity and cost are kept as the text typed, so "2." or an empty box
// mid-edit stays on screen as typed. They become numbers only to add up
// and to send.
type ReceiptLine = {
  productId: string;
  name: string;
  sku: string;
  sellPriceMinor: number;
  quantityText: string;
  costText: string; // shillings, as typed: "1050" means KES 1,050.00
};

// Today as the shop counts it, from the phone's clock. Not toISOString,
// which is UTC and would call anything before 3am yesterday.
function todayHere() {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${now.getFullYear()}-${month}-${day}`;
}

function quantityOf(line: ReceiptLine) {
  return Number(line.quantityText) || 0;
}

// Shillings typed, cents sent. Rounded, so 10.505 cannot become a fraction
// of a cent.
function costMinorOf(line: ReceiptLine) {
  return Math.round((Number(line.costText) || 0) * 100);
}

function lineTotalMinor(line: ReceiptLine) {
  return Math.round(quantityOf(line) * costMinorOf(line));
}

export default function StockInScreen({
  shopId,
  products,
  startWith,
  backLabel = "More",
  onBack,
  onReceived,
  onExpired,
}: {
  shopId: string;
  products: Product[];
  // Opened from "Parts needing a cost": that part is already on the delivery.
  startWith?: Product;
  // Where the back button goes, in words. "More" unless opened from elsewhere.
  backLabel?: string;
  onBack: () => void;
  onReceived: () => void; // reloads the shelf
  onExpired: () => void;
}) {
  const [receiptDate, setReceiptDate] = useState(todayHere);

  // No default. Booking a credit delivery as cash takes money out of the
  // drawer that never left it, so the shopkeeper has to choose.
  const [paidWith, setPaidWith] = useState<number | null>(null);
  const [supplierName, setSupplierName] = useState("");

  const [search, setSearch] = useState("");
  const [lines, setLines] = useState<ReceiptLine[]>(() =>
    startWith
      ? [
          {
            productId: startWith.id,
            name: startWith.name,
            sku: startWith.sku,
            sellPriceMinor: startWith.sellPriceMinor,
            quantityText: "",
            costText: "",
          },
        ]
      : [],
  );

  const [saving, setSaving] = useState(false);

  // The disabled button stops a second tap once React has re-rendered. Two
  // taps inside the same moment can both arrive before that, so this ref
  // is the real lock: it changes at once, without waiting for a render.
  const savingNow = useRef(false);
  const [problem, setProblem] = useState("");
  const [received, setReceived] = useState("");

  const q = search.trim().toLowerCase();
  const matches =
    q === ""
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
    setReceived("");

    // One line per product: a second tap should not quietly double it.
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
        quantityText: "",
        costText: "",
      },
    ]);
  }

  function updateLine(index: number, changes: Partial<ReceiptLine>) {
    setProblem("");
    setLines(lines.map((line, i) => (i === index ? { ...line, ...changes } : line)));
  }

  function removeLine(index: number) {
    setProblem("");
    setLines(lines.filter((_, i) => i !== index));
  }

  // Refused here, before sending, with the line named. Zero is the cheap
  // typing mistake: an empty box, or a cost forgotten.
  function checkBeforeSending() {
    if (paidWith === null) {
      return "Choose how you paid";
    }

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const label = `Line ${i + 1} (${line.name})`;

      if (!(quantityOf(line) > 0)) {
        return `${label}: quantity must be above zero`;
      }

      if (!(costMinorOf(line) > 0)) {
        return `${label}: cost must be above zero`;
      }
    }

    return "";
  }

  async function save() {
    if (savingNow.current) {
      return;
    }

    const notReady = checkBeforeSending();

    if (notReady !== "") {
      setProblem(notReady);
      return;
    }

    savingNow.current = true;
    setSaving(true);
    setProblem("");
    setReceived("");

    const body = {
      organizationId: shopId,
      receiptDate,
      paidWith,
      supplierName: supplierName.trim() === "" ? null : supplierName.trim(),
      lines: lines.map((line) => ({
        productId: line.productId,
        quantity: quantityOf(line),
        unitCostMinor: costMinorOf(line),
      })),
    };

    try {
      const response = await apiFetch(`${API}/api/stock-receipts`, {
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
        setProblem(await problemText(response));
        return;
      }

      const result: { totalCostMinor: number } = await response.json();

      const items = lines.map((line) => `${qty(quantityOf(line))} × ${line.name}`).join(", ");
      const how = paidWithLabel(paidWith);
      const from = body.supplierName ? ` from ${body.supplierName}` : "";
      setReceived(`Received ${items}${from}. KES ${kes(result.totalCostMinor)}, ${how}.`);

      setLines([]);
      setSupplierName("");
      setPaidWith(null);

      // Same as after a sale: the shelf catches up behind, not awaited.
      onReceived();
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      savingNow.current = false;
      setSaving(false);
    }
  }

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold md:hidden"
        >
          ‹ {backLabel}
        </button>
        <span className="text-base font-bold">Stock in</span>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto">
        {/* The delivery: when, how it was paid, who from. */}
        <section className="space-y-3 border-b border-border bg-surface p-3">
          <p className="text-base">Stock that has arrived since. You paid, or you owe.</p>
          <label className="block text-sm font-semibold">
            Date received
            <input
              type="date"
              value={receiptDate}
              max={todayHere()}
              onChange={(e) => setReceiptDate(e.target.value)}
              className="mt-1 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-base
                focus:border-primary focus:outline-none"
            />
          </label>

          <PaidWithChoice
            value={paidWith}
            onChange={(value) => {
              setPaidWith(value);
              setProblem("");
            }}
          />

          <label className="block text-sm font-semibold">
            Supplier (optional)
            <input
              value={supplierName}
              onChange={(e) => setSupplierName(e.target.value)}
              maxLength={200}
              className="mt-1 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-base
                placeholder:text-muted-foreground focus:border-primary focus:outline-none"
            />
          </label>
        </section>

        {/* Find a product the way the till does: name or SKU. */}
        <section className="border-b border-border bg-surface p-2">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search"
            className="h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
              placeholder:text-muted-foreground focus:border-primary focus:outline-none"
          />
        </section>

        {matches.map((product) => (
          <button
            key={product.id}
            onClick={() => addLine(product)}
            className="flex min-h-14 w-full items-center justify-between gap-3 border-b
              border-border bg-surface px-3 py-2 text-left active:bg-primary-soft"
          >
            <span className="min-w-0">
              <span className="block truncate text-base font-semibold">{product.name}</span>
              <span className="text-sm text-muted-foreground">{product.sku}</span>
            </span>
            <span className="shrink-0 text-base font-bold text-primary">+ Add</span>
          </button>
        ))}

        {q !== "" && matches.length === 0 && (
          <p className="p-3 text-base md:text-sm text-muted-foreground">No match.</p>
        )}

        {/* The lines of the delivery. */}
        {lines.map((line, index) => {
          const costMinor = costMinorOf(line);
          const dearerThanPrice = costMinor > line.sellPriceMinor;

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
                    autoFocus={startWith !== undefined && line.productId === startWith.id}
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

              {/* Warn, never block: the till's rule. The shop may be
                  clearing stock at a loss on purpose. */}
              {dearerThanPrice && (
                <p className="mt-1 text-sm font-semibold text-warning-strong">
                  Costs more than its price
                </p>
              )}
            </div>
          );
        })}

        {lines.length === 0 && q === "" && received === "" && (
          <p className="p-4 text-base md:text-sm text-muted-foreground">
            Nothing added yet.
          </p>
        )}
      </main>

      {/* Pinned: the total to check against the supplier's invoice, and Save. */}
      <section className="shrink-0 border-t-2 border-border bg-surface">
        {received !== "" && (
          <p role="status" className="mx-2 mt-2 rounded-lg bg-primary-soft px-3 py-2 text-base font-semibold text-primary">
            {received}
          </p>
        )}

        {problem !== "" && (
          <div role="alert" className="mx-2 mt-2 rounded-lg bg-error px-3 py-2 text-error-foreground">
            <p className="text-base font-bold">Not saved</p>
            <p className="text-base">{problem}</p>
          </div>
        )}

        <div className="flex items-baseline justify-between px-3 py-2">
          <span className="text-base font-semibold">Delivery total</span>
          <span className="font-money text-2xl font-bold">KES {kes(totalMinor)}</span>
        </div>

        <div className="px-2 pb-2">
          <button
            onClick={save}
            disabled={lines.length === 0 || saving}
            className="h-14 w-full rounded-xl bg-primary text-xl font-bold
              text-primary-foreground disabled:opacity-40"
          >
            {saving ? "Saving…" : "Save delivery"}
          </button>
        </div>
      </section>
    </div>
  );
}
