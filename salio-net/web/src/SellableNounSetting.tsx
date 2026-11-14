import { useState } from "react";
import { API, type ItemWords } from "./auth";
import { apiFetch } from "./api";

// "What Salio calls what you sell": the one place a person chooses the word.
// Setup derives it from the trade and never asks. Owner only: App.tsx shows
// it to an Owner, and the server refuses a Cashier anyway.
export default function SellableNounSetting({
  shopId,
  words,
  onSaved,
  onExpired,
}: {
  shopId: string;
  words: ItemWords;
  onSaved: () => void; // reloads the shop, so every screen uses the new word
  onExpired: () => void;
}) {
  const [text, setText] = useState(words.Plural);
  const [busy, setBusy] = useState(false);
  const [said, setSaid] = useState<{ ok: boolean; text: string } | null>(null);

  const trimmed = text.trim();

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (busy) {
      return;
    }
    if (trimmed === "") {
      setSaid({ ok: false, text: "Type a word" });
      return;
    }

    setBusy(true);
    setSaid(null);
    try {
      const response = await apiFetch(`${API}/api/organizations/sellable-noun?organizationId=${shopId}`, {
        method: "PUT",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ sellableNoun: trimmed }),
      });

      if (response.status === 401) {
        onExpired();
        return;
      }
      if (!response.ok) {
        const body = await response.json().catch(() => null);
        setSaid({ ok: false, text: body?.detail ?? "Not saved." });
        return;
      }

      setSaid({ ok: true, text: "Saved." });
      onSaved();
    } catch {
      setSaid({ ok: false, text: "Can't reach Salio" });
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={save} className="border-b border-border bg-surface px-3 py-3">
      <label htmlFor="sellable-noun" className="block text-base font-bold">
        Call them
      </label>
      <div className="mt-1 flex gap-2">
        <input
          id="sellable-noun"
          value={text}
          onChange={(e) => {
            setText(e.target.value);
            setSaid(null);
          }}
          maxLength={30}
          autoComplete="off"
          className="h-12 min-w-0 flex-1 rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
            focus:border-primary focus:outline-none"
        />
        <button
          type="submit"
          disabled={busy || trimmed === words.Plural}
          className="h-12 shrink-0 rounded-lg bg-primary px-5 text-lg font-bold text-primary-foreground disabled:opacity-40"
        >
          {busy ? "Saving…" : "Save"}
        </button>
      </div>
      {said && (
        <p role="status" className={"mt-1 text-base font-semibold " + (said.ok ? "text-primary" : "text-error")}>
          {said.text}
        </p>
      )}
    </form>
  );
}
