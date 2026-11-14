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
export default function PartsScreen({
  shopId,
  words,
  startAdding = false,
  onBack,
  onChanged,
  onWordChanged,
  onExpired,
}: {
  shopId: string;
  words: ItemWords;
  // Opens straight on the "Add" form: used by the till's empty shelf.
  startAdding?: boolean;
  onBack: () => void;
  onChanged: () => void;
  onWordChanged: () => void;
  onExpired: () => void;
}) {
  const [parts, setParts] = useState<Part[] | null>(null);
  const [loadProblem, setLoadProblem] = useState("");
  const [search, setSearch] = useState("");

  // Switched-off parts are hidden unless asked for.
  const [showInactive, setShowInactive] = useState(false);

  // Bumped to load the list again: after a change, or by "Try again".
  const [loadCount, setLoadCount] = useState(0);

  // Which form is open: a part being edited, "new" for adding, or none.
  const [editing, setEditing] = useState<Part | "new" | null>(startAdding ? "new" : null);

  // What just happened, said once at the top of the list.
  const [doneMessage, setDoneMessage] = useState("");

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
  const visible = (parts ?? [])
    .filter((p) => p.isActive || showInactive)
    .filter((p) => p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q))
    // Parts on the shelf first, then retired ones, each by name.
    .sort((a, b) =>
      a.isActive === b.isActive ? a.name.localeCompare(b.name) : a.isActive ? -1 : 1,
    );

  const inactiveCount = (parts ?? []).filter((p) => !p.isActive).length;
  const activeCount = (parts ?? []).length - inactiveCount;

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold md:hidden"
        >
          ‹ More
        </button>
        <span className="text-base font-bold">{words.Plural}</span>
      </div>

      <div className="shrink-0 space-y-2 border-b border-border bg-surface p-2">
        <button
          onClick={() => {
            setDoneMessage("");
            setEditing("new");
          }}
          className="h-12 w-full rounded-lg bg-primary text-lg font-bold text-primary-foreground"
        >
          Add {words.singular}
        </button>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={`Search ${words.plural}`}
          className="h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
            placeholder:text-muted-foreground focus:border-primary focus:outline-none"
        />
        <p className="flex flex-wrap items-center gap-x-1 px-1 text-sm font-semibold text-muted-foreground">
          {parts === null ? (
            "—"
          ) : (
            <>
              <span>
                {activeCount} {activeCount === 1 ? words.singular : words.plural} ·
              </span>
              {inactiveCount === 0 ? (
                <span>0 inactive</span>
              ) : (
                // The inactive count is the switch: tap to show them, again to hide.
                <button
                  type="button"
                  onClick={() => setShowInactive(!showInactive)}
                  aria-pressed={showInactive}
                  className="min-h-11 px-1 font-bold text-primary underline"
                >
                  {inactiveCount} inactive{showInactive ? " (hide)" : " (show)"}
                </button>
              )}
            </>
          )}
        </p>
      </div>

      {doneMessage !== "" && (
        <p role="status" className="shrink-0 bg-primary-soft px-3 py-2 text-base font-semibold text-primary">
          {doneMessage}
        </p>
      )}

      <main className="min-h-0 flex-1 overflow-y-auto">
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
              "flex min-h-14 w-full items-center justify-between gap-3 border-b border-border px-3 py-2 text-left " +
              (part.isActive ? "bg-surface active:bg-primary-soft" : "bg-background text-muted-foreground")
            }
          >
            <span className="min-w-0">
              <span className="block truncate text-base font-semibold">{part.name}</span>
              <span className="mt-1 flex flex-wrap items-center gap-1.5 text-sm">
                {!part.isActive && (
                  <span className="rounded border border-border-strong px-1.5 py-0.5 font-bold">
                    Inactive
                  </span>
                )}
                <span
                  className={
                    "rounded px-1.5 py-0.5 font-bold " +
                    (part.isActive ? "bg-primary-soft text-primary" : "bg-surface")
                  }
                >
                  {part.binLocation ?? "No shelf"}
                </span>
                <span className="text-muted-foreground">{part.sku}</span>
                {part.needsReceiving ? (
                  <span className="rounded bg-warning px-1.5 py-0.5 font-bold text-warning-foreground">
                    Needs receiving
                  </span>
                ) : (
                  <span className="font-money font-semibold text-muted-foreground">
                    {qty(part.quantityOnHand)} in stock
                  </span>
                )}
              </span>
            </span>
            <span className="shrink-0 font-money text-lg font-bold">{kes(part.sellPriceMinor)}</span>
          </button>
        ))}

        {parts !== null && visible.length === 0 && (
          <p className="p-4 text-base md:text-sm text-muted-foreground">
            {parts.length === 0
              ? "Nothing here yet"
              : activeCount === 0 && !showInactive && q === ""
                ? "None active"
                : "No match"}
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
