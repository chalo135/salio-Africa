import { useEffect, useState } from "react";
import { API, kes, qty, type ItemWords } from "./auth";
import { apiFetch } from "./api";

// One part that has sold, or has stock, with no cost known (decision D9).
export type NeedingCost = {
  productId: string;
  name: string;
  sku: string;
  quantitySold: number;
  revenueWithoutCostMinor: number;
  quantityOnHand: number;
};

// Every part sold with no cost behind it. Those sales were booked with no
// cost of sales, so profit is overstated by them. Owner only: it is about
// cost and profit (locked rule 13).
//
// Tapping a part opens Stock in with it already on the delivery. That sets
// the cost from now on. It never touches sales already made.
export default function NeedingCostScreen({
  shopId,
  words,
  onBack,
  onChoose,
  onExpired,
}: {
  shopId: string;
  words: ItemWords;
  onBack: () => void;
  onChoose: (item: NeedingCost) => void;
  onExpired: () => void;
}) {
  const [items, setItems] = useState<NeedingCost[] | null>(null);
  const [problem, setProblem] = useState("");

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const response = await apiFetch(`${API}/api/products/needing-cost?organizationId=${shopId}`, {
          credentials: "include",
        });

        if (response.status === 401) {
          onExpired();
          return;
        }

        if (!response.ok) {
          if (!cancelled) setProblem("List didn't load");
          return;
        }

        const found: NeedingCost[] = await response.json();
        if (!cancelled) setItems(found);
      } catch {
        if (!cancelled) setProblem("Can't reach Salio");
      }
    }

    load();
    return () => {
      cancelled = true;
    };
    // onExpired is App's loginEnded and does not change meaning.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId]);

  let totalMinor = 0;
  for (const item of items ?? []) {
    totalMinor = totalMinor + item.revenueWithoutCostMinor;
  }

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold md:hidden"
        >
          ‹ More
        </button>
        <span className="min-w-0 truncate text-base font-bold">Missing costs</span>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto">
        {problem !== "" && (
          <p role="alert" className="m-3 rounded-lg bg-error px-3 py-2 text-base text-error-foreground">
            {problem}
          </p>
        )}

        {items === null && problem === "" && (
          <p className="p-4 text-base text-muted-foreground md:text-sm">Loading…</p>
        )}

        {items !== null && items.length === 0 && (
          <p className="mx-auto max-w-sm px-4 py-10 text-center text-lg font-semibold">
            All costed
          </p>
        )}

        {items !== null && items.length > 0 && (
          <>
            {/* The profit figure is wrong while this list has anything in it. Say so first. */}
            <div className="space-y-2 border-b border-border bg-surface p-3">
              <p role="status" className="rounded-lg bg-warning px-3 py-2 text-base font-bold text-warning-foreground">
                KES <span className="font-money">{kes(totalMinor)}</span> sold with no cost
              </p>
              <p className="text-base md:text-sm">
                {words.Plural} sold without a cost. Past sales stay as booked.
              </p>
            </div>

            <ul className="bg-surface">
              {items.map((item) => (
                <li key={item.productId} className="border-b border-border">
                  <button
                    onClick={() => onChoose(item)}
                    className="flex min-h-16 w-full items-center gap-3 px-3 py-2 text-left active:bg-primary-soft"
                  >
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-base font-semibold">{item.name}</span>
                      <span className="block text-sm text-muted-foreground">
                        {item.sku} · <span className="font-money">{qty(item.quantitySold)}</span> sold
                      </span>
                    </span>
                    <span className="shrink-0 text-right">
                      <span className="block font-money text-base font-bold">
                        KES {kes(item.revenueWithoutCostMinor)}
                      </span>
                      <span className="block text-sm text-muted-foreground">with no cost</span>
                    </span>
                    <span className="shrink-0 text-2xl text-muted-foreground" aria-hidden="true">
                      ›
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </>
        )}
      </main>
    </div>
  );
}
