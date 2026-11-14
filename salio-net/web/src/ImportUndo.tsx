import { type ItemWords } from "./auth";
import { counted, type UndoResult } from "./importApi";

// What undo did, plainly, with every part it kept and why.
export function UndoResultView({ result, words }: { result: UndoResult; words: ItemWords }) {
  return (
    <div role="status" className="space-y-2 rounded-lg bg-primary-soft px-3 py-2 text-primary">
      <p className="text-base font-bold">
        Undone: {counted(result.undoneCreated, `new ${words.singular} switched off`, `new ${words.plural} switched off`)},{" "}
        {counted(result.undoneUpdated, "change put back", "changes put back")}.
      </p>
      {result.kept.length > 0 && (
        <div className="text-foreground">
          <p className="text-base font-semibold">
            Kept as they are ({result.kept.length}):
          </p>
          <ul className="mt-1 space-y-1">
            {result.kept.map((item) => (
              <li key={item.productId} className="rounded border border-border bg-surface px-2 py-1 text-base">
                <span className="font-semibold">{item.name}</span>{" "}
                <span className="text-sm text-muted-foreground">{item.sku}</span>
                <span className="block text-sm">{item.reason}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

// Asked before undoing. Two buttons, the safe one first.
export function UndoConfirm({
  busy,
  onYes,
  onNo,
}: {
  busy: boolean;
  onYes: () => void;
  onNo: () => void;
}) {
  return (
    <div className="space-y-2 rounded-lg border-2 border-warning-strong bg-warning px-3 py-2 text-warning-foreground">
      <p className="text-base font-bold">Undo import?</p>
      <p className="text-base">
        Some changes may be kept.
      </p>
      <div className="flex gap-2">
        <button
          onClick={onNo}
          disabled={busy}
          className="min-h-11 flex-1 rounded-lg border-2 border-border-strong bg-surface px-3 text-base font-bold text-foreground"
        >
          No, keep it
        </button>
        <button
          onClick={onYes}
          disabled={busy}
          className="min-h-11 flex-1 rounded-lg bg-primary px-3 text-base font-bold text-primary-foreground disabled:opacity-40"
        >
          {busy ? "Undoing…" : "Yes, undo it"}
        </button>
      </div>
    </div>
  );
}

