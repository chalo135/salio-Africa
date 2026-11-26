import { useEffect, useRef } from "react";
import { kes } from "./auth";
import { type ChangeRow, type ChangesCard as Card } from "./useChanges";

// What changed in the shop since the Owner last marked it seen: the bell,
// the one floating change on Sell, and the sheet with all of them
// (design/NotifyFloat, design/NotifySheet). Owner only: App.tsx shows these
// to an Owner, and the server refuses anyone else.

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

// "Tuesday", "earlier today", or "12 Nov" when over a week.
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

function heading(card: Card) {
  return `${card.total} ${card.total === 1 ? "thing" : "things"} changed since ${sinceWords(card.since)}`;
}

// Most important first: a cost that went up, then a price someone else
// changed, then something that ran out. Newest first within each. The
// floating card shows the first; the sheet lists them all in this order.
const PRIORITY: Record<ChangeRow["kind"], number> = { costUp: 0, priceChanged: 1, ranOut: 2 };

function byImportance(rows: ChangeRow[]) {
  return [...rows].sort((a, b) =>
    PRIORITY[a.kind] !== PRIORITY[b.kind]
      ? PRIORITY[a.kind] - PRIORITY[b.kind]
      : new Date(b.at).getTime() - new Date(a.at).getTime(),
  );
}

function BellIcon({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9" />
      <path d="M10.3 21a1.94 1.94 0 0 0 3.4 0" />
    </svg>
  );
}

// The bell, on green: the Sell header and the tablet sidebar. The count is
// hidden at 0. Owner only: App leaves it out for everyone else.
export function BellButton({ count, onOpen }: { count: number; onOpen: () => void }) {
  return (
    <button
      type="button"
      onClick={onOpen}
      aria-label={count > 0 ? `${count} ${count === 1 ? "change" : "changes"} not seen` : "Changes"}
      className="relative flex h-11 w-11 shrink-0 items-center justify-center rounded-[10px] border-[1.5px]
        border-primary-outline text-primary-foreground active:bg-primary-fill"
    >
      <BellIcon size={20} />
      {count > 0 && (
        <span
          className="absolute -right-1.5 -top-1.5 flex h-5 min-w-5 items-center justify-center rounded-full border-2
            border-primary bg-badge px-[5px] font-money text-[12px] font-extrabold leading-none text-foreground"
        >
          {count}
        </span>
      )}
    </button>
  );
}

function CloseButton({ size, label, onClick }: { size: number; label: string; onClick: () => void }) {
  return (
    // Drawn small and round; the space around it is the tap target.
    <button type="button" onClick={onClick} aria-label={label} className="-m-1.5 flex shrink-0 items-center justify-center p-1.5">
      <span
        style={{ width: size, height: size }}
        className="flex items-center justify-center rounded-full bg-close text-[17px] leading-none text-muted-foreground"
        aria-hidden="true"
      >
        ×
      </span>
    </button>
  );
}

// "Engine Oil now costs you 450, was 350" and what is kept now and before.
function CostUpText({ row, big }: { row: ChangeRow; big: boolean }) {
  if (row.wasMinor === null || row.nowMinor === null || row.askingMinor === null) {
    return null;
  }
  return (
    <>
      <span className={"block font-bold leading-[1.3] " + (big ? "text-[15px]" : "text-[15px]")}>
        {row.partName} now costs you <span className="font-money">{shillings(row.nowMinor)}</span>, was{" "}
        <span className="font-money">{shillings(row.wasMinor)}</span>
      </span>
      <span className="mt-0.5 block text-[13px] leading-[1.35] text-notice-foreground">
        You still sell at <span className="font-money">{shillings(row.askingMinor)}</span>. You keep{" "}
        <span className="font-money">{shillings(row.keptNowMinor ?? row.askingMinor - row.nowMinor)}</span>, used to
        keep <span className="font-money">{shillings(row.keptWasMinor ?? row.askingMinor - row.wasMinor)}</span>.
      </span>
    </>
  );
}

function FixButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="min-h-11 shrink-0 rounded-[10px] bg-primary px-[14px] text-[14px] font-bold text-primary-foreground"
    >
      Fix
    </button>
  );
}

// The one change floating over the part list, under the chips. Only ×, See
// all and Fix act on it; a tap anywhere else goes to what is underneath.
export function FloatingChange({
  card,
  onTuck,
  onSeeAll,
  onFix,
}: {
  card: Card;
  onTuck: () => void;
  onSeeAll: () => void;
  onFix: (productId: string) => void;
}) {
  const row = byImportance(card.rows)[0];
  const more = card.total - 1;

  return (
    <section
      aria-label="What changed"
      className="pointer-events-auto rounded-[14px] border-l-[5px] border-notice-border bg-surface py-3 pl-[14px] pr-3
        shadow-[0_10px_28px_rgba(25,24,20,0.22),0_2px_6px_rgba(25,24,20,0.10)]"
    >
      <div className="flex items-center gap-2">
        <span className="shrink-0 text-warning-strong">
          <BellIcon size={15} />
        </span>
        <h2 className="min-w-0 flex-1 text-[13px] font-bold text-warning-strong">{heading(card)}</h2>
        <CloseButton size={30} label="Tuck away" onClick={onTuck} />
      </div>

      <div className="mt-2 flex items-center gap-[10px]">
        <span className="min-w-0 flex-1">
          {row.kind === "costUp" && <CostUpText row={row} big />}
          {row.kind === "priceChanged" && (
            <>
              <span className="block text-[15px] font-bold leading-[1.3]">
                {row.changedBy} changed {row.partName}
              </span>
              <span className="mt-0.5 block text-[13px] text-muted-foreground">
                <span className="font-money">
                  {shillings(row.wasMinor ?? 0)} → {shillings(row.nowMinor ?? 0)}
                </span>{" "}
                · {when(row.at)}
              </span>
            </>
          )}
          {row.kind === "ranOut" && (
            <>
              <span className="block text-[15px] font-bold leading-[1.3]">{row.partName} ran out</span>
              <span className="mt-0.5 block text-[13px] text-muted-foreground">{when(row.at)}</span>
            </>
          )}
        </span>
        {row.kind === "costUp" && <FixButton onClick={() => onFix(row.productId)} />}
      </div>

      <div className="mt-[10px] flex items-center border-t border-notice-line pt-[9px]">
        <span className="min-w-0 flex-1 text-[13px] text-muted-foreground">
          {more > 0 && `+ ${more} more ${more === 1 ? "change" : "changes"}`}
        </span>
        <button type="button" onClick={onSeeAll} className="-my-2 min-h-11 text-[13px] font-bold text-primary">
          See all →
        </button>
      </div>
    </section>
  );
}

function RowIcon({ kind }: { kind: ChangeRow["kind"] }) {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth={kind === "costUp" ? 3 : 2.2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"
      className={"mt-[3px] shrink-0 " + (kind === "costUp" ? "text-warning-strong" : "text-foreground")}>
      {kind === "costUp" && (
        <>
          <path d="M12 19V5" />
          <path d="M5 12l7-7 7 7" />
        </>
      )}
      {kind === "priceChanged" && (
        <>
          <path d="M20 13l-7 7-9-9V4h7l9 9z" />
          <path d="M8 8h.01" />
        </>
      )}
      {kind === "ranOut" && <path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z" />}
    </svg>
  );
}

// Every unseen change, in a sheet from the bottom over a dimmed screen.
// ×, a tap on the dim, Escape or a swipe down closes it and marks nothing.
// Only "Mark all as seen" moves what the bell counts from.
export function ChangesSheet({
  card,
  onClose,
  onMarkAllSeen,
  onFix,
  onStockIn,
}: {
  card: Card;
  onClose: () => void;
  onMarkAllSeen: () => void;
  onFix: (productId: string) => void;
  onStockIn: (row: ChangeRow) => void;
}) {
  // Where a downward swipe on the top of the sheet began.
  const swipeFrom = useRef<number | null>(null);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") {
        onClose();
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div onClick={onClose} className="fixed inset-0 z-30 flex items-end justify-center bg-[rgba(25,24,20,0.48)] md:items-center">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="changes-sheet-heading"
        onClick={(e) => e.stopPropagation()}
        className="flex max-h-[88%] w-full max-w-md flex-col rounded-t-[20px] bg-surface px-[18px] pb-[22px] pt-[10px]
          shadow-[0_-8px_30px_rgba(0,0,0,0.18)] md:rounded-[20px]"
      >
        {/* The handle and heading: a swipe down here closes the sheet. */}
        <div
          className="shrink-0 touch-none"
          onTouchStart={(e) => {
            swipeFrom.current = e.touches[0].clientY;
          }}
          onTouchEnd={(e) => {
            if (swipeFrom.current !== null && e.changedTouches[0].clientY - swipeFrom.current > 60) {
              onClose();
            }
            swipeFrom.current = null;
          }}
        >
          <div className="mx-auto mb-3 h-[5px] w-11 rounded-full bg-handle" aria-hidden="true" />
          <div className="mb-1 flex items-center gap-[10px]">
            <h2 id="changes-sheet-heading" className="min-w-0 flex-1 text-[19px] font-extrabold tracking-[-0.01em]">
              {heading(card)}
            </h2>
            <CloseButton size={34} label="Close" onClick={onClose} />
          </div>
        </div>

        {/* Long lists scroll here, inside the sheet. */}
        <ul className="min-h-0 flex-1 overflow-y-auto">
          {byImportance(card.rows).map((row, index) => (
            <li key={index} className="flex items-center gap-[10px] border-b border-row-line py-[13px] last:border-b-0">
              <span className="flex min-w-0 flex-1 gap-[10px]">
                <RowIcon kind={row.kind} />
                <span className="min-w-0 flex-1">
                  {row.kind === "costUp" && <CostUpText row={row} big={false} />}
                  {row.kind === "priceChanged" && (
                    <>
                      <span className="block text-[15px] font-bold leading-[1.3]">
                        {row.changedBy} changed {row.partName}
                      </span>
                      <span className="mt-[3px] block text-[13px] text-muted-foreground">
                        <span className="font-money">
                          {shillings(row.wasMinor ?? 0)} → {shillings(row.nowMinor ?? 0)}
                        </span>{" "}
                        · {when(row.at)}
                      </span>
                    </>
                  )}
                  {row.kind === "ranOut" && (
                    <>
                      <span className="block text-[15px] font-bold leading-[1.3]">{row.partName} ran out</span>
                      <span className="mt-[3px] block text-[13px] text-muted-foreground">{when(row.at)}</span>
                    </>
                  )}
                </span>
              </span>
              {row.kind === "costUp" && <FixButton onClick={() => onFix(row.productId)} />}
              {row.kind === "ranOut" && (
                <button
                  type="button"
                  onClick={() => onStockIn(row)}
                  className="min-h-11 shrink-0 rounded-[10px] border-2 border-primary px-[14px] text-[14px] font-bold text-primary"
                >
                  Stock in
                </button>
              )}
            </li>
          ))}
        </ul>

        <button
          type="button"
          onClick={onMarkAllSeen}
          className="mt-[14px] h-[52px] w-full shrink-0 rounded-xl bg-primary text-base font-bold text-primary-foreground"
        >
          Mark all as seen
        </button>
        <p className="mt-[9px] shrink-0 text-center text-[12px] text-muted-foreground">
          The bell lights up again when something new changes.
        </p>
      </div>
    </div>
  );
}
