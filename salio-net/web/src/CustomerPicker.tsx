import { useState, useEffect } from "react";
import { API, kes } from "./auth";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { apiFetch, problemText } from "./api";

export type Customer = {
  id: string;
  name: string;
  phone: string;
  balanceMinor: number; // what they owe now, worked out by the server
};

// Who is taking the goods on credit. Opens from the till's Credit button.
// What the customer already owes is shown for the cashier to weigh up, never
// as a block: the cashier decides.
export default function CustomerPicker({
  shopId,
  totalMinor,
  onCancel,
  onConfirm,
  onExpired,
}: {
  shopId: string;
  totalMinor: number; // this sale
  onCancel: () => void;
  onConfirm: (customer: Customer) => void;
  onExpired: () => void;
}) {
  const [customers, setCustomers] = useState<Customer[] | null>(null);
  const [loadProblem, setLoadProblem] = useState("");
  const [attempt, setAttempt] = useState(0);

  const [search, setSearch] = useState("");
  const [chosen, setChosen] = useState<Customer | null>(null);

  const [adding, setAdding] = useState(false);
  const [newName, setNewName] = useState("");
  const [newPhone, setNewPhone] = useState("");
  const [addProblem, setAddProblem] = useState("");
  const [addBusy, setAddBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;

    apiFetch(`${API}/api/customers?organizationId=${shopId}`, { credentials: "include" })
      .then(async (response) => {
        if (cancelled) return;
        if (response.status === 401) {
          onExpired();
          return;
        }
        if (!response.ok) {
          setLoadProblem(await problemText(response));
          return;
        }
        setCustomers(await response.json());
      })
      .catch(() => {
        if (!cancelled) setLoadProblem("Can't reach Salio");
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, attempt]);

  const q = search.trim().toLowerCase();
  const matches = (customers ?? []).filter(
    (c) => q === "" || c.name.toLowerCase().includes(q) || c.phone.includes(q),
  );

  async function addCustomer() {
    if (newName.trim() === "" || newPhone.trim() === "") {
      setAddProblem("Name and phone needed");
      return;
    }

    setAddBusy(true);
    setAddProblem("");

    try {
      const response = await apiFetch(`${API}/api/customers`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ organizationId: shopId, name: newName.trim(), phone: newPhone.trim() }),
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        // What was typed stays, so it can be corrected.
        setAddProblem(await problemText(response));
        return;
      }

      const created: Customer = await response.json();
      setCustomers([...(customers ?? []), created]);
      setAdding(false);
      setNewName("");
      setNewPhone("");
      setChosen(created);
    } catch {
      setAddProblem("Can't reach Salio. Not added.");
    } finally {
      setAddBusy(false);
    }
  }

  const showLoading = useShowLoader(customers === null && loadProblem === "");

  // ---------- a customer is chosen: confirm ----------
  if (chosen !== null) {
    return (
      <div className="flex h-full flex-col bg-background text-foreground">
        <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
          <button
            onClick={() => setChosen(null)}
            className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold"
          >
            ‹ Back
          </button>
          <span className="text-base font-bold">On credit</span>
        </div>

        <main className="min-h-0 flex-1 overflow-y-auto bg-surface p-4">
          <p className="text-xl font-bold">{chosen.name}</p>
          <p className="text-base">{chosen.phone}</p>

          {/* Information, not a block. */}
          <div
            className={
              "mt-4 rounded-xl px-3 py-3 " +
              (chosen.balanceMinor > 0
                ? "bg-warning text-warning-foreground"
                : "border-2 border-border")
            }
          >
            <div className="flex items-baseline justify-between">
              <span className="text-base font-semibold">Already owes</span>
              <span className="font-money text-xl font-bold">KES {kes(chosen.balanceMinor)}</span>
            </div>
          </div>

          <div className="mt-3 flex items-baseline justify-between px-3">
            <span className="text-base">This sale</span>
            <span className="font-money text-lg font-semibold">KES {kes(totalMinor)}</span>
          </div>
          <div className="mt-1 flex items-baseline justify-between border-t border-border px-3 pt-1">
            <span className="text-base font-semibold">Will owe</span>
            <span className="font-money text-2xl font-bold">
              KES {kes(chosen.balanceMinor + totalMinor)}
            </span>
          </div>
        </main>

        <div className="shrink-0 space-y-2 border-t-2 border-border bg-surface p-2">
          <button
            onClick={() => onConfirm(chosen)}
            className="h-14 w-full rounded-xl bg-primary text-xl font-bold text-primary-foreground"
          >
            Credit to {chosen.name}
          </button>
          <button
            onClick={onCancel}
            className="h-12 w-full rounded-xl border-2 border-border-strong text-base font-bold"
          >
            Cancel
          </button>
        </div>
      </div>
    );
  }

  // ---------- who is it? ----------
  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onCancel}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold"
        >
          ‹ Till
        </button>
        <span className="text-base font-bold">Which customer?</span>
      </div>

      <header className="shrink-0 border-b border-border bg-surface p-2">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search customers"
          className="h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
            placeholder:text-muted-foreground focus:border-primary focus:outline-none"
        />
      </header>

      <main className="min-h-0 flex-1 overflow-y-auto">
        {customers === null && loadProblem === "" && showLoading && (
          <SalioLoader size="inline" message="Loading…" />
        )}

        {loadProblem !== "" && (
          <div className="p-4">
            <p className="mb-3 text-base font-semibold text-error">{loadProblem}</p>
            <button
              onClick={() => {
                setLoadProblem("");
                setAttempt(attempt + 1);
              }}
              className="h-12 w-full rounded-xl bg-primary text-base font-bold text-primary-foreground"
            >
              Try again
            </button>
          </div>
        )}

        {/* A new customer, without leaving the till. */}
        {customers !== null && (
          <div className="border-b border-border bg-surface p-2">
            {!adding ? (
              <button
                onClick={() => {
                  setAdding(true);
                  setAddProblem("");
                  // Whatever was searched for is probably the new name.
                  if (/^\d/.test(search.trim())) {
                    setNewPhone(search.trim());
                  } else {
                    setNewName(search.trim());
                  }
                }}
                className="min-h-12 w-full rounded-xl border-2 border-dashed border-primary text-base font-bold text-primary"
              >
                New customer
              </button>
            ) : (
              <div className="space-y-2">
                <input
                  value={newName}
                  onChange={(e) => {
                    setNewName(e.target.value);
                    setAddProblem("");
                  }}
                  maxLength={200}
                  placeholder="Name"
                  className="h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
                    placeholder:text-muted-foreground focus:border-primary focus:outline-none"
                />
                <input
                  value={newPhone}
                  onChange={(e) => {
                    setNewPhone(e.target.value);
                    setAddProblem("");
                  }}
                  type="tel"
                  inputMode="tel"
                  maxLength={30}
                  placeholder="Phone"
                  className="h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
                    placeholder:text-muted-foreground focus:border-primary focus:outline-none"
                />
                {addProblem !== "" && (
                  <p className="text-base font-semibold text-error">{addProblem}</p>
                )}
                <div className="grid grid-cols-2 gap-2">
                  <button
                    onClick={() => {
                      setAdding(false);
                      setAddProblem("");
                    }}
                    className="h-12 rounded-xl border-2 border-border-strong text-base font-bold"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={addCustomer}
                    disabled={addBusy}
                    className="h-12 rounded-xl bg-primary text-base font-bold text-primary-foreground disabled:opacity-40"
                  >
                    {addBusy ? "Adding…" : "Add"}
                  </button>
                </div>
              </div>
            )}
          </div>
        )}

        {matches.map((customer) => (
          <button
            key={customer.id}
            onClick={() => setChosen(customer)}
            className="flex min-h-14 w-full items-center justify-between gap-3 border-b
              border-border bg-surface px-3 py-2 text-left active:bg-primary-soft"
          >
            <span className="min-w-0">
              <span className="block truncate text-base font-semibold">{customer.name}</span>
              <span className="text-sm text-muted-foreground">{customer.phone}</span>
            </span>
            <span className="shrink-0 text-right">
              {customer.balanceMinor > 0 ? (
                <>
                  <span className="block text-sm font-semibold text-warning-strong">owes</span>
                  <span className="font-money text-base font-bold">{kes(customer.balanceMinor)}</span>
                </>
              ) : (
                <span className="text-sm text-muted-foreground">owes nothing</span>
              )}
            </span>
          </button>
        ))}

        {customers !== null && matches.length === 0 && (
          <p className="p-4 text-base md:text-sm text-muted-foreground">
            {q === "" ? "No customers yet" : "No match"}
          </p>
        )}
      </main>
    </div>
  );
}
