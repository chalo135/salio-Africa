import NavIcon, { type NavPlace } from "./NavIcons";
import { type ItemWords } from "./auth";

// The ways parts get into Salio, as large cards: one at a time, a
// spreadsheet, or a supplier's invoice. Shown on the empty shelf and in the
// "+ Add parts" sheet on the Sell screen, so the two always offer the same.
//
// A Cashier gets only the first: importing and invoice reading carry cost
// and stock, which are the Owner's (locked rule 13). The invoice card shows
// only when a real invoice reader is set up, never for the demo one.
export default function AddPartsChoices({
  words,
  isOwner,
  readerReal,
  sideBySide,
  onAddOne,
  onImport,
  onReadInvoice,
}: {
  words: ItemWords;
  isOwner: boolean;
  readerReal: boolean;
  // True on the empty shelf: side by side from 768px. False in the sheet.
  sideBySide: boolean;
  onAddOne: () => void;
  onImport: () => void;
  onReadInvoice: () => void;
}) {
  const choices: { place: NavPlace; title: string; detail: string; onOpen: () => void }[] = [
    {
      place: "parts",
      title: `Add ${words.aSingular}`,
      detail: "One at a time",
      onOpen: onAddOne,
    },
  ];

  if (isOwner) {
    choices.push({
      place: "importParts",
      title: "Import a list",
      detail: "From a spreadsheet",
      onOpen: onImport,
    });
  }

  if (isOwner && readerReal) {
    choices.push({
      place: "readInvoice",
      title: "Scan invoice",
      detail: "From a delivery note",
      onOpen: onReadInvoice,
    });
  }

  return (
    <div className={"flex flex-col gap-3" + (sideBySide ? " md:flex-row" : "")}>
      {choices.map((choice) => (
        <button
          key={choice.place}
          onClick={choice.onOpen}
          className={
            "flex min-h-24 items-center gap-3 rounded-2xl border-2 border-primary bg-surface p-4 text-left active:bg-primary-soft" +
            (sideBySide ? " md:min-h-44 md:max-w-sm md:flex-1 md:flex-col md:items-start md:justify-center" : "")
          }
        >
          <span className="flex h-14 w-14 shrink-0 items-center justify-center rounded-xl bg-primary text-primary-foreground">
            <NavIcon place={choice.place} size={30} />
          </span>
          <span className="min-w-0">
            <span className="block text-xl font-bold text-foreground">{choice.title}</span>
            <span className="block text-base text-muted-foreground">{choice.detail}</span>
          </span>
        </button>
      ))}
    </div>
  );
}
