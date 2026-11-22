import { useState, useEffect } from "react";
import { API, kes, qty, type ItemWords } from "./auth";
import PartForm, { type Part } from "./PartForm";
import SellableNounSetting from "./SellableNounSetting";
import { apiFetch } from "./api";

// Every part the shop has ever had. Switched-off ones are hidden until the
// "inactive" count is tapped (an undone import leaves many). Owner only: App.tsx
// shows it to an Owner, and the server refuses a Cashier's changes anyway.
// Nothing here changes a quantity: stock moves only through sales and
// Stock in.
type Filter = "all" | "changed" | "out" | "noCost" | "inactive";

// No buying price a person stated: the stop and the margin note cannot work.
// Only asked where the buying price may be seen.
function needsCost(p: Part) {
  return p.buyingPriceShown === true && !(p.lastBoughtAtMinor != null && p.lastBoughtAtMinor > 0);
}

export default function PartsScreen({
  shopId,
  words,
  startAdding = false,
  onlyIds = null,
  openPartId = null,
  onChanged,
  onWordChanged,
  onExpired,
}: {
  shopId: string;
  words: ItemWords;
  // Opens straight on the "Add" form: used by the till's empty shelf.
  startAdding?: boolean;
  // Only these parts, until "Show all": the welcome card's "See everything
  // that changed".
  onlyIds?: string[] | null;
  // Opens straight on this part's form once the list loads: the card's Fix.
  openPartId?: string | null;
  onChanged: () => void;
  onWordChanged: () => void;
  onExpired: () => void;
}) {
  const [parts, setParts] = useState<Part[] | null>(null);
  const [loadProblem, setLoadProblem] = useState("");
  const [search, setSearch] = useState("");

  // Which chip is chosen (design/Inventory). Switched-off parts show only
  // under "Inactive". "Changed" is the welcome card's list of parts.
  const [filter, setFilter] = useState<Filter>(onlyIds !== null ? "changed" : "all");

  // Bumped to load the list again: after a change, or by "Try again".
  const [loadCount, setLoadCount] = useState(0);

  // Which form is open: a part being edited, "new" for adding, or none.
  const [editing, setEditing] = useState<Part | "new" | null>(startAdding ? "new" : null);

  // What just happened, said once at the top of the list.
  const [doneMessage, setDoneMessage] = useState("");

  // The card's Fix opens its part once, when the list first arrives.
  const [opened, setOpened] = useState(false);

  useEffect(() => {
    if (openPartId === null || opened || parts === null) {
      return;
    }
    setOpened(true);
    const part = parts.find((p) => p.id === openPartId);
    if (part) {
      setEditing(part);
    }
  }, [openPartId, opened, parts]);

  useEffect(() => {
    let current = true;

    apiFetch(`${API}/api/products?organizationId=${shopId}&includeInactive=true`, {
      credentials: "include",
    })
      .then(async (response) => {
        if (!current) {
          return;
        }
        if (response.status === 401) {
          onExpired();
          return;
        }
        if (!response.ok) {
          setLoadProblem("List didn't load");
          return;
        }
        setParts(await response.json());
        setLoadProblem("");
      })
      .catch(() => {
        if (current) {
          setLoadProblem("Can't reach Salio");
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

  if (editing !== null) {
    return (
      <PartForm
        shopId={shopId}
        words={words}
        part={editing === "new" ? null : editing}
        onCancel={() => setEditing(null)}
        onExpired={onExpired}
        onDone={(message) => {
          setEditing(null);
          setDoneMessage(message);
          setLoadCount(loadCount + 1);
          // The till's shelf shows names and prices too.
          onChanged();
        }}
      />
    );
  }

  // Same search as the till: name or SKU, anywhere in it, any case.
  const q = search.toLowerCase();
  const all = parts ?? [];
  const active = all.filter((p) => p.isActive);

  // Each chip's own list, before the search.
  const lists: Record<Filter, Part[]> = {
    all: active,
    changed: all.filter((p) => onlyIds !== null && onlyIds.includes(p.id)),
    out: active.filter((p) => p.quantityOnHand <= 0),
    noCost: active.filter(needsCost),
    inactive: all.filter((p) => !p.isActive),
  };

  const visible = lists[filter]
    .filter((p) => p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q))
    // Parts on the shelf first, then retired ones, each by name.
    .sort((a, b) =>
      a.isActive === b.isActive ? a.name.localeCompare(b.name) : a.isActive ? -1 : 1,
    );

  // The chips, as drawn: All, Changed (only from the card), Out, No cost
  // (only for someone who may see cost), and Inactive when there are any.
  const chips: { key: Filter; label: string }[] = [{ key: "all", label: "All" }];
  if (onlyIds !== null) {
    chips.push({ key: "changed", label: "Changed" });
  }
  chips.push({ key: "out", label: "Out" });
  if (all.some((p) => p.buyingPriceShown === true)) {
    chips.push({ key: "noCost", label: "No cost" });
  }
  if (lists.inactive.length > 0) {
    chips.push({ key: "inactive", label: "Inactive" });
  }

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      {/* No back button: Inventory is a bottom tab, so the tabs are the way out. */}
      <div className="shrink-0 bg-primary px-4 pb-[13px] pt-[14px]">
        <h1 className="text-[18px] font-bold leading-6 tracking-[-0.01em] text-primary-foreground">Inventory</h1>
        <p className="mt-0.5 text-[12px] leading-4 text-primary-foreground-muted">
          {parts === null ? "—" : `${active.length.toLocaleString("en-KE")} ${active.length === 1 ? words.singular : words.plural}`}
        </p>
      </div>

      <div className="flex shrink-0 gap-2 px-[14px] pb-[10px] pt-3">
        <input
          type="search"
          aria-label={`Search ${words.plural}`}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search"
          className="h-11 min-w-0 flex-1 rounded-[11px] border-[1.5px] border-border-strong bg-surface px-[13px] text-base
            placeholder:text-muted-foreground focus:border-2 focus:border-primary focus:outline-none"
        />
        <button
          onClick={() => {
            setDoneMessage("");
            setEditing("new");
          }}
          aria-label={`Add ${words.singular}`}
          className="h-11 shrink-0 rounded-[11px] border-2 border-primary bg-surface px-[14px] text-[14px] font-bold text-primary
            active:bg-primary-soft"
        >
          Add
        </button>
      </div>

      {/* One row of chips: scrolls sideways on a phone. */}
      <div className="flex shrink-0 gap-[7px] overflow-x-auto px-[14px] pb-[11px]">
        {chips.map((chip) => {
          const on = chip.key === filter;
          return (
            <button
              key={chip.key}
              type="button"
              onClick={() => setFilter(chip.key)}
              aria-pressed={on}
              className={
                "h-[34px] shrink-0 whitespace-nowrap rounded-full px-3 text-[13px] font-semibold " +
                (on
                  ? "border-2 border-primary bg-primary text-primary-foreground"
                  : chip.key === "changed"
                    ? "border-2 border-warning-strong bg-surface text-warning-strong"
                    : "border-[1.5px] border-border-strong bg-surface text-foreground")
              }
            >
              {chip.label} <span className="font-money">{parts === null ? "" : lists[chip.key].length}</span>
            </button>
          );
        })}
      </div>

      {doneMessage !== "" && (
        <p role="status" className="shrink-0 bg-primary-soft px-3 py-2 text-base font-semibold text-primary">
          {doneMessage}
        </p>
      )}

      <main className="min-h-0 flex-1 overflow-y-auto border-t border-border bg-surface">
        {loadProblem !== "" && (
          <div className="p-3">
            <p className="text-base">{loadProblem}</p>
            <button
              onClick={() => setLoadCount(loadCount + 1)}
              className="mt-2 min-h-11 rounded-lg border-2 border-primary px-3 text-base font-bold text-primary"
            >
              Try again
            </button>
          </div>
        )}

        {visible.map((part) => (
          <button
            key={part.id}
            onClick={() => {
              setDoneMessage("");
              setEditing(part);
            }}
            className={
              "flex w-full items-start gap-3 border-b border-row-line px-4 py-[11px] text-left " +
              (part.isActive ? "bg-surface active:bg-primary-soft" : "bg-background text-muted-foreground")
            }
          >
            <span className="min-w-0 flex-1">
              <span className="block truncate text-[15px] font-bold leading-5">{part.name}</span>
              <span className="mt-[3px] flex flex-wrap items-center gap-x-1 gap-y-0.5 text-[12px] leading-4 text-muted-foreground">
                {!part.isActive && (
                  <span className="rounded-[5px] border border-border-strong px-[7px] py-0.5 text-[11px] font-bold text-foreground">
                    Inactive
                  </span>
                )}
                <span>{part.binLocation ?? "No shelf"} ·</span>
                {part.needsReceiving ? (
                  <span className="rounded-[5px] bg-warning px-[7px] py-0.5 text-[11px] font-bold text-warning-foreground">
                    Needs receiving
                  </span>
                ) : part.quantityOnHand <= 0 ? (
                  <span className="rounded-[5px] bg-warning px-[7px] py-0.5 text-[11px] font-bold text-warning-foreground">
                    Out of stock
                  </span>
                ) : (
                  <span className="font-money">{qty(part.quantityOnHand)} in stock</span>
                )}
              </span>
              {/* The owner's own margin rule, where this part earns less.
                  Here, for whoever sets prices; never in the cart. */}
              {part.marginUnderRulePercent != null && (
                <span className="mt-1 block text-[12px] leading-4 text-warning-strong">
                  Margin under your {part.marginUnderRulePercent}% rule
                </span>
              )}
            </span>
            <span className="shrink-0 text-right">
              <span className="block font-money text-[17px] font-bold leading-6 text-foreground">
                {kes(part.sellPriceMinor)}
              </span>
              {/* What a person last paid, only when the server sent it: it
                  survives selling out, and an imported cost is never it.
                  Hidden from this person: nothing shows at all. */}
              {part.lastBoughtAtMinor != null && part.lastBoughtAtMinor > 0 && (
                <span className="mt-[3px] block text-[11px] leading-4 text-muted-foreground">
                  Last bought at <span className="font-money">{kes(part.lastBoughtAtMinor)}</span>
                </span>
              )}
              {needsCost(part) && (
                <span className="mt-[3px] block text-[11px] font-bold leading-4 text-warning-strong">add cost</span>
              )}
            </span>
          </button>
        ))}

        {parts !== null && visible.length === 0 && (
          <p className="p-4 text-base md:text-sm text-muted-foreground">
            {parts.length === 0 ? "Nothing here yet" : q !== "" ? "No match" : "None here"}
          </p>
        )}

        {/* A tablet has no More screen, so the word is changed here too. */}
        <div className="mt-6 hidden border-t border-border md:block">
          <SellableNounSetting shopId={shopId} words={words} onSaved={onWordChanged} onExpired={onExpired} />
        </div>
      </main>
    </div>
  );
}
