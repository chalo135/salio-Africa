// The tabs along the bottom of the till. Every tab has a word under its
// icon: nothing is a bare picture.

import NavIcon from "./NavIcons";
import { type MenuEntry } from "./menu";

export type Tab = "sell" | "today" | "owed" | "more";

export default function BottomNav({
  active,
  top,
  moreBadge,
  onChoose,
}: {
  active: Tab;
  // Sell, Today and (for an Owner) Owed, from menu.ts: the same three the
  // tablet sidebar puts at its top. A Cashier gets no Owed tab, not one
  // they can never open (locked rule 13).
  top: MenuEntry[];
  // A number on More: the parts there that need a cost. Hidden at 0.
  moreBadge: number;
  onChoose: (tab: Tab) => void;
}) {
  const tabs: { tab: Tab; label: string }[] = [];

  for (const entry of top) {
    if (entry.place === "sell" || entry.place === "today" || entry.place === "owed") {
      tabs.push({ tab: entry.place, label: entry.label });
    }
  }

  // Everything in the groups (Shop, Books, Settings), and Lock and Sign out.
  tabs.push({ tab: "more", label: "More" });

  return (
    // 64px of tabs, then 20px of nothing for the phone's own home bar.
    <nav className="shrink-0 border-t border-border bg-surface pb-[max(1.25rem,env(safe-area-inset-bottom))]">
      <div className="flex">
        {tabs.map((t) => {
          const isActive = t.tab === active;

          return (
            <button
              key={t.tab}
              onClick={() => onChoose(t.tab)}
              aria-current={isActive ? "page" : undefined}
              className={
                "flex h-16 flex-1 flex-col items-center justify-center gap-0.5 text-base " +
                (isActive ? "font-bold text-primary" : "font-semibold text-muted-foreground")
              }
            >
              <span className="relative">
                <NavIcon place={t.tab} size={26} />
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
