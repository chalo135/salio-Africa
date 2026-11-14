// The pictures beside each place in the till: the bottom tabs, the More
// list and the tablet sidebar all draw them from here, so the three can
// never show different pictures for the same place. Every one of them is
// always shown with its word: nothing is a bare picture.

import type { ReactNode } from "react";

export type NavPlace =
  | "sell"
  | "today"
  | "owed"
  | "more"
  | "stockIn"
  | "openingStock"
  | "moneyOut"
  | "month"
  | "tax"
  | "parts"
  | "needingCost"
  | "importParts"
  | "readInvoice"
  | "people"
  | "shopWord";

// Drawn with lines in the text colour, so an active item's icon turns the
// same colour as its word.
const PATHS: Record<NavPlace, ReactNode> = {
  // A basket.
  sell: (
    <>
      <path d="M3 9h18l-2 11H5L3 9z" />
      <path d="M8 9l4-6 4 6" />
    </>
  ),
  // A receipt.
  today: (
    <>
      <path d="M6 3h12v18l-3-2-3 2-3-2-3 2V3z" />
      <path d="M9 8h6M9 12h6" />
    </>
  ),
  // An account book.
  owed: (
    <>
      <path d="M5 4h11a3 3 0 0 1 3 3v13H8a3 3 0 0 1-3-3V4z" />
      <path d="M5 17a3 3 0 0 1 3-3h11" />
    </>
  ),
  // Three lines: a list.
  more: <path d="M4 6h16M4 12h16M4 18h16" />,
  // A box.
  stockIn: (
    <>
      <path d="M3 7l9-4 9 4v10l-9 4-9-4V7z" />
      <path d="M3 7l9 4 9-4M12 11v10" />
    </>
  ),
  // A shelf with two boxes on it.
  openingStock: (
    <>
      <path d="M3 21V3M21 21V3M3 13h18M3 21h18" />
      <rect x="6" y="7" width="5" height="6" />
      <rect x="13" y="9" width="5" height="4" />
    </>
  ),
  // A note.
  moneyOut: (
    <>
      <rect x="3" y="6" width="18" height="12" rx="2" />
      <circle cx="12" cy="12" r="2.5" />
    </>
  ),
  // A calendar.
  month: (
    <>
      <rect x="4" y="5" width="16" height="16" rx="2" />
      <path d="M4 10h16M9 3v4M15 3v4" />
    </>
  ),
  // A sheet of paper with lines: the return to file.
  tax: (
    <>
      <path d="M6 3h9l4 4v14H6z" />
      <path d="M9 11h7M9 15h7" />
    </>
  ),
  // A price tag.
  parts: (
    <>
      <path d="M20 13l-7 7-9-9V4h7l9 9z" />
      <circle cx="8" cy="8" r="1.5" />
    </>
  ),
  // A price tag with a question mark: no cost known yet.
  needingCost: (
    <>
      <path d="M20 13l-7 7-9-9V4h7l9 9z" />
      <path d="M10 9.5a2 2 0 1 1 2.6 1.9c-.5.2-.8.6-.8 1.1v.5M11.8 15.4v.1" />
    </>
  ),
  // A sheet with an arrow coming into it: a file brought in.
  importParts: (
    <>
      <path d="M14 3H6v18h12V7l-4-4zM14 3v4h4" />
      <path d="M12 10v7M9 14l3 3 3-3" />
    </>
  ),
  // A camera.
  readInvoice: (
    <>
      <path d="M3 8h4l2-3h6l2 3h4v11H3V8z" />
      <circle cx="12" cy="13" r="3.5" />
    </>
  ),
  // Two people.
  people: (
    <>
      <circle cx="9" cy="8" r="3.5" />
      <path d="M3 20a6 6 0 0 1 12 0M16 4.5a3.5 3.5 0 0 1 0 7M21 20a6 6 0 0 0-4-5.6" />
    </>
  ),
  // A pencil: the word the shop chose.
  shopWord: (
    <>
      <path d="M4 20l1-5L16 4l4 4L9 19l-5 1z" />
      <path d="M14 6l4 4" />
    </>
  ),
};

export default function NavIcon({ place, size }: { place: NavPlace; size: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {PATHS[place]}
    </svg>
  );
}
