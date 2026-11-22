// The one list of places in the till. The tablet sidebar, the phone's More
// screen and the phone's bottom tabs all read it from here, so the three can
// never disagree about what exists, what it is called, or who may open it.
//
// The places used all day sit on their own at the top, in the bottom-tab
// order: Inventory, Sell, Owed (Inventory and Owed for an Owner only).
// Today comes first on More, and straight under the top on a tablet.
// Everything else is in three groups: Shop, Books, Settings.
//
// This is only the order of the buttons. The screen the app opens on is set
// in App.tsx ("till"), never taken from this list.

import { type NavPlace } from "./NavIcons";
import { type ItemWords } from "./auth";

export type MenuEntry = {
  place: NavPlace;
  label: string;
  // The second line on the phone's More screen.
  detail: string;
  // A count shown beside the label when above 0.
  badge?: number;
};

export type MenuGroupKey = "shop" | "books" | "settings";

export type MenuGroup = {
  key: MenuGroupKey;
  title: string;
  entries: MenuEntry[];
};

export type Menu = {
  top: MenuEntry[];
  // First on the More screen, above the groups: Today.
  firstInMore: MenuEntry[];
  // A group with nothing in it for this person is not in the list at all.
  groups: MenuGroup[];
};

export function buildMenu({
  isOwner,
  words,
  businessKind,
  showInvoiceReader,
  needingCostCount,
}: {
  isOwner: boolean;
  words: ItemWords;
  // "Sales" or "Services".
  businessKind: string;
  // True only when a real (not demo) invoice reader is set up. See App.tsx.
  showInvoiceReader: boolean;
  needingCostCount: number;
}): Menu {
  const firstInMore: MenuEntry[] = [{ place: "today", label: "Today", detail: "" }];

  // Money, stock and people are the Owner's (locked rule 13). A Cashier
  // sells and sees today's sales: no Inventory, no Owed, and no groups.
  if (!isOwner) {
    return { top: [{ place: "sell", label: "Sell", detail: "" }], firstInMore, groups: [] };
  }

  const top: MenuEntry[] = [
    { place: "parts", label: "Inventory", detail: "" },
    { place: "sell", label: "Sell", detail: "" },
    { place: "owed", label: "Owed", detail: "" },
  ];

  const shopEntries: MenuEntry[] = [
    { place: "stockIn", label: "Stock in", detail: "" },
    { place: "openingStock", label: "Opening stock", detail: "" },
    { place: "importParts", label: "Import", detail: "" },
    {
      place: "needingCost",
      label: "Missing costs",
      detail: "",
      badge: needingCostCount,
    },
  ];

  if (showInvoiceReader) {
    shopEntries.push({
      place: "readInvoice",
      label: "Scan invoice",
      detail: "",
    });
  }

  const shop: MenuGroup = { key: "shop", title: "Shop", entries: shopEntries };

  const books: MenuGroup = {
    key: "books",
    title: "Books",
    entries: [
      { place: "month", label: "Month end", detail: "" },
      { place: "tax", label: "Tax", detail: "" },
      { place: "moneyOut", label: "Money out", detail: "" },
    ],
  };

  const settings: MenuGroup = {
    key: "settings",
    title: "Settings",
    entries: [
      { place: "people", label: "People", detail: "" },
      { place: "shopWord", label: "Wording", detail: `Now: ${words.Plural}` },
    ],
  };

  // Services: selling first, stock after (decision D14). Nothing is hidden,
  // the Shop group only moves last.
  const groups = businessKind === "Services" ? [books, settings, shop] : [shop, books, settings];

  return { top, firstInMore, groups };
}

// Which group a place is in, or null for Inventory, Sell, Today, Owed and More.
export function groupOf(menu: Menu, place: NavPlace): MenuGroupKey | null {
  for (const group of menu.groups) {
    if (group.entries.some((entry) => entry.place === place)) {
      return group.key;
    }
  }
  return null;
}
