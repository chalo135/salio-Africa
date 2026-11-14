// Everything that is not selling, today's sales or who owes what, in the
// same three groups as the tablet sidebar: Shop, Books, Settings (menu.ts).
// Then Lock and Sign out on their own below, well away from the rows people
// tap every day.

import NavIcon, { type NavPlace } from "./NavIcons";
import { type ItemWords } from "./auth";
import InstallButton from "./InstallButton";
import { type Menu } from "./menu";

export default function MoreScreen({
  shopName,
  userName,
  menu,
  onChoose,
  onLock,
  onSignOut,
}: {
  shopName: string;
  userName: string;
  words: ItemWords;
  // Already cut down to what this person may open. A Cashier has no groups.
  menu: Menu;
  onChoose: (place: NavPlace) => void;
  // Shows the lock screen at once. The login, the shop and the carts stay.
  onLock: () => void;
  // Ends the login on this phone and clears this person's carts.
  onSignOut: () => void;
}) {
  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="shrink-0 border-b border-border bg-surface px-3 py-2">
        <p className="truncate text-sm font-semibold">
          {shopName} · {userName}
        </p>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto">
        {menu.groups.map((group) => (
          <section key={group.key}>
            <h2 className="px-3 pb-1 pt-4 text-sm font-bold tracking-wide text-muted-foreground">
              {group.title}
            </h2>
            <ul className="border-y border-border bg-surface">
              {group.entries.map((entry) => (
                <li key={entry.place} className="border-t border-border first:border-t-0">
                  <button
                    onClick={() => onChoose(entry.place)}
                    className="flex min-h-16 w-full items-center gap-3 px-3 py-2 text-left active:bg-primary-soft"
                  >
                    <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg bg-primary-soft text-primary">
                      <NavIcon place={entry.place} size={24} />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block text-base font-bold text-foreground">{entry.label}</span>
                      <span className="block text-sm text-muted-foreground">{entry.detail}</span>
                    </span>
                    {entry.badge !== undefined && entry.badge > 0 && (
                      <span className="shrink-0 rounded-full bg-warning px-2 font-money text-base font-bold text-warning-foreground">
                        {entry.badge}
                      </span>
                    )}
                    <span className="shrink-0 text-2xl text-muted-foreground" aria-hidden="true">
                      ›
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        ))}

        {menu.groups.length === 0 && (
          <p className="px-3 py-4 text-base md:text-sm text-muted-foreground">
            Shop and books: owner only.
          </p>
        )}

        {/* Apart from the list on purpose: nobody should sign out by
            reaching for Stock in. Lock and Sign out look different and are
            kept apart by a line: Lock is for stepping away; Sign out ends
            the login. */}
        <div className="px-3 pb-6 pt-10">
          <InstallButton tone="page" />
          <button
            onClick={onLock}
            className="flex min-h-16 w-full flex-col items-center justify-center rounded-xl
              bg-primary px-3 py-2 text-primary-foreground"
          >
            <span className="text-xl font-bold">Lock</span>
            <span className="text-sm">Your cart is kept</span>
          </button>

          <div className="my-6 border-t border-border" />

          <button
            onClick={onSignOut}
            className="flex min-h-16 w-full flex-col items-center justify-center rounded-xl
              border-2 border-border-strong bg-surface px-3 py-2"
          >
            <span className="text-xl font-bold">Sign out</span>
            <span className="text-sm text-muted-foreground">
              Clears the cart too
            </span>
          </button>
        </div>
      </div>
    </div>
  );
}
