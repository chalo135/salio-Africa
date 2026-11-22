import { useEffect, useState } from "react";
import { API, kes } from "./auth";
import { apiFetch } from "./api";

type Row = {
  kind: "costUp" | "priceChanged" | "ranOut";
  productId: string;
  partName: string;
  at: string;
  wasMinor: number | null;
  nowMinor: number | null;
  askingMinor: number | null;
  // costUp: what one earns at the asking price, on the old and new cost.
  keptWasMinor: number | null;
  keptNowMinor: number | null;
  changedBy: string | null;
};

type Card = {
  since: string;
  total: number;
  productIds: string[];
  rows: Row[];
};

// "450" for a whole amount, "450.50" otherwise. Display only.
function shillings(minor: number) {
  return minor % 100 === 0 ? (minor / 100).toLocaleString("en-KE") : kes(minor);
}

const DAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

// "Tuesday 4:20pm".
function when(iso: string) {
  const d = new Date(iso);
  const hours = d.getHours();
  const minutes = d.getMinutes().toString().padStart(2, "0");
  return `${DAYS[d.getDay()]} ${hours % 12 === 0 ? 12 : hours % 12}:${minutes}${hours < 12 ? "am" : "pm"}`;
}

// "since Tuesday", "since earlier today", or "since 12 Nov" when over a week.
function sinceWords(iso: string) {
  const then = new Date(iso);
  const now = new Date();
  const days = (now.getTime() - then.getTime()) / 86400000;

  if (then.toDateString() === now.toDateString()) {
    return "earlier today";
  }
  if (days < 6) {
    return DAYS[then.getDay()];
  }
  return then.toLocaleDateString("en-KE", { day: "numeric", month: "short" });
}

// What changed since the Owner last pressed Hide (the last 7 days before the
// first Hide). Owner only: App.tsx shows it to an Owner, and the server
// refuses anyone else. With nothing to say it is not there at all: no empty
// card, no "nothing changed". Hide is kept by the server, so it holds across
// sign-ins, PIN unlocks and other phones, and only new changes bring it back.
export default function WelcomeCard({
  shopId,
  onFix,
  onSeeAll,
}: {
  shopId: string;
  onFix: (productId: string) => void;
  onSeeAll: (productIds: string[]) => void;
}) {
  const [card, setCard] = useState<Card | null>(null);
  const [hidden, setHidden] = useState(false);

  useEffect(() => {
    let current = true;

    apiFetch(`${API}/api/welcome-card?organizationId=${shopId}`, { credentials: "include" })
      .then(async (response) => {
        if (!current || !response.ok) {
          return;
        }
        setCard(await response.json());
      })
      .catch(() => {
        // The card is extra. If it cannot load, the till carries on without it.
      });

    return () => {
      current = false;
    };
  }, [shopId]);

  if (card === null || hidden || card.rows.length === 0) {
    return null;
  }

  function hide() {
    // Gone at once. If the server did not hear it, the card comes back on
    // the next visit to Sell, which is better than a Hide that lies.
    setHidden(true);
    apiFetch(`${API}/api/welcome-card/hide?organizationId=${shopId}`, {
      method: "POST",
      credentials: "include",
    }).catch(() => {
      // Nothing to do: see above.
    });
  }

  return (
    <section
      aria-labelledby="changes-heading"
      className="mx-[14px] mt-3 rounded-[14px] border-2 border-notice-border bg-notice px-[14px] pb-[10px] pt-3"
    >
      <div className="flex items-center gap-2">
        <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.3"
          strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className="shrink-0 text-warning-strong">
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7.5v5" />
          <path d="M12 16h.01" />
        </svg>
        <h2 id="changes-heading" className="min-w-0 flex-1 text-[15px] font-bold leading-5">
          {card.total} {card.total === 1 ? "thing" : "things"} changed since {sinceWords(card.since)}
        </h2>
        {/* Drawn as a small word; given a full-size place to tap. */}
        <button onClick={hide} className="-my-2 -mr-2 min-h-11 shrink-0 px-2 text-[12px] font-bold text-warning-strong">
          Hide
        </button>
      </div>

      <ul>
        {card.rows.map((row, index) => (
          <li
            key={index}
            className={
              "flex gap-[10px] border-t border-notice-line py-[9px] " +
              (index === 0 ? "items-start border-t-0 pt-[10px]" : "items-center")
            }
          >
            {row.kind === "costUp" && row.wasMinor !== null && row.nowMinor !== null && row.askingMinor !== null && (
              <>
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"
                  strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className="mt-[3px] shrink-0 text-warning-strong">
                  <path d="M12 19V5" />
                  <path d="M5 12l7-7 7 7" />
                </svg>
                <span className="min-w-0 flex-1">
                  <span className="block text-[14px] font-bold leading-[1.35]">
                    {row.partName} now costs you <span className="font-money">{shillings(row.nowMinor)}</span>, was{" "}
                    <span className="font-money">{shillings(row.wasMinor)}</span>
                  </span>
                  <span className="mt-0.5 block text-[13px] leading-[1.35] text-notice-foreground">
                    You still sell it at <span className="font-money">{shillings(row.askingMinor)}</span>. You keep{" "}
                    <span className="font-money">{shillings(row.keptNowMinor ?? row.askingMinor - row.nowMinor)}</span>,
                    you used to keep{" "}
                    <span className="font-money">{shillings(row.keptWasMinor ?? row.askingMinor - row.wasMinor)}</span>.
                  </span>
                </span>
                <button
                  onClick={() => onFix(row.productId)}
                  className="min-h-11 shrink-0 rounded-[9px] bg-primary px-3 text-[13px] font-bold text-primary-foreground"
                >
                  Fix
                </button>
              </>
            )}

            {row.kind === "priceChanged" && row.wasMinor !== null && row.nowMinor !== null && (
              <span className="min-w-0 flex-1">
                <span className="block text-[14px] font-bold leading-[1.35]">
                  {row.changedBy} changed {row.partName}
                </span>
                <span className="mt-px block text-[13px] text-notice-foreground">
                  <span className="font-money">
                    {shillings(row.wasMinor)} → {shillings(row.nowMinor)}
                  </span>{" "}
                  · {when(row.at)}
                </span>
              </span>
            )}

            {row.kind === "ranOut" && (
              <span className="min-w-0 flex-1">
                <span className="block text-[14px] font-bold leading-[1.35]">{row.partName} ran out</span>
                <span className="mt-px block text-[13px] text-notice-foreground">{when(row.at)}</span>
              </span>
            )}
          </li>
        ))}
      </ul>

      <div className="mt-0.5 border-t border-notice-line pt-[9px]">
        <button
          onClick={() => onSeeAll(card.productIds)}
          className="-my-2 min-h-11 text-[13px] font-bold text-primary"
        >
          See everything that changed →
        </button>
      </div>
    </section>
  );
}
