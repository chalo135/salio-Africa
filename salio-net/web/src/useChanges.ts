import { useEffect, useState } from "react";
import { API } from "./auth";
import { apiFetch } from "./api";

// The data behind the bell, the floating change and the sheet (Changes.tsx):
// what changed in the shop since the Owner last marked it seen. Kept apart
// from the components so the screens reload cleanly while being worked on.

export type ChangeRow = {
  kind: "costUp" | "priceChanged" | "ranOut";
  productId: string;
  partName: string;
  at: string;
  wasMinor: number | null;
  nowMinor: number | null;
  askingMinor: number | null;
  keptWasMinor: number | null;
  keptNowMinor: number | null;
  changedBy: string | null;
};

export type ChangesCard = {
  since: string;
  total: number;
  productIds: string[];
  rows: ChangeRow[];
};

// The newest change in the list, in milliseconds. 0 for none.
function newestAt(rows: ChangeRow[]) {
  let newest = 0;
  for (const row of rows) {
    newest = Math.max(newest, new Date(row.at).getTime());
  }
  return newest;
}

// × on the floating card is remembered per phone: the newest change there
// was when it was tucked. It floats again only for something newer.
function tuckKey(userId: string, shopId: string) {
  return `salio.changes.tuckedAt.${userId}.${shopId}`;
}

function readTucked(userId: string, shopId: string) {
  try {
    const value = Number(localStorage.getItem(tuckKey(userId, shopId)));
    return Number.isFinite(value) ? value : 0;
  } catch {
    // Could not read: treated as never tucked.
    return 0;
  }
}

// The unseen changes, fetched again each time `refreshWhen` turns true
// (App passes "Sell is on screen"). Off entirely for anyone but an Owner.
export function useChanges({
  shopId,
  userId,
  enabled,
  refreshWhen,
}: {
  shopId: string;
  userId: string;
  enabled: boolean;
  refreshWhen: boolean;
}) {
  const [card, setCard] = useState<ChangesCard | null>(null);
  const [tuckedAt, setTuckedAt] = useState(() => readTucked(userId, shopId));

  // Another person or shop: nothing of the last one's changes is kept. Done
  // while drawing, not in an effect (React's pattern for "change state when
  // a prop changes"), as Sidebar.tsx does.
  const whose = `${userId}.${shopId}`;
  const [lastWhose, setLastWhose] = useState(whose);
  if (whose !== lastWhose) {
    setLastWhose(whose);
    setCard(null);
    setTuckedAt(readTucked(userId, shopId));
  }

  useEffect(() => {
    if (!enabled || !refreshWhen) {
      return;
    }
    let current = true;

    apiFetch(`${API}/api/welcome-card?organizationId=${shopId}`, { credentials: "include" })
      .then(async (response) => {
        if (current && response.ok) {
          setCard(await response.json());
        }
      })
      .catch(() => {
        // Extra, not essential: the till carries on without it.
      });

    return () => {
      current = false;
    };
  }, [shopId, enabled, refreshWhen]);

  const rows = enabled && card !== null ? card.rows : [];

  return {
    card: enabled && card !== null && card.rows.length > 0 ? card : null,
    count: rows.length,
    // Floats while there is something newer than what × last tucked away.
    floats: rows.length > 0 && newestAt(rows) > tuckedAt,

    // ×: out of the way, nothing marked seen. The bell keeps its count.
    tuck() {
      const at = newestAt(rows);
      setTuckedAt(at);
      try {
        localStorage.setItem(tuckKey(userId, shopId), String(at));
      } catch {
        // Tucked for now; it may float again on reload.
      }
    },

    // Mark all as seen: the server's Hide. Cleared here at once; if the
    // server did not hear it, the changes come back on the next look.
    markAllSeen() {
      setCard(null);
      apiFetch(`${API}/api/welcome-card/hide?organizationId=${shopId}`, {
        method: "POST",
        credentials: "include",
      }).catch(() => {
        // See above.
      });
    },
  };
}

