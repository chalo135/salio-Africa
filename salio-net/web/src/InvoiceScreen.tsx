import { useState } from "react";
import { API, kes, type ItemWords } from "./auth";
import { refusalText } from "./importApi";
import InvoiceCleanUp from "./InvoiceCleanUp";
import InvoiceReview, { type InvoiceView } from "./InvoiceReview";
import { loadImage } from "./invoiceImage";
import { apiFetch } from "./api";

// The most the server takes for one invoice.
const MAX_BYTES = 10 * 1024 * 1024;

type Step =
  | { kind: "pick" }
  | { kind: "clean"; file: File; image: HTMLImageElement }
  | { kind: "review"; view: InvoiceView }
  | { kind: "done"; linesReceived: number; totalMinor: number; paidWith: string };

// "Read a supplier invoice". Owner only: it is cost and stock (rule 13).
// A photo is cleaned up here, on this phone, then sent as a PNG. A PDF is
// sent as it is. What comes back is checked line by line, and nothing is
// written to stock or the books until Commit.
export default function InvoiceScreen({
  shopId,
  words,
  products,
  onBack,
  onReceived,
  onExpired,
}: {
  shopId: string;
  words: ItemWords;
  products: { id: string; sku: string; name: string }[];
  onBack: () => void;
  onReceived: () => void; // reloads the shelf
  onExpired: () => void;
}) {
  const [step, setStep] = useState<Step>({ kind: "pick" });
  const [sending, setSending] = useState(false);
  const [problem, setProblem] = useState("");
  const [note, setNote] = useState("");

  async function chosen(file: File) {
    setProblem("");
    setNote("");

    const isPdf = file.type === "application/pdf" || file.name.toLowerCase().endsWith(".pdf");
    if (isPdf) {
      if (file.size > MAX_BYTES) {
        setProblem(`PDF is ${(file.size / 1024 / 1024).toFixed(1)} MB. Limit is 10 MB.`);
        return;
      }
      send(file, file.name);
      return;
    }

    if (!file.type.startsWith("image/")) {
      setProblem("Choose a photo or PDF");
      return;
    }

    try {
      const image = await loadImage(file);
      setStep({ kind: "clean", file, image });
    } catch {
      setProblem("Can't open photo. Retake it.");
    }
  }

  async function send(file: Blob, name: string) {
    if (file.size > MAX_BYTES) {
      setProblem("Too big. Crop closer.");
      return;
    }

    setSending(true);
    setProblem("");
    const form = new FormData();
    form.append("file", file, name);

    try {
      const response = await apiFetch(`${API}/api/supplier-invoices?organizationId=${shopId}`, {
        method: "POST",
        credentials: "include",
        body: form,
      });

      if (response.status === 401) {
        onExpired();
        return;
      }
      if (!response.ok) {
        setProblem(await refusalText(response, "Can't read this invoice"));
        return;
      }

      setStep({ kind: "review", view: await response.json() });
    } catch {
      setProblem("Can't reach Salio");
    } finally {
      setSending(false);
    }
  }

  const problemBox = problem !== "" && (
    <p role="alert" className="m-2 rounded-lg bg-error px-3 py-2 text-base text-error-foreground">
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
        <span className="min-w-0 truncate text-base font-bold">Scan invoice</span>
      </div>

      {step.kind === "pick" && (
        <main className="min-h-0 flex-1 space-y-3 overflow-y-auto p-3">
          {note !== "" && (
            <p role="status" className="rounded-lg bg-primary-soft px-3 py-2 text-base font-semibold text-primary">
              {note}
            </p>
          )}
          <p className="text-base">Check the lines before stock moves</p>
          <div className="grid gap-2 md:grid-cols-2">
            <label className="flex min-h-20 cursor-pointer items-center justify-center rounded-xl bg-primary px-3 text-xl font-bold text-primary-foreground">
              Take photo
              <input
                type="file"
                accept="image/*"
                capture="environment"
                className="sr-only"
                disabled={sending}
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  e.target.value = "";
                  if (file) chosen(file);
                }}
              />
            </label>
            <label className="flex min-h-20 cursor-pointer items-center justify-center rounded-xl border-2 border-primary bg-surface px-3 text-xl font-bold text-primary">
              {sending ? "Reading…" : "Choose file"}
              <input
                type="file"
                accept="image/*,application/pdf,.pdf"
                className="sr-only"
                disabled={sending}
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  e.target.value = "";
                  if (file) chosen(file);
                }}
              />
            </label>
          </div>
          {problemBox}
        </main>
      )}

      {step.kind === "clean" && (
        <>
          {problemBox}
          <InvoiceCleanUp
            file={step.file}
            image={step.image}
            sending={sending}
            onSend={(png) => send(png, "invoice.png")}
            onCancel={() => {
              URL.revokeObjectURL(step.image.src);
              setProblem("");
              setStep({ kind: "pick" });
            }}
          />
        </>
      )}

      {step.kind === "review" && (
        <InvoiceReview
          shopId={shopId}
          words={words}
          first={step.view}
          products={products}
          onCommitted={(linesReceived, totalMinor, paidWith) => {
            setStep({ kind: "done", linesReceived, totalMinor, paidWith });
            onReceived();
          }}
          onDiscarded={() => {
            setNote("Discarded. Nothing received.");
            setStep({ kind: "pick" });
          }}
          onExpired={onExpired}
        />
      )}

      {step.kind === "done" && (
        <main className="min-h-0 flex-1 space-y-3 overflow-y-auto p-3">
          <p role="status" className="rounded-lg bg-primary-soft px-3 py-3 text-2xl font-bold text-primary">
            Stock received: {step.linesReceived} {step.linesReceived === 1 ? "line" : "lines"}
          </p>
          <p className="text-base">
            KES <span className="font-money font-bold">{kes(step.totalMinor)}</span>, {step.paidWith}
          </p>
          <div className="flex flex-wrap gap-2">
            <button
              onClick={() => setStep({ kind: "pick" })}
              className="min-h-12 flex-1 rounded-xl bg-primary px-3 text-base font-bold text-primary-foreground"
            >
              Scan another
            </button>
            <button
              onClick={onBack}
              className="min-h-12 flex-1 rounded-xl border-2 border-border-strong bg-surface px-3 text-base font-bold"
            >
              Done
            </button>
          </div>
        </main>
      )}
    </div>
  );
}
