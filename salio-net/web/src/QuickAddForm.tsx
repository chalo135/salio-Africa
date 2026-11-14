import { useEffect, useState } from "react";
import { API } from "./auth";
import { apiFetch, problemText } from "./api";

// What the server sends back for a part added at the counter: enough to go
// straight into the cart.
export type QuickAdded = {
  id: string;
  sku: string;
  name: string;
  sellPriceMinor: number;
};

// Shillings typed, cents sent. Null when the box is empty or not a number.
function minorOf(text: string): number | null {
  if (text.trim() === "") {
    return null;
  }

  const shillings = Number(text);
  return Number.isFinite(shillings) ? Math.round(shillings * 100) : null;
}

// A part the shelf did not know, added from the till's search in one screen.
// No SKU, no shelf, no category: the server makes the SKU and the rest can be
// filled in later. A blank cost is allowed on purpose: the part still sells.
//
// A Cashier sees name and price only. Cost and stock are the Owner's
// (locked rule 13), and the server ignores them from a Cashier anyway.
//
// An Owner sees quantity and cost only while the shop has never sold
// anything: then the stock was on the shelf before the books opened, and is
// opening stock. Once the shop trades, new stock is bought, and goes through
// Stock in. The server says which (GET quick-add/rules).
export default function QuickAddForm({
  shopId,
  typed,
  heading,
  saveLabel,
  isOwner,
  onAdded,
  onCancel,
  onExpired,
}: {
  shopId: string;
  typed: string;
  // Opened from "Add a part" rather than from a search, there is no typed
  // name to show and no cart to add to: the caller says what the top and the
  // button read instead. Left out, they read as they always have.
  heading?: string;
  saveLabel?: string;
  isOwner: boolean;
  onAdded: (added: QuickAdded, quantity: number | null) => void;
  onCancel: () => void;
  onExpired: () => void;
}) {
  const [name, setName] = useState(typed.trim());
  const [priceText, setPriceText] = useState("");
  const [costText, setCostText] = useState("");
  const [quantityText, setQuantityText] = useState("");

  const [saving, setSaving] = useState(false);
  const [problem, setProblem] = useState("");

  // Null while asking. Hidden until the server says yes: if the question
  // fails, quantity and cost stay hidden, and the part still saves.
  const [stockAllowed, setStockAllowed] = useState<boolean | null>(null);

  useEffect(() => {
    if (!isOwner) {
      return;
    }
    let current = true;

    apiFetch(`${API}/api/products/quick-add/rules?organizationId=${shopId}`, { credentials: "include" })
      .then(async (response) => {
        if (!current) return;
        if (!response.ok) {
          setStockAllowed(false);
          return;
        }
        const rules: { stockAllowed?: boolean } = await response.json();
        if (current) setStockAllowed(rules.stockAllowed === true);
      })
      .catch(() => {
        if (current) setStockAllowed(false);
      });

    return () => {
      current = false;
    };
  }, [shopId, isOwner]);

  // Quantity and cost are on the form only for an Owner, before the first sale.
  const showsStock = isOwner && stockAllowed === true;

  // Said before sending. The server checks again.
  function whatIsWrong() {
    const price = minorOf(priceText);

    if (name.trim() === "") {
      return "Type a name.";
    }
    if (price === null || price < 0) {
      return "Type a price";
    }
    if (!showsStock) {
      return "";
    }
    if (costText.trim() !== "" && minorOf(costText) === null) {
      return "Cost must be a number";
    }
    if (quantityText.trim() !== "" && !(Number.isInteger(Number(quantityText)) && Number(quantityText) > 0)) {
      return "Quantity must be a whole number";
    }
    // Stock at no cost is not allowed (decision D8). The part itself can
    // still be added without a cost: it just has no stock yet.
    if (quantityText.trim() !== "" && costText.trim() === "") {
      return "Quantity needs a cost";
    }
    // And the other way round: a cost with nothing on the shelf records nothing.
    if (costText.trim() !== "" && quantityText.trim() === "") {
      return "Cost needs a quantity";
    }
    return "";
  }

  async function save() {
    if (saving) {
      return;
    }

    const wrong = whatIsWrong();
    if (wrong !== "") {
      setProblem(wrong);
      return;
    }

    setSaving(true);
    setProblem("");

    const quantity = showsStock && quantityText.trim() !== "" ? Number(quantityText) : null;

    const body = {
      organizationId: shopId,
      name: name.trim(),
      sellPriceMinor: minorOf(priceText),
      unitCostMinor: showsStock ? minorOf(costText) : null,
      quantity,
    };

    try {
      const response = await apiFetch(`${API}/api/products/quick-add?organizationId=${shopId}`, {
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
        // Everything typed stays where it is.
        setProblem(await problemText(response));
        return;
      }

      onAdded(await response.json(), quantity);
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      setSaving(false);
    }
  }

  const boxClass =
    "mt-1 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg focus:border-primary focus:outline-none";

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          type="button"
          onClick={onCancel}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold"
        >
          ‹ Sell
        </button>
        <span className="min-w-0 truncate text-base font-bold">{heading ?? `Add "${typed.trim()}"`}</span>
      </div>

      {/* A form, so Enter in any box saves. */}
      <form
        onSubmit={(e) => {
          e.preventDefault();
          save();
        }}
        className="min-h-0 flex-1 overflow-y-auto px-3 py-3"
      >
        <div className="mx-auto max-w-md">
          <label className="block text-base font-semibold md:text-sm">
            Name
            <input
              id="quick-name"
              value={name}
              onChange={(e) => {
                setName(e.target.value);
                setProblem("");
              }}
              autoFocus
              maxLength={200}
              className={boxClass}
            />
          </label>

          <label className="mt-3 block text-base font-semibold md:text-sm">
            Price
            <input
              id="quick-price"
              value={priceText}
              onChange={(e) => {
                setPriceText(e.target.value);
                setProblem("");
              }}
              inputMode="decimal"
              enterKeyHint={showsStock ? "next" : "done"}
              className={boxClass + " font-money"}
            />
          </label>

          {showsStock && (
            <div className="mt-3 flex gap-3">
              <label className="block min-w-0 flex-1 text-base font-semibold md:text-sm">
                Cost
                <input
                  id="quick-cost"
                  value={costText}
                  onChange={(e) => {
                    setCostText(e.target.value);
                    setProblem("");
                  }}
                  inputMode="decimal"
                  className={boxClass + " font-money"}
                />
                <span className="mt-1 block text-sm font-normal text-muted-foreground">
                  Optional
                </span>
              </label>
              <label className="block w-28 shrink-0 text-base font-semibold md:text-sm">
                Quantity
                <input
                  id="quick-quantity"
                  value={quantityText}
                  onChange={(e) => {
                    setQuantityText(e.target.value);
                    setProblem("");
                  }}
                  inputMode="numeric"
                  className={boxClass + " text-center font-money"}
                />
              </label>
            </div>
          )}

          {/* The shop has traded: new stock is bought, not opening stock. */}
          {isOwner && stockAllowed === false && (
            <p className="mt-3 text-sm text-muted-foreground">Use Stock in for stock</p>
          )}

          {problem !== "" && (
            <p role="alert" className="mt-4 rounded-lg bg-error px-3 py-2 text-base text-error-foreground">
              {problem}
            </p>
          )}

          <button
            type="submit"
            disabled={saving}
            className="mt-4 h-14 w-full rounded-xl bg-primary text-xl font-bold text-primary-foreground disabled:opacity-40"
          >
            {saving ? "Adding…" : saveLabel ?? "Add to cart"}
          </button>
        </div>
      </form>
    </div>
  );
}
