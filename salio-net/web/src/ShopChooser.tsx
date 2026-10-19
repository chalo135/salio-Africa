import type { Shop } from "./auth";

// Shown only when a person works at more than one shop. The server never
// picks for them.
export default function ShopChooser({
  shops,
  onChoose,
}: {
  shops: Shop[];
  onChoose: (organizationId: string) => void;
}) {
  return (
    <div className="flex min-h-dvh flex-col justify-center bg-background px-4 text-foreground">
      <div className="mx-auto w-full max-w-sm">
        <h1 className="mb-6 text-2xl font-bold">Which shop?</h1>

        {shops.map((shop) => (
          <button
            key={shop.organizationId}
            onClick={() => onChoose(shop.organizationId)}
            className="mb-3 flex min-h-14 w-full items-center justify-between gap-3
              rounded-xl border-2 border-border-strong bg-surface px-4 text-left"
          >
            <span className="text-lg font-semibold">{shop.name}</span>
            <span className="text-sm font-bold text-muted-foreground">
              {shop.role === 0 ? "Owner" : "Cashier"}
            </span>
          </button>
        ))}
      </div>
    </div>
  );
}
