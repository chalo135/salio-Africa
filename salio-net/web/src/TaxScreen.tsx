import { useState } from "react";
import { API, kes } from "./auth";
import { apiFetch, problemText } from "./api";

// From GET /api/tax/filing-pack.
export type FilingPack = {
  taxDueMinor: number;
  grossTurnoverMinor: number;
  ratePercent: number;
  rateEffectiveFrom: string;
  saleCount: number;
  // Plain English from the server, one problem per line.
  exceptions: string[];
};

// What POST /api/tax/filed-returns sends back, plus who pressed the button.
// The server does not store who: recordedBy is the name signed in on this
// phone when it was saved.
export type FiledReturn = {
  amountMinor: number;
  filedAt: string;
  acknowledgementReference: string;
  recordedBy: string;
};

// The server's TaxType number.
const TURNOVER_TAX = 0;

// "2024-12-27" as "27 December 2024".
function dateText(isoDate: string) {
  const [year, month, day] = isoDate.split("-").map(Number);
  return new Date(year, month - 1, day).toLocaleDateString("en-KE", {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

export default function TaxScreen({
  shopId,
  userName,
  monthLabel,
  from,
  to,
  monthEnded,
  pack,
  netProfitMinor,
  filed,
  onFiled,
  onBack,
  onExpired,
}: {
  shopId: string;
  userName: string;
  monthLabel: string;
  from: string;
  to: string;
  monthEnded: boolean;
  pack: FilingPack;
  netProfitMinor: number;
  filed: FiledReturn | null;
  onFiled: (filedReturn: FiledReturn) => void;
  onBack: () => void;
  onExpired: () => void;
}) {
  // The "I have filed" form, while it is open.
  const [recording, setRecording] = useState(false);
  const [reference, setReference] = useState("");
  const [saving, setSaving] = useState(false);
  const [problem, setProblem] = useState("");

  async function recordFiled() {
    if (reference.trim() === "") {
      setProblem("Type the iTax reference");
      return;
    }

    setSaving(true);
    setProblem("");

    try {
      const response = await apiFetch(`${API}/api/tax/filed-returns`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          organizationId: shopId,
          periodFrom: from,
          periodTo: to,
          taxType: TURNOVER_TAX,
          amountMinor: pack.taxDueMinor,
          filedAt: new Date().toISOString(),
          acknowledgementReference: reference.trim(),
        }),
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        // The reference stays typed in the box.
        setProblem(await problemText(response));
        return;
      }

      const saved = await response.json();
      onFiled({
        amountMinor: saved.amountMinor,
        filedAt: saved.filedAt,
        acknowledgementReference: saved.acknowledgementReference,
        recordedBy: userName,
      });
    } catch {
      setProblem("Can't reach Salio.");
    } finally {
      setSaving(false);
    }
  }

  const lineClass = "flex items-baseline justify-between gap-3 py-1";
  const profitWords =
    netProfitMinor < 0 ? "a loss of KES " + kes(-netProfitMinor) : "KES " + kes(netProfitMinor);

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold"
        >
          ‹ Month end
        </button>
        <span className="min-w-0 truncate text-base font-bold">Tax · {monthLabel}</span>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto px-3 py-3">
        {/* The figure, alone. */}
        <section className="rounded-lg bg-primary px-3 py-5 text-center text-primary-foreground">
          <p className="text-base text-primary-foreground-soft">To file for {monthLabel}</p>
          <p className="mt-1 font-money text-2xl font-bold">KES {kes(pack.taxDueMinor)}</p>
          <p className="mt-3 text-sm text-primary-foreground-soft">
            You file on iTax
          </p>
        </section>

        <section className="mt-4 rounded-lg border border-border bg-surface px-3 py-2">
          <p className="text-sm font-bold text-muted-foreground">The working</p>
          <div className={lineClass}>
            <span className="text-base">Everything sold ({pack.saleCount} sales)</span>
            <span className="font-money text-lg">{kes(pack.grossTurnoverMinor)}</span>
          </div>
          <div className={lineClass}>
            <span className="text-base">Rate</span>
            <span className="font-money text-lg">{pack.ratePercent}%</span>
          </div>
          <div className={lineClass + " border-t border-border"}>
            <span className="text-base font-bold">Tax</span>
            <span className="font-money text-lg font-bold">{kes(pack.taxDueMinor)}</span>
          </div>
          <p className="mt-2 text-base">Charged on sales, not profit ({profitWords})</p>
        </section>

        <section className="mt-4">
          <p className="text-sm font-bold text-muted-foreground">Before filing</p>
          <p className="mt-1 rounded-lg border border-border bg-surface px-3 py-2 text-base">
            Rate {pack.ratePercent}%, since {dateText(pack.rateEffectiveFrom)}. Check it on iTax.
          </p>
          {pack.exceptions.length === 0 ? (
            <p className="mt-2 text-base md:text-sm text-muted-foreground">
              No problems
            </p>
          ) : (
            <ul className="mt-2 space-y-2">
              {pack.exceptions.map((exception) => (
                <li
                  key={exception}
                  className="rounded-lg bg-warning px-3 py-2 text-base text-warning-foreground"
                >
                  {exception}
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="mt-8 border-t border-border pt-4 pb-6">
          {filed !== null ? (
            <div className="rounded-lg border-2 border-primary bg-surface px-3 py-3">
              <p className="text-lg font-bold">{monthLabel} is closed</p>
              <div className={lineClass}>
                <span className="text-base">Filed</span>
                <span className="text-base font-semibold">Turnover Tax, {monthLabel}</span>
              </div>
              <div className={lineClass}>
                <span className="text-base">Amount</span>
                <span className="font-money text-lg font-bold">KES {kes(filed.amountMinor)}</span>
              </div>
              <div className={lineClass}>
                <span className="text-base">When</span>
                <span className="text-base font-semibold">
                  {new Date(filed.filedAt).toLocaleString("en-KE", {
                    day: "numeric",
                    month: "long",
                    year: "numeric",
                    hour: "2-digit",
                    minute: "2-digit",
                  })}
                </span>
              </div>
              <div className={lineClass}>
                <span className="text-base">By</span>
                <span className="text-base font-semibold">{filed.recordedBy}</span>
              </div>
              <div className={lineClass}>
                <span className="text-base">iTax reference</span>
                <span className="font-money text-base font-semibold">
                  {filed.acknowledgementReference}
                </span>
              </div>
            </div>
          ) : !monthEnded ? (
            <p className="text-base md:text-sm text-muted-foreground">
              Record filing after month ends
            </p>
          ) : !recording ? (
            <button
              onClick={() => setRecording(true)}
              className="h-14 w-full rounded-xl bg-primary text-lg font-bold text-primary-foreground"
            >
              I filed on iTax
            </button>
          ) : (
            <div className="rounded-lg border-2 border-warning-strong bg-surface px-3 py-3">
              <label className="block text-base md:text-sm font-semibold">
                iTax reference
                <input
                  value={reference}
                  onChange={(e) => setReference(e.target.value)}
                  autoCapitalize="characters"
                  className="mt-1 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3
                    font-money text-lg focus:border-primary focus:outline-none"
                />
              </label>
              <p className="mt-3 text-base">
                This closes <strong>{monthLabel}</strong>. Nothing more can be dated in it.
              </p>
              {pack.exceptions.length > 0 && (
                <p className="mt-2 text-base font-semibold text-warning-strong">
                  {pack.exceptions.length} {pack.exceptions.length === 1 ? "problem" : "problems"} above.
                  The figure may be wrong.
                </p>
              )}
              {problem !== "" && (
                <div role="alert" className="mt-3 rounded-lg bg-error px-3 py-2 text-error-foreground">
                  <p className="text-base font-bold">NOT recorded</p>
                  <p className="text-base">{problem}</p>
                </div>
              )}
              <div className="mt-3 flex gap-2">
                <button
                  onClick={() => {
                    setRecording(false);
                    setProblem("");
                  }}
                  className="h-12 flex-1 rounded-lg border-2 border-border-strong bg-surface text-base font-bold"
                >
                  Not yet
                </button>
                <button
                  onClick={recordFiled}
                  disabled={saving}
                  className="h-12 flex-1 rounded-lg bg-primary text-base font-bold text-primary-foreground
                    disabled:opacity-40"
                >
                  {saving ? "Saving…" : "Yes, close it"}
                </button>
              </div>
            </div>
          )}
        </section>
      </main>
    </div>
  );
}
