import { useEffect, useRef, useState, type FormEvent } from "react";
import { API } from "./auth";
import { apiFetch } from "./api";

// The platform admin's page, at /admin. Reached only after the server said
// yes to GET /api/admin/session (Root.tsx); every call below is checked by
// the server again, which answers anyone else with a bare 404.
//
// Three things and nothing more: issue a setup code, list the codes (and
// revoke an unused one), list the shops. It never shows a shop's sales
// figures, books, stock, profit, tax or customers, and it cannot change
// anything inside a shop or sign in as anyone.

type IssuedCode = {
  id: string;
  code: string;
  link: string;
  expiresAt: string;
  message: string;
};

type SetupCodeRow = {
  id: string;
  code: string;
  shopName: string;
  email: string;
  issuedAt: string;
  expiresAt: string;
  redeemedAt: string | null;
  revokedAt: string | null;
  status: "Unused" | "Redeemed" | "Expired" | "Revoked";
  organizationId: string | null;
};

type ShopRow = {
  organizationId: string;
  name: string;
  ownerEmail: string | null;
  createdAt: string;
  lastSaleAt: string | null;
};

// "K7MP29" -> "K7M-P29". The server already sends the dash; this is a guard.
function dashed(code: string) {
  return code.length === 6 && !code.includes("-") ? code.slice(0, 3) + "-" + code.slice(3) : code;
}

function dateOnly(iso: string) {
  return new Date(iso).toLocaleDateString("en-KE", { day: "numeric", month: "short", year: "numeric" });
}

function dateAndTime(iso: string) {
  return new Date(iso).toLocaleString("en-KE", {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

// The server's own words when it gave any.
async function detailOf(response: Response, fallback: string) {
  const body = await response.json().catch(() => null);
  return typeof body?.detail === "string" && body.detail !== "" ? body.detail : fallback;
}

// Copies to the clipboard. Where the browser does not allow that (an http
// address that is not localhost), the text is selected instead so Ctrl+C
// or a long press copies it.
async function copyText(text: string, box: HTMLInputElement | HTMLTextAreaElement | null) {
  try {
    await navigator.clipboard.writeText(text);
    return "Copied";
  } catch {
    if (box) {
      box.focus();
      box.select();
      try {
        if (document.execCommand("copy")) {
          return "Copied";
        }
      } catch {
        // Fall through: the text is selected.
      }
    }
    return "Selected: press Ctrl+C";
  }
}

// One of the two lists, or what to say instead.
async function getList<T>(path: string, what: string): Promise<{ ok: true; rows: T[] } | { ok: false; problem: string }> {
  try {
    const response = await apiFetch(`${API}/api/admin/${path}`, { credentials: "include" });
    if (!response.ok) {
      return { ok: false, problem: `The ${what} did not load. Reload the page; if it shows Page not found, sign in again.` };
    }
    return { ok: true, rows: await response.json() };
  } catch {
    return { ok: false, problem: "Can't reach Salio. Check the connection, then reload." };
  }
}

const sectionClass = "rounded-xl border border-border bg-surface p-4 md:p-5";
const inputClass =
  "mt-1 h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg " +
  "focus:border-primary focus:outline-none";
const smallButton =
  "min-h-11 shrink-0 rounded-lg border-2 border-primary px-3 text-base font-bold text-primary disabled:opacity-40";
const th = "whitespace-nowrap px-3 py-2 text-left text-sm font-bold text-muted-foreground";
const td = "whitespace-nowrap px-3 py-2 text-base";

// A Kenyan mobile number in the form wa.me wants: country code, no plus, no
// leading zero. People type it four different ways, so accept all of them.
// Returns null if it is not a Kenyan mobile, and the caller stays disabled.
function kenyanWhatsAppNumber(typed: string): string | null {
  const digits = typed.replace(/\D/g, "");

  if (digits.length === 12 && digits.startsWith("254")) {
    return digits;
  }

  // 0712345678
  if (digits.length === 10 && digits.startsWith("0")) {
    return "254" + digits.slice(1);
  }

  // 712345678
  if (digits.length === 9 && (digits.startsWith("7") || digits.startsWith("1"))) {
    return "254" + digits;
  }

  return null;
}

// Opens WhatsApp with the message already typed to one person. It does not
// send by itself: that needs Meta's API, a verified business and approved
// templates, which is a lot of machinery for a link sent once per shop.
// The number is used here and nowhere else - it is never sent to Salio.
function SendOnWhatsApp({ message }: { message: string }) {
  const [typed, setTyped] = useState("");
  const number = kenyanWhatsAppNumber(typed);

  return (
    <div className="mt-4">
      <label htmlFor="wa-number" className="text-base font-bold">
        Send on WhatsApp
      </label>
      <div className="mt-1 flex items-start gap-2">
        <input
          id="wa-number"
          type="tel"
          inputMode="tel"
          value={typed}
          onChange={(e) => setTyped(e.target.value)}
          placeholder="0712345678"
          autoComplete="off"
          className={inputClass}
        />
        {number === null ? (
          <span
            aria-disabled="true"
            className="flex h-12 shrink-0 items-center rounded-lg border-2 border-border px-5 text-base font-bold text-muted-foreground"
          >
            Open
          </span>
        ) : (
          <a
            href={"https://wa.me/" + number + "?text=" + encodeURIComponent(message)}
            target="_blank"
            rel="noopener noreferrer"
            className="flex h-12 shrink-0 items-center rounded-lg bg-primary px-5 text-base font-bold text-primary-foreground"
          >
            Open
          </a>
        )}
      </div>
      <p className="mt-1 text-sm text-muted-foreground">
        {typed !== "" && number === null
          ? "That is not a Kenyan mobile number. Try 0712345678."
          : "Opens WhatsApp with the message typed. You press send."}
      </p>
    </div>
  );
}

// One copyable value: the box, and a Copy button that says what happened.
function CopyField({ label, value, multiline, big }: { label: string; value: string; multiline?: boolean; big?: boolean }) {
  const box = useRef<HTMLInputElement & HTMLTextAreaElement>(null);
  const [said, setSaid] = useState("");

  async function copy() {
    setSaid(await copyText(value, box.current));
    window.setTimeout(() => setSaid(""), 2500);
  }

  return (
    <div className="mt-4">
      <div className="flex items-end justify-between gap-3">
        <span className="text-base font-bold">{label}</span>
        <span className="flex items-center gap-2">
          {said && (
            <span role="status" className="text-sm font-semibold text-primary">
              {said}
            </span>
          )}
          <button type="button" onClick={copy} className={smallButton} aria-label={"Copy " + label.toLowerCase()}>
            Copy
          </button>
        </span>
      </div>
      {multiline ? (
        <textarea
          ref={box}
          readOnly
          value={value}
          rows={6}
          className="mt-1 w-full resize-y rounded-lg border-2 border-border bg-background px-3 py-2 text-base"
        />
      ) : (
        <input
          ref={box}
          readOnly
          value={value}
          className={
            "mt-1 w-full rounded-lg border-2 border-border bg-background px-3 " +
            (big ? "h-16 font-money text-2xl font-bold tracking-widest" : "h-12 text-base")
          }
        />
      )}
    </div>
  );
}

export default function AdminScreen() {
  const [shopName, setShopName] = useState("");
  const [email, setEmail] = useState("");
  const [issuing, setIssuing] = useState(false);
  const [issueProblem, setIssueProblem] = useState("");
  const [issued, setIssued] = useState<IssuedCode | null>(null);

  const [codes, setCodes] = useState<SetupCodeRow[] | null>(null);
  const [codesProblem, setCodesProblem] = useState("");
  const [shops, setShops] = useState<ShopRow[] | null>(null);
  const [shopsProblem, setShopsProblem] = useState("");

  // The row whose Revoke was tapped once, waiting for "Yes, revoke".
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [revoking, setRevoking] = useState(false);
  const [revokeProblem, setRevokeProblem] = useState("");

  useEffect(() => {
    document.title = "Salio admin";
  }, []);

  function loadCodes() {
    return getList<SetupCodeRow>("setup-codes", "codes").then((result) => {
      if (result.ok) {
        setCodes(result.rows);
      }
      setCodesProblem(result.ok ? "" : result.problem);
    });
  }

  function loadShops() {
    return getList<ShopRow>("shops", "shops").then((result) => {
      if (result.ok) {
        setShops(result.rows);
      }
      setShopsProblem(result.ok ? "" : result.problem);
    });
  }

  useEffect(() => {
    loadCodes();
    loadShops();
    // Once, on arrival.
  }, []);

  async function issue(event: FormEvent) {
    event.preventDefault();
    if (issuing) {
      return;
    }
    if (shopName.trim() === "" || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
      setIssueProblem("Type the shop's name and a full email address.");
      return;
    }

    setIssuing(true);
    setIssueProblem("");
    try {
      const response = await apiFetch(`${API}/api/admin/setup-codes`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ shopName: shopName.trim(), email: email.trim() }),
      });
      if (!response.ok) {
        setIssueProblem(await detailOf(response, "No code was issued. Reload the page and try again."));
        return;
      }
      setIssued(await response.json());
      setShopName("");
      setEmail("");
      loadCodes();
    } catch {
      setIssueProblem("Can't reach Salio. No code was issued.");
    } finally {
      setIssuing(false);
    }
  }

  async function revoke(id: string) {
    if (revoking) {
      return;
    }
    setRevoking(true);
    setRevokeProblem("");
    try {
      const response = await apiFetch(`${API}/api/admin/setup-codes/${id}/revoke`, {
        method: "POST",
        credentials: "include",
      });
      if (!response.ok) {
        setRevokeProblem(await detailOf(response, "The code was not revoked."));
      }
      setConfirmingId(null);
      await loadCodes();
    } catch {
      setRevokeProblem("Can't reach Salio. The code was not revoked.");
    } finally {
      setRevoking(false);
    }
  }

  async function signOut() {
    try {
      await apiFetch(`${API}/api/auth/logout`, { method: "POST", credentials: "include" });
    } finally {
      window.location.assign("/");
    }
  }

  return (
    <div className="min-h-full bg-background text-foreground">
      <header className="bg-primary text-primary-foreground">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-3 px-4 py-3">
          <h1 className="text-xl font-bold">Salio admin</h1>
          <button
            type="button"
            onClick={signOut}
            className="min-h-11 shrink-0 rounded-lg border-2 border-primary-foreground-soft px-3 text-base font-bold"
          >
            Sign out
          </button>
        </div>
      </header>

      <main className="mx-auto max-w-5xl space-y-5 px-4 py-5">
        {/* ---------- Issue a setup code ---------- */}
        <section className={sectionClass} aria-labelledby="issue-heading">
          <h2 id="issue-heading" className="text-lg font-bold">
            New code
          </h2>
          <form noValidate onSubmit={issue} className="mt-3 grid gap-3 md:grid-cols-[1fr_1fr_auto] md:items-end">
            <label className="block text-base font-semibold">
              Shop name
              <input
                value={shopName}
                onChange={(e) => setShopName(e.target.value)}
                maxLength={200}
                autoComplete="off"
                className={inputClass}
              />
            </label>
            <label className="block text-base font-semibold">
              Owner's email
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                maxLength={256}
                autoComplete="off"
                autoCapitalize="none"
                className={inputClass}
              />
            </label>
            <button
              type="submit"
              disabled={issuing}
              className="h-12 rounded-lg bg-primary px-6 text-lg font-bold text-primary-foreground disabled:opacity-40"
            >
              {issuing ? "Issuing…" : "Issue code"}
            </button>
          </form>
          {issueProblem && (
            <p role="alert" className="mt-2 text-base font-semibold text-error">
              {issueProblem}
            </p>
          )}

          {issued && (
            <div className="mt-4 rounded-xl border-2 border-primary bg-primary-soft p-4">
              <p className="text-base font-semibold">
                Issued. Expires {dateAndTime(issued.expiresAt)}. Send it by hand: Salio sends nothing.
              </p>
              <CopyField label="Code" value={dashed(issued.code)} big />
              <CopyField label="Link" value={issued.link} />
              <CopyField label="WhatsApp message" value={issued.message} multiline />
              <SendOnWhatsApp message={issued.message} />
            </div>
          )}
        </section>

        {/* ---------- Setup codes ---------- */}
        <section className={sectionClass} aria-labelledby="codes-heading">
          <h2 id="codes-heading" className="text-lg font-bold">
            Setup codes
          </h2>
          {codesProblem && <p className="mt-2 text-base font-semibold text-error">{codesProblem}</p>}
          {revokeProblem && (
            <p role="alert" className="mt-2 text-base font-semibold text-error">
              {revokeProblem}
            </p>
          )}
          {codes === null && !codesProblem && <p className="mt-2 text-base text-muted-foreground">Loading…</p>}
          {codes !== null && codes.length === 0 && (
            <p className="mt-2 text-base text-muted-foreground">No codes issued yet.</p>
          )}
          {codes !== null && codes.length > 0 && (
            // Wide tables scroll inside this box, never the page. "relative"
            // keeps the hidden "Action" heading (sr-only, absolute) inside it.
            <div className="relative mt-3 overflow-x-auto rounded-lg border border-border">
              <table className="w-full border-collapse">
                <thead className="bg-background">
                  <tr>
                    <th className={th}>Code</th>
                    <th className={th}>Shop name</th>
                    <th className={th}>Email</th>
                    <th className={th}>Issued</th>
                    <th className={th}>Expires</th>
                    <th className={th}>Status</th>
                    <th className={th}>
                      <span className="sr-only">Action</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {codes.map((row) => (
                    <tr key={row.id} className="border-t border-border">
                      <td className={td + " font-money font-bold tracking-wider"}>{dashed(row.code)}</td>
                      <td className={td}>{row.shopName}</td>
                      <td className={td}>{row.email}</td>
                      <td className={td}>{dateAndTime(row.issuedAt)}</td>
                      <td className={td}>{dateAndTime(row.expiresAt)}</td>
                      <td className={td}>
                        <span
                          className={
                            "rounded px-1.5 py-0.5 text-sm font-bold " +
                            (row.status === "Unused"
                              ? "bg-primary-soft text-primary"
                              : row.status === "Redeemed"
                                ? "bg-primary text-primary-foreground"
                                : "bg-background text-muted-foreground")
                          }
                        >
                          {row.status}
                        </span>
                        {row.status === "Redeemed" && row.redeemedAt && (
                          <span className="ml-2 text-sm text-muted-foreground">{dateOnly(row.redeemedAt)}</span>
                        )}
                      </td>
                      <td className={td + " text-right"}>
                        {row.status === "Unused" &&
                          (confirmingId === row.id ? (
                            <span className="flex items-center justify-end gap-2">
                              <span className="text-sm font-semibold">Revoke {dashed(row.code)}?</span>
                              <button
                                type="button"
                                disabled={revoking}
                                onClick={() => revoke(row.id)}
                                className="min-h-11 rounded-lg bg-error px-3 text-base font-bold text-error-foreground disabled:opacity-40"
                              >
                                {revoking ? "Revoking…" : "Yes, revoke"}
                              </button>
                              <button
                                type="button"
                                disabled={revoking}
                                onClick={() => setConfirmingId(null)}
                                className="min-h-11 rounded-lg border-2 border-border-strong px-3 text-base font-bold disabled:opacity-40"
                              >
                                Keep
                              </button>
                            </span>
                          ) : (
                            <button
                              type="button"
                              onClick={() => {
                                setRevokeProblem("");
                                setConfirmingId(row.id);
                              }}
                              className="min-h-11 rounded-lg border-2 border-error px-3 text-base font-bold text-error"
                            >
                              Revoke
                            </button>
                          ))}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        {/* ---------- Shops: read only ---------- */}
        <section className={sectionClass} aria-labelledby="shops-heading">
          <h2 id="shops-heading" className="text-lg font-bold">
            Shops
          </h2>
          {shopsProblem && <p className="mt-2 text-base font-semibold text-error">{shopsProblem}</p>}
          {shops === null && !shopsProblem && <p className="mt-2 text-base text-muted-foreground">Loading…</p>}
          {shops !== null && shops.length === 0 && (
            <p className="mt-2 text-base text-muted-foreground">No shops yet.</p>
          )}
          {shops !== null && shops.length > 0 && (
            <div className="relative mt-3 overflow-x-auto rounded-lg border border-border">
              <table className="w-full border-collapse">
                <thead className="bg-background">
                  <tr>
                    <th className={th}>Name</th>
                    <th className={th}>Owner email</th>
                    <th className={th}>Created</th>
                    <th className={th}>Last sale</th>
                  </tr>
                </thead>
                <tbody>
                  {shops.map((shop) => (
                    <tr key={shop.organizationId} className="border-t border-border">
                      <td className={td + " font-semibold"}>{shop.name}</td>
                      <td className={td}>{shop.ownerEmail ?? "—"}</td>
                      <td className={td}>{dateOnly(shop.createdAt)}</td>
                      <td className={td + (shop.lastSaleAt ? "" : " text-muted-foreground")}>
                        {shop.lastSaleAt ? dateOnly(shop.lastSaleAt) : "No sales yet"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </main>
    </div>
  );
}
