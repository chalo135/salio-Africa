import { useState, useEffect } from "react";
import { API } from "./auth";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { apiFetch, problemText } from "./api";

// From GET /api/people. Never a password or a hash.
type Person = {
  userId: string;
  fullName: string;
  email: string;
  role: number; // 0 = Owner, 1 = Cashier
  isActive: boolean;
  mustChangePassword: boolean;
  isYou: boolean;
};

type Added = {
  person: Person;
  addedExistingAccount: boolean;
  temporaryPassword: string | null;
  message: string;
};

// A destructive action waiting for "yes", with the sentence that says what
// will happen.
type Pending = {
  label: string;
  sentence: string;
  path: string;
  method: "POST" | "PUT";
  body?: unknown;
};

const ROLES = [
  { value: 1, label: "Cashier" },
  { value: 0, label: "Owner" },
];

function roleName(role: number) {
  return role === 0 ? "Owner" : "Cashier";
}

// Who can sign in to this shop. Owner only: App.tsx shows it to an Owner
// from More, and every /api/people call refuses a Cashier as well.
export default function PeopleScreen({
  shopId,
  onBack,
  onExpired,
}: {
  shopId: string;
  onBack: () => void;
  onExpired: () => void;
}) {
  const [people, setPeople] = useState<Person[] | null>(null);
  const [loadProblem, setLoadProblem] = useState("");
  const [loadCount, setLoadCount] = useState(0);

  // ---- Adding someone ----
  const [adding, setAdding] = useState(false);
  const [newName, setNewName] = useState("");
  const [newEmail, setNewEmail] = useState("");
  const [newRole, setNewRole] = useState(1); // Cashier: the smaller of the two
  const [addBusy, setAddBusy] = useState(false);
  const [addProblem, setAddProblem] = useState("");
  const [added, setAdded] = useState<Added | null>(null);

  // ---- One person ----
  const [openId, setOpenId] = useState<string | null>(null);
  const [pending, setPending] = useState<Pending | null>(null);
  const [actionBusy, setActionBusy] = useState(false);
  const [actionProblem, setActionProblem] = useState("");
  const [actionDone, setActionDone] = useState("");

  useEffect(() => {
    let current = true;

    apiFetch(`${API}/api/people?organizationId=${shopId}`, { credentials: "include" })
      .then(async (response) => {
        if (!current) return;
        if (response.status === 401) {
          onExpired();
          return;
        }
        if (!response.ok) {
          setLoadProblem(await problemText(response));
          return;
        }
        setPeople(await response.json());
      })
      .catch(() => {
        if (current) setLoadProblem("Can't reach Salio.");
      });

    return () => {
      current = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, loadCount]);

  function reload() {
    setLoadProblem("");
    setLoadCount(loadCount + 1);
  }

  async function addPerson() {
    if (addBusy) {
      return;
    }
    if (newName.trim() === "" || !newEmail.includes("@")) {
      setAddProblem("Type a name and email.");
      return;
    }

    setAddBusy(true);
    setAddProblem("");

    try {
      const response = await apiFetch(`${API}/api/people`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          organizationId: shopId,
          fullName: newName.trim(),
          email: newEmail.trim(),
          role: newRole,
        }),
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

      setAdded(await response.json());
      setAdding(false);
      setNewName("");
      setNewEmail("");
      setNewRole(1);
      reload();
    } catch {
      setAddProblem("Can't reach Salio. Nobody added.");
    } finally {
      setAddBusy(false);
    }
  }

  async function confirmPending() {
    if (pending === null || actionBusy) {
      return;
    }

    setActionBusy(true);
    setActionProblem("");

    try {
      const response = await apiFetch(`${API}${pending.path}?organizationId=${shopId}`, {
        method: pending.method,
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: pending.body === undefined ? undefined : JSON.stringify(pending.body),
      });

      if (response.status === 401) {
        onExpired();
        return;
      }

      if (!response.ok) {
        // The guards answer in a sentence: say it.
        setActionProblem(await problemText(response));
        setPending(null);
        return;
      }

      setActionDone(pending.label + ": done.");
      setPending(null);
      reload();
    } catch {
      setActionProblem("Can't reach Salio. Nothing changed.");
    } finally {
      setActionBusy(false);
    }
  }

  const showLoading = useShowLoader(people === null && loadProblem === "");

  // ---------- the temporary password, shown once ----------
  if (added !== null) {
    // Covers the whole screen, the tablet's sidebar too, so a stray tap
    // cannot leave before it is written down.
    return (
      <div className="fixed inset-0 z-50 flex flex-col bg-background text-foreground">
        <main className="flex min-h-0 flex-1 flex-col items-center justify-center overflow-y-auto px-4 text-center">
          {added.temporaryPassword !== null ? (
            <>
              <p className="text-xl font-bold">{added.person.fullName}</p>
              <p className="mt-1 text-lg">Temporary password</p>
              {/* Large and alone. No copy button, and not made easy to
                  select: a password on the clipboard ends up pasted
                  somewhere it should not be. */}
              <p className="mt-6 break-all font-money text-2xl font-bold tracking-widest">
                {added.temporaryPassword}
              </p>
              <p className="mt-6 max-w-sm text-lg font-semibold">
                Write it down. Shown only once.
              </p>
            </>
          ) : (
            <p className="max-w-sm text-xl font-semibold">{added.message}</p>
          )}
        </main>
        <div className="shrink-0 p-4">
          <button
            onClick={() => setAdded(null)}
            className="h-16 w-full rounded-xl bg-primary text-2xl font-bold text-primary-foreground"
          >
            Done
          </button>
        </div>
      </div>
    );
  }

  const open = (people ?? []).find((p) => p.userId === openId) ?? null;

  // ---------- one person ----------
  if (open !== null) {
    const otherRole = open.role === 0 ? 1 : 0;

    return (
      <div className="flex h-full flex-col bg-background text-foreground">
        <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
          <button
            onClick={() => {
              setOpenId(null);
              setPending(null);
              setActionProblem("");
              setActionDone("");
            }}
            className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold"
          >
            ‹ People
          </button>
          <span className="min-w-0 truncate text-base font-bold">{open.fullName}</span>
        </div>

        <main className="min-h-0 flex-1 space-y-4 overflow-y-auto bg-surface p-4">
          <div>
            <p className="text-xl font-bold">
              {open.fullName}
              {open.isYou && <span className="ml-2 text-base font-semibold text-muted-foreground">(you)</span>}
            </p>
            <p className="text-base">{open.email}</p>
            <p className="mt-1 text-base font-semibold">{roleName(open.role)}</p>
            <PersonMarks person={open} />
          </div>

          {actionDone !== "" && (
            <p role="status" className="rounded-lg bg-primary-soft px-3 py-2 text-base font-semibold text-primary">
              {actionDone}
            </p>
          )}

          {actionProblem !== "" && (
            <p role="alert" className="rounded-lg bg-error px-3 py-2 text-base font-semibold text-error-foreground">
              {actionProblem}
            </p>
          )}

          {/* Asks first, in a sentence that says what will happen. */}
          {pending !== null ? (
            <div className="rounded-xl border-2 border-warning-strong p-3">
              <p className="text-lg font-semibold">{pending.sentence}</p>
              <div className="mt-3 grid grid-cols-2 gap-2">
                <button
                  onClick={() => setPending(null)}
                  className="h-14 rounded-xl border-2 border-border-strong text-lg font-bold"
                >
                  Cancel
                </button>
                <button
                  onClick={confirmPending}
                  disabled={actionBusy}
                  className="h-14 rounded-xl bg-primary text-lg font-bold text-primary-foreground disabled:opacity-40"
                >
                  {actionBusy ? "Working…" : "Yes, " + pending.label.toLowerCase()}
                </button>
              </div>
            </div>
          ) : (
            <div className="space-y-2">
              {/* What an owner may not do to themselves is not offered;
                  the server refuses it as well. */}
              {!(open.isYou && open.role === 0) && open.isActive && (
                <button
                  onClick={() => {
                    setActionDone("");
                    setActionProblem("");
                    setPending({
                      label: "Make " + roleName(otherRole),
                      sentence:
                        otherRole === 0
                          ? `${open.fullName} will see money and manage people.`
                          : `${open.fullName} will only sell. No money, no people.`,
                      path: `/api/people/${open.userId}/role`,
                      method: "PUT",
                      body: { role: otherRole },
                    });
                  }}
                  className="h-14 w-full rounded-xl border-2 border-primary text-lg font-bold text-primary"
                >
                  Make {roleName(otherRole)}
                </button>
              )}

              <button
                onClick={() => {
                  setActionDone("");
                  setActionProblem("");
                  setPending({
                    label: "Sign out devices",
                    sentence: open.isYou
                      ? "Signs out all your phones, this one too."
                      : `Signs out ${open.fullName} on every phone.`,
                    path: `/api/people/${open.userId}/sign-out-all-devices`,
                    method: "POST",
                  });
                }}
                className="h-14 w-full rounded-xl border-2 border-border-strong text-lg font-bold"
              >
                Sign out devices
              </button>

              {!open.isYou &&
                (open.isActive ? (
                  <button
                    onClick={() => {
                      setActionDone("");
                      setActionProblem("");
                      setPending({
                        label: "Switch off",
                        sentence: `${open.fullName} can't sign in. Their sales stay.`,
                        path: `/api/people/${open.userId}/deactivate`,
                        method: "POST",
                      });
                    }}
                    className="h-14 w-full rounded-xl border-2 border-error text-lg font-bold text-error"
                  >
                    Switch off
                  </button>
                ) : (
                  <button
                    onClick={() => {
                      setActionDone("");
                      setActionProblem("");
                      setPending({
                        label: "Switch on",
                        sentence: `${open.fullName} can sign in again, as ${roleName(open.role).toLowerCase()}.`,
                        path: `/api/people/${open.userId}/activate`,
                        method: "POST",
                      });
                    }}
                    className="h-14 w-full rounded-xl border-2 border-primary text-lg font-bold text-primary"
                  >
                    Switch on
                  </button>
                ))}

              {open.isYou && (
                <p className="text-base md:text-sm text-muted-foreground">
                  Another owner must change you
                </p>
              )}
            </div>
          )}
        </main>
      </div>
    );
  }

  // ---------- everyone ----------
  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface px-2 py-1.5">
        <button
          onClick={onBack}
          className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-sm font-bold md:hidden"
        >
          ‹ More
        </button>
        <span className="text-base font-bold">People</span>
      </div>

      <main className="min-h-0 flex-1 overflow-y-auto">
        <div className="border-b border-border bg-surface p-2">
          {!adding ? (
            <button
              onClick={() => {
                setAdding(true);
                setAddProblem("");
              }}
              className="h-14 w-full rounded-xl border-2 border-dashed border-primary text-lg font-bold text-primary"
            >
              + Add someone
            </button>
          ) : (
            <div className="space-y-2">
              <label className="block text-sm font-semibold">
                Name
                <input
                  value={newName}
                  onChange={(e) => {
                    setNewName(e.target.value);
                    setAddProblem("");
                  }}
                  maxLength={200}
                  className="mt-0.5 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
                    focus:border-primary focus:outline-none"
                />
              </label>
              <label className="block text-sm font-semibold">
                Email
                <input
                  type="email"
                  value={newEmail}
                  onChange={(e) => {
                    setNewEmail(e.target.value);
                    setAddProblem("");
                  }}
                  maxLength={256}
                  autoCapitalize="none"
                  autoCorrect="off"
                  className="mt-0.5 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
                    focus:border-primary focus:outline-none"
                />
              </label>
              <div className="grid grid-cols-2 gap-2">
                {ROLES.map((r) => (
                  <button
                    key={r.value}
                    type="button"
                    onClick={() => setNewRole(r.value)}
                    aria-pressed={newRole === r.value}
                    className={
                      "h-12 rounded-xl text-lg font-bold " +
                      (newRole === r.value
                        ? "bg-primary text-primary-foreground"
                        : "border-2 border-border-strong bg-surface")
                    }
                  >
                    {r.label}
                  </button>
                ))}
              </div>
              {addProblem !== "" && (
                <p role="alert" className="text-base font-semibold text-error">{addProblem}</p>
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
                  onClick={addPerson}
                  disabled={addBusy}
                  className="h-12 rounded-xl bg-primary text-base font-bold text-primary-foreground disabled:opacity-40"
                >
                  {addBusy ? "Adding…" : "Add"}
                </button>
              </div>
            </div>
          )}
        </div>

        {people === null && loadProblem === "" && showLoading && (
          <SalioLoader size="inline" message="Loading people…" />
        )}

        {loadProblem !== "" && (
          <div className="p-4">
            <p className="mb-3 text-base font-semibold text-error">{loadProblem}</p>
            <button
              onClick={reload}
              className="h-12 w-full rounded-xl bg-primary text-base font-bold text-primary-foreground"
            >
              Try again
            </button>
          </div>
        )}

        {(people ?? []).map((person) => (
          <button
            key={person.userId}
            onClick={() => {
              setOpenId(person.userId);
              setActionDone("");
              setActionProblem("");
            }}
            className="flex min-h-16 w-full items-center justify-between gap-3 border-b
              border-border bg-surface px-3 py-2 text-left active:bg-primary-soft"
          >
            <span className="min-w-0">
              <span
                className={
                  "block truncate text-base font-bold " +
                  (person.isActive ? "text-foreground" : "text-muted-foreground line-through")
                }
              >
                {person.fullName}
                {person.isYou && <span className="ml-1 font-semibold text-muted-foreground"> (you)</span>}
              </span>
              <span className="block truncate text-sm text-muted-foreground">{person.email}</span>
              <PersonMarks person={person} />
            </span>
            <span className="shrink-0 text-base font-semibold">{roleName(person.role)}</span>
          </button>
        ))}
      </main>
    </div>
  );
}

// The two things worth noticing at a glance, said in words.
function PersonMarks({ person }: { person: Person }) {
  return (
    <span className="mt-1 flex flex-wrap gap-1.5">
      {!person.isActive && (
        <span className="rounded bg-error px-1.5 py-0.5 text-sm font-bold text-error-foreground">
          Switched off
        </span>
      )}
      {person.mustChangePassword && (
        <span className="rounded bg-warning px-1.5 py-0.5 text-sm font-bold text-warning-foreground">
          No password yet
        </span>
      )}
    </span>
  );
}
