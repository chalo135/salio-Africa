import { useState, useEffect } from "react";

type Product = {
  id: string;
  sku: string;
  name: string;
  sellPriceMinor: number;
  binLocation: string | null;
  quantityOnHand: number;
  averageCostMinor: number;
};

type CartLine = {
  productId: string;
  name: string;
  quantity: number;
  unitPriceMinor: number;
};

const ORG_ID = "00000000-0000-0000-0000-000000000001";

// Money is whole cents everywhere. Divide by 100 only to show it.
function kes(minor: number) {
  return (minor / 100).toLocaleString("en-KE", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

// What one unit of this line costs the shop today, or 0 if nothing is known
// yet - a part that has never been received has no cost to compare against.
function costOf(line: CartLine, products: Product[]) {
  const product = products.find((p) => p.id === line.productId);
  return product ? product.averageCostMinor : 0;
}

function belowCost(line: CartLine, products: Product[]) {
  const cost = costOf(line, products);
  return cost > 0 && line.unitPriceMinor < cost;
}

export default function App() {
  const [products, setProducts] = useState<Product[]>([]);
  const [search, setSearch] = useState("");

  // One cart per customer at the counter, and which one is on screen.
  const [carts, setCarts] = useState<CartLine[][]>([[]]);
  const [activeCart, setActiveCart] = useState(0);
  const [paying, setPaying] = useState(false);

  // The cart being served right now. Everything below reads this.
  const lines = carts[activeCart] ?? [];

  useEffect(() => {
    fetch(`http://localhost:5077/api/products?organizationId=${ORG_ID}`)
      .then((response) => response.json())
      .then((data) => setProducts(data));
  }, []);

  const visible = products.filter((p) => {
    const q = search.toLowerCase();
    return p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q);
  });

  // Replaces only the active cart and leaves the others exactly as they were.
  function setActiveLines(newLines: CartLine[]) {
    setCarts(carts.map((cart, index) => (index === activeCart ? newLines : cart)));
  }

  function addToCart(product: Product) {
    setActiveLines([
      ...lines,
      {
        productId: product.id,
        name: product.name,
        quantity: 1,
        unitPriceMinor: product.sellPriceMinor,
      },
    ]);
  }

  function updateLine(index: number, changes: Partial<CartLine>) {
    setActiveLines(lines.map((line, i) => (i === index ? { ...line, ...changes } : line)));
  }

  function removeLine(index: number) {
    setActiveLines(lines.filter((_, i) => i !== index));
  }

  function newCart() {
    setCarts([...carts, []]);
    setActiveCart(carts.length);
  }

  let total = 0;
  for (const line of lines) {
    total = total + line.quantity * line.unitPriceMinor;
  }

  async function pay(method: number) {
  setPaying(true);

  const body = {
    organizationId: ORG_ID,
    saleDate: new Date().toISOString().slice(0, 10),
    lines: lines.map((line) => ({
      productId: line.productId,
      quantity: line.quantity,
      unitPriceMinor: line.unitPriceMinor,
      taxBand: "A",
    })),
    payments: [{ method: method, amountMinor: total, reference: null }],
  };

  try {
    const response = await fetch("http://localhost:5077/api/sales", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (response.ok) {
      const sale = await response.json();
      alert("Sale " + sale.saleNumber + " — KES " + kes(total));

      const remaining = carts.filter((_, index) => index !== activeCart);
      setCarts(remaining.length > 0 ? remaining : [[]]);
      setActiveCart(0);
    } else {
      alert("Failed: " + (await response.text()));
    }
  } catch {
    alert("Could not reach the till server. Is the API running?");
  } finally {
    setPaying(false);
  }
} 

  return (
    <div className="flex h-dvh flex-col bg-background text-foreground">
      {/* Search: always at the top, never scrolls away. */}
      <header className="shrink-0 border-b border-border bg-surface p-2">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search name or SKU"
          className="h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
            placeholder:text-muted-foreground focus:border-primary focus:outline-none"
        />
      </header>

      {/* Results: the only part of the screen that scrolls. */}
      <main className="min-h-0 flex-1 overflow-y-auto">
        {visible.map((product) => (
          <button
            key={product.id}
            onClick={() => addToCart(product)}
            className="flex min-h-14 w-full items-center justify-between gap-3 border-b
              border-border bg-surface px-3 py-2 text-left active:bg-primary-soft"
          >
            <span className="min-w-0">
              <span className="block truncate text-base font-semibold">{product.name}</span>
              <span className="mt-1 flex flex-wrap items-center gap-1.5 text-sm">
                <span className="rounded bg-primary-soft px-1.5 py-0.5 font-bold text-primary">
                  {product.binLocation ?? "No shelf"}
                </span>
                <span className="text-muted-foreground">{product.sku}</span>
                {product.quantityOnHand <= 0 ? (
                  <span className="rounded bg-warning px-1.5 py-0.5 font-bold text-warning-foreground">
                    Out of stock
                  </span>
                ) : (
                  <span className="font-money font-semibold text-muted-foreground">
                    {product.quantityOnHand} left
                  </span>
                )}
              </span>
            </span>
            <span className="shrink-0 font-money text-lg font-bold">{kes(product.sellPriceMinor)}</span>
          </button>
        ))}

        {visible.length === 0 && (
          <p className="p-4 text-base text-muted-foreground">No part matches that.</p>
        )}
      </main>

      {/* Cart: pinned to the bottom, so the total is never scrolled off. */}
      <section className="shrink-0 border-t-2 border-border bg-surface">
        {/* One tab per customer. The count stops a half-served cart being forgotten. */}
        <div className="flex gap-1 overflow-x-auto border-b border-border p-1.5">
          {carts.map((cart, index) => (
            <button
              key={index}
              onClick={() => setActiveCart(index)}
              className={
                "flex min-h-11 shrink-0 items-center gap-1.5 rounded-lg px-3 text-base font-semibold " +
                (index === activeCart
                  ? "bg-primary text-primary-foreground"
                  : "border-2 border-border-strong bg-surface text-foreground")
              }
            >
              Customer {index + 1}
              <span
                className={
                  "font-money rounded px-1.5 text-sm font-bold " +
                  (index === activeCart
                    ? "bg-primary-foreground text-primary"
                    : "bg-primary-soft text-primary")
                }
              >
                {cart.length}
              </span>
            </button>
          ))}

          <button
            onClick={newCart}
            className="min-h-11 shrink-0 rounded-lg border-2 border-primary px-3
              text-base font-bold text-primary"
          >
            + New
          </button>
        </div>

        <div className="max-h-32 overflow-y-auto">
          {lines.map((line, index) => (
            <div key={index} className="flex items-center gap-2 border-b border-border px-2 py-1.5">
              <span className="min-w-0 flex-1">
                <span className="block truncate text-base">{line.name}</span>
                {/* Selling under cost is allowed - the shop may have a reason.
                    Say so and let it through: a till that refuses a sale gets
                    worked around, and then there is no record of it at all. */}
                {belowCost(line, products) && (
                  <span className="block text-sm font-semibold text-warning-strong">
                    Below cost (KES {kes(costOf(line, products))})
                  </span>
                )}
              </span>
              <input
                type="number"
                inputMode="numeric"
                value={line.quantity}
                onChange={(e) => updateLine(index, { quantity: parseInt(e.target.value) || 0 })}
                className="h-11 w-14 rounded-lg border-2 border-border-strong text-center font-money text-base"
              />
              <span className="w-20 shrink-0 text-right font-money text-base font-semibold">
                {kes(line.unitPriceMinor * line.quantity)}
              </span>
              <button
                onClick={() => removeLine(index)}
                className="h-11 w-11 shrink-0 rounded-lg border-2 border-border-strong text-xl font-bold"
                aria-label={"Remove " + line.name}
              >
                ×
              </button>
            </div>
          ))}
        </div>

        <div className="flex items-baseline justify-between px-3 py-2">
          <span className="text-base font-semibold">Total</span>
          <span className="font-money text-2xl font-bold">KES {kes(total)}</span>
        </div>

        <div className="space-y-2 px-2 pb-2">
          <button
            onClick={() => pay(0)}
            disabled={lines.length === 0 || paying}
            className="h-14 w-full rounded-xl bg-primary text-xl font-bold
              text-primary-foreground disabled:opacity-40"
          >
            Pay Cash
          </button>
          <button
            onClick={() => pay(1)}
            disabled={lines.length === 0 || paying}
            className="h-14 w-full rounded-xl border-2 border-primary text-xl font-bold
              text-primary disabled:opacity-40"
          >
            Pay M-Pesa
          </button>
        </div>
      </section>
    </div>
  );
}
