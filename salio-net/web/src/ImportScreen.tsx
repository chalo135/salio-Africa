import { useRef, useState } from "react";
import { API, type ItemWords } from "./auth";
import { counted, refusalText, undoImport, type UndoResult } from "./importApi";
import { UndoConfirm, UndoResultView } from "./ImportUndo";
import PaidWithChoice from "./PaidWithChoice";
import { apiFetch } from "./api";

// One thing wrong (or worth a look) in the file. Row is the spreadsheet's own
// row number, header = row 1. No row means the whole file.
type ImportIssue = {
  row?: number | null;
  column?: string | null;
  value?: string | null;
  message: string;
};

type Preview = {
  fileName: string;
  rowsToCreate: number;
  rowsToUpdate: number;
  // Not in the agreed contract, but the server sends it: rows already the same.
  rowsUnchanged?: number;
  errors: ImportIssue[];
  warnings: ImportIssue[];
  // Whether any row carries a quantity: only then is the stock question asked.
  hasQuantities?: boolean;
  // Whether this shop has ever recorded a sale: picks the likely answer.
  shopHasSales?: boolean;
};

// Where the stock in the file came from. Opening stock was the owner's
// before the books opened (Owner's Equity); bought stock was paid for or is
// owed (cash, M-Pesa or a payable). The server posts each its own way.
type StockBasis = "OpeningStock" | "Purchase";

type Imported = { batchId: string; rowsCreated: number; rowsUpdated: number };

// The most the server takes. Checked here first so a big file is refused
// before it is sent over a slow connection.
const MAX_BYTES = 5 * 1024 * 1024;

// One problem or warning: where it is, what the cell holds, what is wrong.
function IssueRow({ issue }: { issue: ImportIssue }) {
  const where =
    issue.row === undefined || issue.row === null
      ? "Whole file"
      : `Row ${issue.row}` + (issue.column ? ` · ${issue.column}` : "");
  const hasCell = issue.row !== undefined && issue.row !== null && issue.column;
  const cell = issue.value === undefined || issue.value === null || issue.value === "" ? "(empty)" : issue.value;

  return (
    <li className="border-t border-border px-3 py-2 first:border-t-0">
      <span className="block text-sm font-bold">
        {where}
        {hasCell && (
          <>
            {" · "}
            <span className="break-all rounded border border-border-strong bg-background px-1 font-mono font-normal">{cell}</span>
          </>
        )}
      </span>
      <span className="block text-base">{issue.message}</span>
    </li>
  );
}

// Bring a spreadsheet of parts in. Owner only: a file can carry cost and
// opening stock (locked rule 13).
//
// 1. Download the shop's own parts as a CSV and add to it in Excel.
// 2. Choose the file.  3. See what it would do: nothing is written.
// 4. Import it, with Undo right there.
export default function ImportScreen({
  shopId,
  shopName,
  words,
  shelfEmpty,
  onBack,
  onHistory,
  onImported,
  onExpired,
}: {
  shopId: string;
  shopName: string;
  words: ItemWords;
  // True when the shop has no parts yet: the download is then an empty file
  // with the right columns, and step 1 says so.
  shelfEmpty: boolean;
  onBack: () => void;
  onHistory: () => void;
  onImported: () => void; // reloads the shelf
  onExpired: () => void;
}) {
  // The chosen file is kept: Import sends this same file again, and the
  // server checks it again. It never trusts the preview.
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [imported, setImported] = useState<Imported | null>(null);
  const [undoResult, setUndoResult] = useState<UndoResult | null>(null);

  const [busy, setBusy] = useState("");
  const [problem, setProblem] = useState("");
  const [dragging, setDragging] = useState(false);
  const [confirmingUndo, setConfirmingUndo] = useState(false);

  // Asked only when the file carries quantities. The likely answer is
  // chosen for them, but both are on screen. Paid with has no default.
  const [stockBasis, setStockBasis] = useState<StockBasis | null>(null);
  const [paidWith, setPaidWith] = useState<number | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  async function download() {
    setProblem("");
    setBusy("download");

    try {
      const response = await apiFetch(`${API}/api/imports/products.csv?organizationId=${shopId}`, {
        credentials: "include",
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        setProblem(await refusalText(response, "Couldn't make the file"));
        return;
      }

      // The server names the file. Another address cannot read that name
      // unless the server allows it, so make the same name here if needed.
      const disposition = response.headers.get("Content-Disposition") ?? "";
      const named = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
      const today = new Date().toLocaleDateString("en-CA");
      const fileName = named ? decodeURIComponent(named[1]) : `products-${shopName}-${today}.csv`;

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 10000);
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      setBusy("");
    }
  }

  async function choose(chosen: File) {
    setProblem("");
    setPreview(null);
    setImported(null);
    setUndoResult(null);
    setStockBasis(null);
    setPaidWith(null);

    const name = chosen.name.toLowerCase();
    if (!name.endsWith(".csv") && !name.endsWith(".xlsx")) {
      setProblem("Use a .csv or .xlsx file");
      return;
    }

    if (chosen.size > MAX_BYTES) {
      const mb = (chosen.size / 1024 / 1024).toFixed(1);
      setProblem(`File is ${mb} MB. Limit is 5 MB.`);
      return;
    }

    setFile(chosen);
    setBusy("preview");

    const form = new FormData();
    form.append("file", chosen);

    try {
      const response = await apiFetch(`${API}/api/imports/preview?organizationId=${shopId}`, {
        method: "POST",
        credentials: "include",
        body: form,
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        setProblem(await refusalText(response, "Can't read this file"));
        return;
      }

      const found: Preview = await response.json();
      setPreview(found);
      if (found.hasQuantities === true) {
        setStockBasis(found.shopHasSales === true ? "Purchase" : "OpeningStock");
      }
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      setBusy("");
    }
  }

  async function commit() {
    if (file === null || busy !== "") {
      return;
    }

    setProblem("");
    setBusy("commit");

    const form = new FormData();
    form.append("file", file);
    // Only when the file carries quantities; paid with only for bought stock.
    if (asksStockBasis && stockBasis !== null) {
      form.append("stockBasis", stockBasis);
      if (stockBasis === "Purchase" && paidWith !== null) {
        form.append("paidWith", String(paidWith));
      }
    }

    try {
      const response = await apiFetch(`${API}/api/imports/commit?organizationId=${shopId}`, {
        method: "POST",
        credentials: "include",
        body: form,
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        // The file was checked again and something is now wrong: show the
        // problems the same way as the preview did.
        let detail = "Nothing was imported.";
        try {
          const refused = await response.json();
          if (typeof refused.detail === "string" && refused.detail !== "") detail = refused.detail;
          if (Array.isArray(refused.errors) && preview !== null) {
            setPreview({ ...preview, errors: refused.errors });
          }
        } catch {
          // Not JSON: the server's front door answering while Salio restarts.
          if (response.status >= 500) {
            detail = "Salio is down. Nothing was imported. Try again shortly.";
          }
        }
        setProblem(detail);
        return;
      }

      setImported(await response.json());
      onImported();
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      setBusy("");
    }
  }

  async function undo() {
    if (imported === null) {
      return;
    }

    setBusy("undo");
    setProblem("");
    const outcome = await undoImport(shopId, imported.batchId);
    setBusy("");
    setConfirmingUndo(false);

    if (outcome.expired) {
      onExpired();
      return;
    }
    if (outcome.problem) {
      setProblem(outcome.problem);
      return;
    }
    if (outcome.result) {
      setUndoResult(outcome.result);
      onImported();
    }
  }

  function startAgain() {
    setFile(null);
    setPreview(null);
    setImported(null);
    setUndoResult(null);
    setProblem("");
    setStockBasis(null);
    setPaidWith(null);
    if (fileInput.current) fileInput.current.value = "";
  }

  const problems = preview?.errors.length ?? 0;
  const nothingToDo = preview !== null && preview.rowsToCreate === 0 && preview.rowsToUpdate === 0;
  const asksStockBasis = preview?.hasQuantities === true;
  // Import waits for the answer, and for how it was paid when bought.
  const stockAnswered =
    !asksStockBasis || stockBasis === "OpeningStock" || (stockBasis === "Purchase" && paidWith !== null);

  const problemBox = problem !== "" && (
    <p role="alert" className="rounded-lg bg-error px-3 py-2 text-base text-error-foreground">
      {problem}
    </p>
  );

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold md:hidden"
        >
          ‹ More
        </button>
        <span className="min-w-0 flex-1 truncate text-base font-bold">Import</span>
        <button onClick={onHistory} className="min-h-11 shrink-0 px-2 text-sm font-bold text-primary underline">
          Past imports
        </button>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto">
        {/* Step 4: done. */}
        {imported !== null && (
          <section className="space-y-3 p-3">
            <p role="status" className="rounded-lg bg-primary-soft px-3 py-3 text-2xl font-bold text-primary">
              Imported: {counted(imported.rowsCreated, `new ${words.singular}`, `new ${words.plural}`)},{" "}
              {imported.rowsUpdated.toLocaleString("en-KE")} updated
            </p>
            {problemBox}
            {undoResult !== null ? (
              <UndoResultView result={undoResult} words={words} />
            ) : confirmingUndo ? (
              <UndoConfirm busy={busy === "undo"} onYes={undo} onNo={() => setConfirmingUndo(false)} />
            ) : (
              <button
                onClick={() => setConfirmingUndo(true)}
                className="min-h-12 w-full rounded-xl border-2 border-border-strong bg-surface text-base font-bold"
              >
                Undo import
              </button>
            )}
            <p className="text-base md:text-sm text-muted-foreground">
              Undo later from Past imports
            </p>
            <div className="flex flex-wrap gap-2">
              <button
                onClick={startAgain}
                className="min-h-12 flex-1 rounded-xl bg-primary px-3 text-base font-bold text-primary-foreground"
              >
                Another file
              </button>
              <button
                onClick={onHistory}
                className="min-h-12 flex-1 rounded-xl border-2 border-border-strong bg-surface px-3 text-base font-bold"
              >
                Past imports
              </button>
            </div>
          </section>
        )}

        {/* Step 3: what the file would do. Nothing written yet. */}
        {imported === null && preview !== null && (
          <section className="space-y-3 p-3">
            <p className="text-lg font-bold">3. Check before importing</p>
            <div className="rounded-lg border border-border bg-surface px-3 py-3">
              <p className="text-2xl font-bold leading-tight">
                {counted(preview.rowsToCreate, `new ${words.singular}`, `new ${words.plural}`)},{" "}
                {preview.rowsToUpdate.toLocaleString("en-KE")} updated,{" "}
                <span className={problems > 0 ? "text-error" : ""}>{counted(problems, "problem", "problems")}</span>
              </p>
              <p className="mt-1 truncate text-sm text-muted-foreground">
                {preview.fileName}
                {preview.rowsUnchanged
                  ? ` · ${counted(preview.rowsUnchanged, "row already the same", "rows already the same")}`
                  : ""}
              </p>
            </div>

            {problemBox}

            {problems > 0 && (
              <div>
                <p className="mb-1 text-base font-bold">
                  Fix {problems === 1 ? "this" : "these"}, then choose again:
                </p>
                <ul className="rounded-lg border-2 border-error bg-surface">
                  {preview.errors.map((issue, index) => (
                    <IssueRow key={index} issue={issue} />
                  ))}
                </ul>
              </div>
            )}

            {preview.warnings.length > 0 && (
              <div className="text-muted-foreground">
                <p className="mb-1 text-sm font-semibold">
                  Worth a look ({preview.warnings.length})
                </p>
                <ul className="rounded-lg border border-border bg-surface text-muted-foreground">
                  {preview.warnings.map((issue, index) => (
                    <IssueRow key={index} issue={issue} />
                  ))}
                </ul>
              </div>
            )}

            {/* Where the stock came from. Asked only when a row has a quantity. */}
            {problems === 0 && !nothingToDo && asksStockBasis && (
              <div className="space-y-2 rounded-lg border-2 border-border-strong bg-surface p-3">
                <p className="text-lg font-bold">Where did this stock come from?</p>
                <div className="grid gap-2 md:grid-cols-2">
                  {(
                    [
                      {
                        basis: "OpeningStock",
                        title: "Stock I already had when I started",
                        detail: "On the shelf before your first sale in Salio",
                      },
                      {
                        basis: "Purchase",
                        title: "Stock I bought",
                        detail: "Bought from a supplier, paid or owed",
                      },
                    ] as const
                  ).map((option) => (
                    <button
                      key={option.basis}
                      type="button"
                      onClick={() => {
                        setStockBasis(option.basis);
                        setProblem("");
                      }}
                      aria-pressed={stockBasis === option.basis}
                      className={
                        "min-h-16 rounded-lg px-3 py-2 text-left " +
                        (stockBasis === option.basis
                          ? "bg-primary text-primary-foreground"
                          : "border-2 border-border-strong bg-surface text-foreground")
                      }
                    >
                      <span className="block text-base font-bold">{option.title}</span>
                      <span className="block text-sm">{option.detail}</span>
                    </button>
                  ))}
                </div>
                {stockBasis === "Purchase" && (
                  <PaidWithChoice
                    value={paidWith}
                    onChange={(value) => {
                      setPaidWith(value);
                      setProblem("");
                    }}
                  />
                )}
                {stockBasis === "Purchase" && paidWith === null && (
                  <p className="text-sm text-muted-foreground">Choose how you paid</p>
                )}
              </div>
            )}

            {problems === 0 && nothingToDo && (
              <p className="text-base">Nothing new to import</p>
            )}
          </section>
        )}

        {/* Steps 1 and 2. */}
        {imported === null && preview === null && (
          <section className="space-y-4 p-3">
            <div className="space-y-2 rounded-lg border border-border bg-surface p-3">
              {shelfEmpty ? (
                <p className="text-lg font-bold">1. Download the file</p>
              ) : (
                <p className="text-lg font-bold">1. Download the file</p>
              )}
              <button
                onClick={download}
                disabled={busy !== ""}
                className="min-h-12 w-full rounded-xl border-2 border-primary bg-surface px-3 text-base font-bold text-primary disabled:opacity-40"
              >
                {busy === "download"
                  ? "Preparing…"
                  : shelfEmpty
                    ? "Download blank (.csv)"
                    : `Download ${words.plural} (.csv)`}
              </button>
              <p className="text-sm text-muted-foreground">
                Quantity counts for new {words.plural} only. For the rest, use Stock in.
              </p>
            </div>

            <div className="space-y-2 rounded-lg border border-border bg-surface p-3">
              <p className="text-lg font-bold">2. Choose your file</p>
              <label
                onDragOver={(e) => {
                  e.preventDefault();
                  setDragging(true);
                }}
                onDragLeave={() => setDragging(false)}
                onDrop={(e) => {
                  e.preventDefault();
                  setDragging(false);
                  const dropped = e.dataTransfer.files[0];
                  if (dropped) choose(dropped);
                }}
                className={
                  "flex min-h-32 cursor-pointer flex-col items-center justify-center gap-1 rounded-xl border-2 border-dashed px-3 py-4 text-center " +
                  (dragging ? "border-primary bg-primary-soft" : "border-border-strong bg-background")
                }
              >
                <span className="text-lg font-bold text-primary">
                  {busy === "preview" ? "Checking…" : "Tap or drop file here"}
                </span>
                <span className="text-sm text-muted-foreground">.csv or .xlsx, up to 5 MB</span>
                <input
                  ref={fileInput}
                  type="file"
                  accept=".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                  className="sr-only"
                  disabled={busy !== ""}
                  onChange={(e) => {
                    const chosen = e.target.files?.[0];
                    if (chosen) choose(chosen);
                  }}
                />
              </label>
            </div>

            {problemBox}
          </section>
        )}
      </main>

      {imported === null && preview !== null && (
        <section className="flex shrink-0 gap-2 border-t-2 border-border bg-surface p-2">
          <button
            onClick={startAgain}
            disabled={busy !== ""}
            className="min-h-14 flex-1 rounded-xl border-2 border-border-strong bg-surface px-2 text-base font-bold"
          >
            Different file
          </button>
          <button
            onClick={commit}
            disabled={problems > 0 || nothingToDo || !stockAnswered || busy !== ""}
            className="min-h-14 flex-1 rounded-xl bg-primary px-2 text-xl font-bold text-primary-foreground disabled:opacity-40"
          >
            {busy === "commit" ? "Importing…" : "Import"}
          </button>
        </section>
      )}
    </div>
  );
}
