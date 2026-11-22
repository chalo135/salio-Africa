// The tabs along the bottom of the till. Every tab has a word under its
// icon: nothing is a bare picture.

import NavIcon from "./NavIcons";
import { type MenuEntry } from "./menu";

// "today" is no longer a tab (it is first on More) but stays a value: the
// Today screen still shows the tab bar, with More lit, where it lives.
export type Tab = "parts" | "sell" | "today" | "owed" | "more";

export default function BottomNav({
  active,
  top,
  moreBadge,
  onChoose,
}: {
  active: Tab;
  // Inventory, Sell, Owed for an Owner, in that order, from menu.ts: the
  // same ones the tablet sidebar puts at its top. A Cashier gets Sell only,
  // not tabs they can never open (locked rule 13).
  top: MenuEntry[];
  // A number on More: the parts there that need a cost. Hidden at 0.
  moreBadge: number;
  onChoose: (tab: Tab) => void;
}) {
  const tabs: { tab: Tab; label: string }[] = [];

  for (const entry of top) {
    if (entry.place === "parts" || entry.place === "sell" || entry.place === "owed") {
      tabs.push({ tab: entry.place, label: entry.label });
    }
  }

  // Everything in the groups (Shop, Books, Settings), and Lock and Sign out.
  tabs.push({ tab: "more", label: "More" });

  return (
    // As drawn (design/FindTap): white, a line above, 21px icons with a
    // small word under each. Then room for the phone's own home bar.
    <nav aria-label="Main" className="shrink-0 border-t border-border bg-surface px-1 pb-[max(0.875rem,env(safe-area-inset-bottom))] pt-[9px]">
      <div className="flex">
        {tabs.map((t) => {
          const isActive = t.tab === active || (t.tab === "more" && active === "today");

          return (
            <button
              key={t.tab}
              onClick={() => onChoose(t.tab)}
              aria-current={isActive ? "page" : undefined}
              className={
                "flex min-h-11 min-w-0 flex-1 flex-col items-center justify-center gap-[3px] whitespace-nowrap text-[12px] leading-4 " +
                (isActive ? "font-bold text-primary" : "font-semibold text-muted-foreground")
              }
            >
              <span className="relative">
                <NavIcon place={t.tab} size={21} />
                {t.tab === "more" && moreBadge > 0 && (
                  <span className="absolute -right-3 -top-1.5 min-w-5 rounded-full bg-warning px-1 text-center font-money text-xs font-bold leading-5 text-warning-foreground">
                    {moreBadge}
                  </span>
                )}
              </span>
              {t.label}
            </button>
          );
        })}
      </div>
    </nav>
  );
}
