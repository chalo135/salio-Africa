// The one list of places in the till. The tablet sidebar, the phone's More
// screen and the phone's bottom tabs all read it from here, so the three can
// never disagree about what exists, what it is called, or who may open it.
//
// Three places are done all day and sit on their own at the top: Sell,
// Today, Owed. Everything else is in three groups: Shop, Books, Settings.

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
  const top: MenuEntry[] = [
    { place: "sell", label: "Sell", detail: "" },
    { place: "today", label: "Today", detail: "" },
  ];

  // Money, stock and people are the Owner's (locked rule 13). A Cashier
  // sells and sees today's sales: no Owed, and no groups.
  if (!isOwner) {
    return { top, groups: [] };
  }

  top.push({ place: "owed", label: "Owed", detail: "" });

  const shopEntries: MenuEntry[] = [
    { place: "parts", label: words.Plural, detail: "" },
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

  return { top, groups };
}

// Which group a place is in, or null for Sell, Today, Owed and More.
export function groupOf(menu: Menu, place: NavPlace): MenuGroupKey | null {
  for (const group of menu.groups) {
    if (group.entries.some((entry) => entry.place === place)) {
      return group.key;
    }
  }
  return null;
}
