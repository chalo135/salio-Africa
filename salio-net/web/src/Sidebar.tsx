// The left column on a tablet. Shown from 768px up; below that the bottom
// tabs and the More list do this job instead, and this is hidden.
//
// Nothing here scrolls on a 1024x768 tablet, nor when Chrome's address bar
// leaves only 650px of height and the Install button is showing. That is
// why the rows are no taller than they are, and why the helper lines under
// Lock and Sign out only show when the screen is 720px tall or more. Top: the three places used all
// day, as big buttons. Then three groups (Shop, Books, Settings), of which
// only one is open at a time, so the longest group still fits. Lock and
// Sign out are pinned at the bottom. What is in the menu comes from menu.ts.

import { useState } from "react";
import NavIcon, { type NavPlace } from "./NavIcons";
import InstallButton from "./InstallButton";
import { groupOf, type Menu, type MenuGroupKey } from "./menu";

export default function Sidebar({
  shopName,
  userName,
  menu,
  active,
  onChoose,
  onLock,
  onSignOut,
}: {
  shopName: string;
  userName: string;
  // Already cut down to what this person may open (see menu.ts).
  menu: Menu;
  active: NavPlace;
  onChoose: (place: NavPlace) => void;
  // Shows the lock screen at once. The login, the shop and the carts stay.
  onLock: () => void;
  // Ends the login on this tablet and clears this person's carts.
  onSignOut: () => void;
}) {
  // Which group is open: one at a time, or none.
  const [openGroup, setOpenGroup] = useState<MenuGroupKey | null>(groupOf(menu, active));

  // The group the person is inside opens by itself: on load (above) and
  // whenever they move to a place in another group. Done while drawing, not
  // in an effect (React's pattern for "change state when a prop changes").
  const [lastActive, setLastActive] = useState(active);
  if (active !== lastActive) {
    setLastActive(active);
    const group = groupOf(menu, active);
    if (group !== null) {
      setOpenGroup(group);
    }
  }

  return (
    // The shop's green, with white words: primary-foreground on primary is
    // 6.9:1. The active item turns white with green words, the same pair
    // the other way round, so it stands out without a new colour.
    <nav className="hidden w-60 shrink-0 flex-col bg-primary text-primary-foreground md:flex">
      {/* Which shop, and who is signed in. */}
      <div className="shrink-0 border-b border-primary-foreground/25 px-3 py-1">
        <p className="truncate text-base font-bold">{shopName}</p>
        <p className="truncate text-sm">{userName}</p>
      </div>

      {/* Inventory, Sell, Owed: side by side, the biggest things to tap. */}
      <div className="flex shrink-0 gap-1.5 p-1.5">
        {menu.top.map((entry) => {
          const isActive = entry.place === active;

          return (
            <button
              key={entry.place}
              onClick={() => onChoose(entry.place)}
              aria-current={isActive ? "page" : undefined}
              className={
                // text-sm and no wrap: "Inventory" must fit a third of 240px.
                "flex h-14 min-w-0 flex-1 flex-col items-center justify-center whitespace-nowrap rounded-xl text-sm font-bold leading-tight " +
                (isActive
                  ? "bg-primary-foreground text-primary"
                  : "bg-primary-fill text-primary-foreground active:bg-primary-foreground/25")
              }
            >
              <NavIcon place={entry.place} size={24} />
              {entry.label}
            </button>
          );
        })}
      </div>

      {/* Today: first after the top, as it is first on the phone's More. */}
      <ul className="shrink-0">
        {menu.firstInMore.map((entry) => {
          const isActive = entry.place === active;

          return (
            <li key={entry.place}>
              <button
                onClick={() => onChoose(entry.place)}
                aria-current={isActive ? "page" : undefined}
                className={
                  "flex min-h-10 w-full items-center gap-2.5 border-l-4 pl-2 pr-2 text-left text-base " +
                  (isActive
                    ? "border-primary-soft bg-primary-foreground font-bold text-primary"
                    : "border-transparent font-semibold text-primary-foreground active:bg-primary-foreground/15")
                }
              >
                <NavIcon place={entry.place} size={22} />
                <span className="min-w-0 flex-1 py-1 leading-tight">{entry.label}</span>
              </button>
            </li>
          );
        })}
      </ul>

      {/* The groups. This is the only part that can scroll, and only on a
          screen shorter than a tablet's: it is better than cutting rows off. */}
      <div className="min-h-0 flex-1 overflow-y-auto">
        {menu.groups.map((group) => {
          const isOpen = group.key === openGroup;

          // While the group is shut, its badges are added up on the heading.
          let hiddenBadge = 0;
          for (const entry of group.entries) {
            hiddenBadge = hiddenBadge + (entry.badge ?? 0);
          }

          return (
            <div key={group.key} className="border-t border-primary-foreground/25">
              <button
                onClick={() => setOpenGroup(isOpen ? null : group.key)}
                aria-expanded={isOpen}
                className="flex min-h-10 w-full items-center gap-2 px-3 text-left text-sm font-bold
                  tracking-wide text-primary-foreground-soft active:bg-primary-foreground/15"
              >
                <span className="min-w-0 flex-1">{group.title}</span>
                {!isOpen && hiddenBadge > 0 && (
                  <span className="shrink-0 rounded-full bg-warning px-2 font-money text-sm font-bold text-warning-foreground">
                    {hiddenBadge}
                  </span>
                )}
                <span className="w-4 shrink-0 text-center text-lg leading-none" aria-hidden="true">
                  {isOpen ? "−" : "+"}
                </span>
              </button>

              {isOpen && (
                <ul>
                  {group.entries.map((entry) => {
                    const isActive = entry.place === active;

                    return (
                      <li key={entry.place}>
                        <button
                          onClick={() => onChoose(entry.place)}
                          aria-current={isActive ? "page" : undefined}
                          className={
                            "flex min-h-10 w-full items-center gap-2.5 border-l-4 pl-2 pr-2 text-left text-base " +
                            (isActive
                              ? "border-primary-soft bg-primary-foreground font-bold text-primary"
                              : "border-transparent font-semibold text-primary-foreground active:bg-primary-foreground/15")
                          }
                        >
                          <NavIcon place={entry.place} size={22} />
                          <span className="min-w-0 flex-1 py-1 leading-tight">{entry.label}</span>
                          {entry.badge !== undefined && entry.badge > 0 && (
                            <span className="shrink-0 rounded-full bg-warning px-2 font-money text-sm font-bold text-warning-foreground">
                              {entry.badge}
                            </span>
                          )}
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          );
        })}
      </div>

      {/* Apart from the list on purpose: nobody should sign out by reaching
          for People. Lock and Sign out look different and are kept apart by
          a line, because they do very different things: Lock is for
          stepping out to the bank; Sign out ends the login. */}
      <div className="shrink-0 border-t-2 border-primary-foreground/25 px-2 py-1.5">
        <InstallButton tone="sidebar" />
        <button
          onClick={onLock}
          className="flex min-h-10 w-full flex-col items-center justify-center rounded-xl
            bg-primary-foreground px-2 py-1 text-primary"
        >
          <span className="text-base font-bold leading-tight">Lock</span>
          <span className="hidden text-sm leading-tight [@media(min-height:720px)]:block">
            Your cart is kept
          </span>
        </button>

        <button
          onClick={onSignOut}
          className="mt-2 flex min-h-10 w-full flex-col items-center justify-center rounded-xl
            border-2 border-primary-foreground px-2 py-1 text-primary-foreground"
        >
          <span className="text-base font-bold leading-tight">Sign out</span>
          <span className="hidden text-sm leading-tight [@media(min-height:720px)]:block">
            Clears the cart too
          </span>
        </button>
      </div>
    </nav>
  );
}
