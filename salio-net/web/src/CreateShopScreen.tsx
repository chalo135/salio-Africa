import { useState } from "react";
import { API } from "./auth";

// Locked rule 14: the developer creates each shop and its first Owner by
// hand, and hands it over. This is that job with a form instead of curl, and
// it is NOT self-signup: the endpoint behind it answers 404 outside
// Development, and the link to this screen only exists in `npm run dev`.
type Created = {
  organizationId: string;
  shopName: string;
  ownerId: string;
  ownerEmail: string;
  accountsCreated: number;
};

export default function CreateShopScreen({ onBack }: { onBack: () => void }) {
  const [shopName, setShopName] = useState("");
  const [ownerFullName, setOwnerFullName] = useState("");
  const [ownerEmail, setOwnerEmail] = useState("");
  const [ownerPassword, setOwnerPassword] = useState("");

  const [created, setCreated] = useState<Created | null>(null);
  const [problem, setProblem] = useState("");
  const [busy, setBusy] = useState(false);

  const ready =
    shopName !== "" &&
    ownerFullName !== "" &&
    ownerEmail !== "" &&
    ownerPassword.length >= 8;

  async function createShop(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setProblem("");

    try {
      const response = await fetch(`${API}/api/setup/organization`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ shopName, ownerFullName, ownerEmail, ownerPassword }),
      });

      if (response.ok) {
        setCreated(await response.json());
        return;
      }

      if (response.status === 404) {
        setProblem("This only works in Development. The API refuses it anywhere else.");
        return;
      }

      // The server's own words: a taken email, a short password.
      const body = await response.json().catch(() => null);
      setProblem(body?.detail ?? "Could not create the shop.");
    } catch {
      setProblem("Could not reach the till server. Is the API running?");
    } finally {
      setBusy(false);
    }
  }

  const field =
    "mb-4 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg";

  if (created !== null) {
    return (
      <div className="flex min-h-dvh flex-col justify-center bg-background px-4 text-foreground">
        <div className="mx-auto w-full max-w-sm">
          <h1 className="mb-4 text-2xl font-bold">{created.shopName} is ready</h1>

          <p className="mb-2 text-base">
            Owner: <span className="font-semibold">{created.ownerEmail}</span>
          </p>
          <p className="mb-2 text-base text-muted-foreground">
            {created.accountsCreated} accounts in its chart of accounts, and its Turnover
            Tax rate, were created with it.
          </p>
          <p className="mb-6 text-base text-muted-foreground">
            The password is not shown again. Sign in with it to check it works.
          </p>

          <button
            onClick={onBack}
            className="h-14 w-full rounded-xl bg-primary text-xl font-bold text-primary-foreground"
          >
            Go to sign in
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex min-h-dvh flex-col justify-center bg-background px-4 text-foreground">
      <form onSubmit={createShop} className="mx-auto w-full max-w-sm py-6">
        <h1 className="mb-1 text-2xl font-bold">Create a shop</h1>
        <p className="mb-6 text-sm text-muted-foreground">
          Development only. Installing a customer by hand.
        </p>

        <label className="mb-1 block text-sm font-semibold" htmlFor="shopName">
          Shop name
        </label>
        <input
          id="shopName"
          value={shopName}
          onChange={(e) => setShopName(e.target.value)}
          maxLength={200}
          className={field}
        />

        <label className="mb-1 block text-sm font-semibold" htmlFor="ownerFullName">
          Owner's name
        </label>
        <input
          id="ownerFullName"
          value={ownerFullName}
          onChange={(e) => setOwnerFullName(e.target.value)}
          maxLength={200}
          className={field}
        />

        <label className="mb-1 block text-sm font-semibold" htmlFor="ownerEmail">
          Owner's email
        </label>
        <input
          id="ownerEmail"
          type="email"
          value={ownerEmail}
          onChange={(e) => setOwnerEmail(e.target.value)}
          maxLength={256}
          // Email is matched exactly as created, and a phone keyboard
          // capitalises the first letter by default.
          autoCapitalize="none"
          autoCorrect="off"
          className={field}
        />

        <label className="mb-1 block text-sm font-semibold" htmlFor="ownerPassword">
          Owner's password
        </label>
        <input
          id="ownerPassword"
          type="password"
          value={ownerPassword}
          onChange={(e) => setOwnerPassword(e.target.value)}
          autoComplete="new-password"
          className={field}
        />
        <p className="mb-4 -mt-3 text-sm text-muted-foreground">
          At least 8 characters. Nothing else is required.
        </p>

        {problem !== "" && (
          <p className="mb-4 text-base font-semibold text-error">{problem}</p>
        )}

        <button
          type="submit"
          disabled={busy || !ready}
          className="mb-3 h-14 w-full rounded-xl bg-primary text-xl font-bold
            text-primary-foreground disabled:opacity-40"
        >
          {busy ? "Creating…" : "Create shop"}
        </button>

        <button
          type="button"
          onClick={onBack}
          className="min-h-11 w-full rounded-lg border-2 border-border-strong text-base font-bold"
        >
          Back to sign in
        </button>
      </form>
    </div>
  );
}
