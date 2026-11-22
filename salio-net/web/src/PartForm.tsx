import { useState } from "react";
import { API, kes, qty, type ItemWords } from "./auth";
import { apiFetch, problemText } from "./api";

// A part as the Parts screen knows it: the shelf list's shape, retired
// parts included.
export type Part = {
  id: string;
  sku: string;
  name: string;
  sellPriceMinor: number;
  binLocation: string | null;
  isActive: boolean;
  quantityOnHand: number;
  needsReceiving: boolean;
  // Left out by the server when this person may not see the buying price.
  averageCostMinor?: number | null;
  // True when the server allowed the buying price to be sent.
  buyingPriceShown?: boolean;
  // False when the cost came from an import or nobody ever stated one.
  // Sent only with the cost.
  costIsConfirmed?: boolean;
  // What a person last paid for one (stock in, invoice, opening stock).
  // Survives selling out; never an imported cost. Left out when hidden or
  // never bought. Guidance only.
  lastBoughtAtMinor?: number | null;
  // The shop's margin rule in percent, sent only when this part's last
  // bought price plus that margin is more than its asking price.
  marginUnderRulePercent?: number;
};

// Shillings typed, cents sent. Rounded, so 10.505 cannot become a fraction
// of a cent. Null when the box does not hold a number.
function priceMinorOf(priceText: string): number | null {
  if (priceText.trim() === "") {
    return null;
  }

  const shillings = Number(priceText);
  return Number.isFinite(shillings) ? Math.round(shillings * 100) : null;
}

// Cents shown as shillings in a box: 55000 becomes "550", 4550 "45.5".
function priceTextOf(minor: number) {
  return String(minor / 100);
}

// One form for both jobs. With a part it edits that part; without one it
// adds a new part.
export default function PartForm({
  shopId,
  part,
  words,
  onDone,
  onCancel,
  onExpired,
}: {
  shopId: string;
  part: Part | null;
  words: ItemWords;
  onDone: (message: string) => void;
  onCancel: () => void;
  onExpired: () => void;
}) {
  const [name, setName] = useState(part?.name ?? "");
  const [sku, setSku] = useState(part?.sku ?? "");
  const [priceText, setPriceText] = useState(part ? priceTextOf(part.sellPriceMinor) : "");
  const [shelf, setShelf] = useState(part?.binLocation ?? "");

  const [saving, setSaving] = useState(false);
  const [problem, setProblem] = useState("");

  // The Deactivate question, while it is on screen.
  const [confirmingDeactivate, setConfirmingDeactivate] = useState(false);

  const priceMinor = priceMinorOf(priceText);
  const shelfOrNull = shelf.trim() === "" ? null : shelf.trim();

  // Save stays off until something is really different. Spaces typed at
  // either end do not count as a change.
  const changed =
    part === null ||
    name.trim() !== part.name ||
    sku.trim() !== part.sku ||
    priceMinor !== part.sellPriceMinor ||
    shelfOrNull !== part.binLocation;

  // Said before sending, naming the box. The server checks again.
  function whatIsWrong() {
    if (name.trim() === "") {
      return `Type a name.`;
    }
    if (sku.trim() === "") {
      return `Type a SKU.`;
    }
    if (priceMinor === null) {
      return "Type a price";
    }
    if (priceMinor < 0) {
      return "Price can't be below 0.";
    }
    return "";
  }

  async function save() {
    const wrong = whatIsWrong();
    if (wrong !== "") {
      setProblem(wrong);
      return;
    }

    setSaving(true);
    setProblem("");

    const body = {
      name: name.trim(),
      sku: sku.trim(),
      sellPriceMinor: priceMinor,
      binLocation: shelfOrNull,
    };

    try {
      const response =
        part === null
          ? await apiFetch(`${API}/api/products`, {
              method: "POST",
              credentials: "include",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ ...body, organizationId: shopId }),
            })
          : await apiFetch(`${API}/api/products/${part.id}?organizationId=${shopId}`, {
              method: "PUT",
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

      onDone(part === null ? "Added " + body.name : "Saved " + body.name);
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      setSaving(false);
    }
  }

  async function setActive(isActive: boolean) {
    if (part === null) {
      return;
    }

    setSaving(true);
    setProblem("");

    try {
      const response = await apiFetch(
        `${API}/api/products/${part.id}/${isActive ? "activate" : "deactivate"}?organizationId=${shopId}`,
        { method: "POST", credentials: "include" },
      );

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        setProblem(await problemText(response));
        return;
      }

      onDone(isActive ? part.name + " is back" : part.name + " deactivated");
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      setSaving(false);
      setConfirmingDeactivate(false);
    }
  }

  const boxClass =
    "h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg focus:border-primary focus:outline-none";

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onCancel}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold"
        >
          ‹ {words.Plural}
        </button>
        <span className="min-w-0 truncate text-base font-bold">
          {part === null ? `Add ${words.singular}` : part.name}
        </span>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-3 py-3">
        {part !== null && !part.isActive && (
          <p className="mb-3 rounded-lg bg-warning px-3 py-2 text-base font-semibold text-warning-foreground">
            Inactive: can't be sold.
          </p>
        )}

        <label className="block text-base md:text-sm font-semibold">
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} className={"mt-1 " + boxClass} />
        </label>

        <label className="mt-3 block text-base md:text-sm font-semibold">
          SKU
          <input
            value={sku}
            onChange={(e) => setSku(e.target.value)}
            autoCapitalize="characters"
            className={"mt-1 " + boxClass}
          />
        </label>

        <div className="mt-3 flex gap-3">
          <label className="block flex-1 text-base md:text-sm font-semibold">
            Price
            <input
              value={priceText}
              onChange={(e) => setPriceText(e.target.value)}
              inputMode="decimal"
              className={"mt-1 font-money " + boxClass}
            />
          </label>
          <label className="block w-28 text-base md:text-sm font-semibold">
            Shelf
            <input
              value={shelf}
              onChange={(e) => setShelf(e.target.value)}
              autoCapitalize="characters"
              className={"mt-1 " + boxClass}
            />
          </label>
        </div>

        {/* Facts, not boxes. Nothing here can be typed. */}
        {part === null ? (
          <p className="mt-4 rounded-lg border border-border bg-surface px-3 py-2 text-base">
            Use <strong>Stock in</strong> for stock
          </p>
        ) : (
          <div className="mt-4 rounded-lg border border-border bg-surface px-3 py-2">
            <div className="flex items-baseline justify-between">
              <span className="text-base">In stock</span>
              <span className="font-money text-lg font-bold">
                {part.needsReceiving ? "Needs receiving (" + qty(part.quantityOnHand) + ")" : qty(part.quantityOnHand)}
              </span>
            </div>
            {/* Only when the server sent the buying price. Hidden from this
                person: the row is not there at all. */}
            {part.buyingPriceShown && (
              <div className="mt-1 flex items-baseline justify-between">
                <span className="text-base">
                  Average cost
                  {part.costIsConfirmed === false && (
                    <span className="block text-sm font-semibold text-warning-strong">cost not confirmed</span>
                  )}
                </span>
                <span className="font-money text-lg font-bold">
                  {part.needsReceiving
                    ? "Unknown"
                    : part.quantityOnHand <= 0 || part.averageCostMinor == null
                      ? "None in stock"
                      : "KES " + kes(part.averageCostMinor)}
                </span>
              </div>
            )}
          </div>
        )}

        {problem !== "" && (
          <div role="alert" className="mt-4 rounded-lg bg-error px-3 py-2 text-error-foreground">
            <p className="text-base font-bold">{part === null ? "NOT added" : "NOT saved"}</p>
            <p className="text-base">{problem}</p>
          </div>
        )}

        <button
          onClick={save}
          disabled={!changed || saving}
          className="mt-4 h-14 w-full rounded-xl bg-primary text-xl font-bold text-primary-foreground disabled:opacity-40"
        >
          {saving ? "Saving…" : part === null ? `Add ${words.singular}` : "Save"}
        </button>

        {/* Far from Save on purpose, behind a question. */}
        {part !== null && (
          <div className="mt-12 border-t border-border pt-4 pb-6">
            {!part.isActive ? (
              <button
                onClick={() => setActive(true)}
                disabled={saving}
                className="h-14 w-full rounded-xl border-2 border-primary bg-surface text-lg font-bold text-primary disabled:opacity-40"
              >
                Put back
              </button>
            ) : confirmingDeactivate ? (
              <div className="rounded-lg border-2 border-warning-strong bg-surface px-3 py-3">
                <p className="text-base">
                  Deactivate <strong>{part.name}</strong>? It can't be sold until put back.
                </p>
                <div className="mt-3 flex gap-2">
                  <button
                    onClick={() => setConfirmingDeactivate(false)}
                    className="h-12 flex-1 rounded-lg border-2 border-border-strong bg-surface text-base font-bold"
                  >
                    Keep it
                  </button>
                  <button
                    onClick={() => setActive(false)}
                    disabled={saving}
                    className="h-12 flex-1 rounded-lg bg-warning text-base font-bold text-warning-foreground disabled:opacity-40"
                  >
                    Yes, deactivate
                  </button>
                </div>
              </div>
            ) : (
              <button
                onClick={() => setConfirmingDeactivate(true)}
                className="h-14 w-full rounded-xl border-2 border-border-strong bg-surface text-lg font-bold"
              >
                Deactivate
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
