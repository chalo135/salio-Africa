import { useEffect, useState } from "react";
import { API, type ItemWords } from "./auth";
import { undoImport, type UndoResult } from "./importApi";
import { UndoConfirm, UndoResultView } from "./ImportUndo";
import { apiFetch } from "./api";

type Batch = {
  batchId: string;
  fileName: string;
  uploadedBy: string;
  startedAt: string;
  completedAt?: string | null;
  rowsCreated: number;
  rowsUpdated: number;
  undoneAt?: string | null;
  undoneBy?: string | null;
};

// "9 Nov 2026, 14:05", as Kenya writes it.
function when(iso: string) {
  return new Date(iso).toLocaleString("en-KE", {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

// Every import into this shop, newest first, each with its Undo. Owner only.
export default function ImportHistoryScreen({
  shopId,
  words,
  onBack,
  onImport,
  onUndone,
  onExpired,
}: {
  shopId: string;
  words: ItemWords;
  onBack: () => void;
  onImport: () => void;
  onUndone: () => void; // reloads the shelf
  onExpired: () => void;
}) {
  const [batches, setBatches] = useState<Batch[] | null>(null);
  const [problem, setProblem] = useState("");
  const [loadCount, setLoadCount] = useState(0);

  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [undoingId, setUndoingId] = useState<string | null>(null);
  const [results, setResults] = useState<Record<string, UndoResult>>({});
  const [rowProblem, setRowProblem] = useState<{ id: string; text: string } | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const response = await apiFetch(`${API}/api/imports?organizationId=${shopId}`, { credentials: "include" });

        if (response.status === 401) {
          onExpired();
          return;
        }

        if (!response.ok) {
          if (!cancelled) setProblem("Couldn't load past imports");
          return;
        }

        const found: Batch[] = await response.json();
        if (!cancelled) setBatches(found);
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
  }, [shopId, loadCount]);

  async function undo(batchId: string) {
    setUndoingId(batchId);
    setRowProblem(null);
    const outcome = await undoImport(shopId, batchId);
    setUndoingId(null);
    setConfirmingId(null);

    if (outcome.expired) {
      onExpired();
      return;
    }
    if (outcome.problem) {
      setRowProblem({ id: batchId, text: outcome.problem });
      return;
    }
    if (outcome.result) {
      setResults({ ...results, [batchId]: outcome.result });
      onUndone();
      // The row now says who undid it and when.
      setLoadCount(loadCount + 1);
    }
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
        <span className="min-w-0 flex-1 truncate text-base font-bold">Past imports</span>
        <button onClick={onImport} className="min-h-11 shrink-0 px-2 text-sm font-bold text-primary underline">
          Import
        </button>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto">
        {problem !== "" && (
          <p role="alert" className="m-3 rounded-lg bg-error px-3 py-2 text-base text-error-foreground">
            {problem}
          </p>
        )}

        {batches === null && problem === "" && (
          <p className="p-4 text-base text-muted-foreground md:text-sm">Loading…</p>
        )}

        {batches !== null && batches.length === 0 && (
          <p className="mx-auto max-w-sm px-4 py-10 text-center text-lg font-semibold">No imports yet.</p>
        )}

        {batches !== null && batches.length > 0 && (
          <ul className="bg-surface">
            {batches.map((batch) => (
              <li key={batch.batchId} className="space-y-2 border-b border-border px-3 py-3">
                <div className="flex items-start gap-3">
                  <div className="min-w-0 flex-1">
                    <p className="break-all text-base font-bold">{batch.fileName}</p>
                    <p className="text-sm text-muted-foreground">
                      {when(batch.startedAt)} · {batch.uploadedBy}
                    </p>
                    <p className="text-base">
                      <span className="font-money">{batch.rowsCreated.toLocaleString("en-KE")}</span> new,{" "}
                      <span className="font-money">{batch.rowsUpdated.toLocaleString("en-KE")}</span> updated
                      {!batch.completedAt && !batch.undoneAt && (
                        <span className="text-muted-foreground"> · did not finish</span>
                      )}
                    </p>
                  </div>
                  {batch.undoneAt ? (
                    <p className="w-32 shrink-0 text-right text-sm font-semibold text-muted-foreground">
                      Undone {when(batch.undoneAt)}
                      {batch.undoneBy ? ` by ${batch.undoneBy}` : ""}
                    </p>
                  ) : (
                    confirmingId !== batch.batchId && (
                      <button
                        onClick={() => {
                          setConfirmingId(batch.batchId);
                          setRowProblem(null);
                        }}
                        className="min-h-11 shrink-0 rounded-lg border-2 border-border-strong px-3 text-base font-bold"
                      >
                        Undo
                      </button>
                    )
                  )}
                </div>

                {confirmingId === batch.batchId && !batch.undoneAt && (
                  <UndoConfirm
                    busy={undoingId === batch.batchId}
                    onYes={() => undo(batch.batchId)}
                    onNo={() => setConfirmingId(null)}
                  />
                )}

                {rowProblem !== null && rowProblem.id === batch.batchId && (
                  <p role="alert" className="rounded-lg bg-error px-3 py-2 text-base text-error-foreground">
                    {rowProblem.text}
                  </p>
                )}

                {results[batch.batchId] && <UndoResultView result={results[batch.batchId]} words={words} />}
              </li>
            ))}
          </ul>
        )}
      </main>
    </div>
  );
}
