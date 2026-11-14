import { type ItemWords } from "./auth";
import SellableNounSetting from "./SellableNounSetting";

// Settings: what Salio calls what this shop sells ("Parts", "Products").
// Owner only: App.tsx shows it to an Owner, and the server refuses a
// Cashier anyway. The box itself is SellableNounSetting, as on the Parts
// screen; this only gives it a place of its own in the menu.
export default function ShopWordScreen({
  shopId,
  words,
  onBack,
  onSaved,
  onExpired,
}: {
  shopId: string;
  words: ItemWords;
  onBack: () => void;
  onSaved: () => void; // reloads the shop, so every screen uses the new word
  onExpired: () => void;
}) {
  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold md:hidden"
        >
          ‹ More
        </button>
        <span className="min-w-0 truncate text-base font-bold">Wording</span>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto">
        <SellableNounSetting shopId={shopId} words={words} onSaved={onSaved} onExpired={onExpired} />
      </main>
    </div>
  );
}
