import { useEffect, useState } from "react";
import { API, itemWords, kes, type LoginResult, type SignedInUser } from "./auth";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { apiFetch } from "./api";

// A stranger's first two minutes with Salio: setting up their own shop.
//
// The questions: the shop's name, what kind of business it is, what it deals
// in, and the owner. Then the shop is created, with a progress bar that moves
// only when a real step has finished. A Sales shop then gets the chance to
// add its first few things; a Services shop goes straight to the till.
//
// - The setup code normally arrives in the link (/setup?code=...). It is
//   checked quietly and the code box never shows, unless the server turns
//   the code down. /setup with no code starts on the code box.
// - The client is never asked what Salio should call what they sell. The
//   word comes from their trade, by the table below. The owner can change it
//   later (More, or the list of what they sell).
// - Back always works until the shop exists, and nothing is lost: the
//   answers live in `draft`, outside the screen.
// - Every field starts neutral. A problem shows only once the field has been
//   left, or Continue has been pressed on that step.
// - Tax is not asked here. A guess made ninety seconds in would become a
//   stored fact behind a KRA figure months later, so "not sure" is sent and
//   the question belongs at the first month end.

type Kind = "Sales" | "Services";

const SOMETHING_ELSE = "Something else";

// What each trade calls the things it sells. The client never sees this.
const SALES_TRADES: { label: string; noun: string }[] = [
  { label: "Motorcycle or car spare parts", noun: "Parts" },
  { label: "Hardware and building materials", noun: "Items" },
  { label: "Clothes, shoes or fabrics", noun: "Goods" },
  { label: "Phones, electronics and accessories", noun: "Items" },
  { label: "Chemist or agrovet", noun: "Products" },
  { label: "Groceries, general shop or supermarket", noun: "Goods" },
];

const SERVICES_TRADES = [
  "Salon, barber or beauty",
  "Laundry or dry cleaning",
  "Garage, repairs or fundi work",
  "Tailoring",
  "Cyber, printing or photocopy",
];

function nounFor(kind: Kind | "", tradeChoice: string) {
  if (kind === "Services") {
    return "Services";
  }
  return SALES_TRADES.find((t) => t.label === tradeChoice)?.noun ?? "Goods";
}

// The server's refusal for a wrong, used or expired code. Matched so the
// code box can come back with the message under it.
const CODE_REFUSED_START = "That setup code does not work";

// The five things that happen after "Create my shop", in order. Each one is
// a real call; the bar moves a fifth when it comes back OK.
function progressSteps(shopName: string) {
  return [
    "Checking code",
    `Creating ${shopName}`,
    "Signing in",
    "Opening shop",
    "Preparing till",
  ];
}

// One row on the last step: something the owner sells. Numbers are kept as
// the text typed, so "2." mid-edit stays on screen. productId is set once the
// server has created it, so a retry never creates it twice.
type StarterRow = {
  key: number;
  add: boolean; // the tick: only ticked rows are saved
  name: string;
  priceText: string; // selling price, shillings
  countText: string; // how many on the shelf; blank is allowed
  paidText: string; // what one cost the shop, shillings; needed with a count
  productId: string | null;
};

function blankRow(key: number): StarterRow {
  return { key, add: false, name: "", priceText: "", countText: "", paidText: "", productId: null };
}

type Step = "code" | "name" | "kind" | "trade" | "owner" | "creating" | "first";

type Draft = {
  step: Step;
  code: string;
  shopName: string;
  kind: Kind | "";
  tradeChoice: string; // a label from the list, or SOMETHING_ELSE
  tradeOther: string; // typed under "Something else"
  fullName: string;
  email: string;
  password: string;
  // How many of the five progress steps have finished, 0 to 5.
  done: number;
  organizationId: string;
  login: LoginResult | null;
  // The last step (Sales only).
  rows: StarterRow[];
  nextRowKey: number;
  nextSkuNumber: number;
  stockSaved: boolean;
};

function emptyDraft(): Draft {
  return {
    step: "code", code: "", shopName: "", kind: "", tradeChoice: "", tradeOther: "",
    fullName: "", email: "", password: "", done: 0, organizationId: "", login: null,
    rows: [blankRow(1), blankRow(2), blankRow(3)], nextRowKey: 4, nextSkuNumber: 1, stockSaved: false,
  };
}

let draft: Draft = emptyDraft();

// True once the link's code has been sent to the server, so React's double
// start-up in development does not check it twice.
let linkCodeSent = false;

// The only writer of `draft`. Called from event handlers, never while
// drawing the screen.
function rememberDraft(next: Draft) {
  draft = next;
}

type Field = "code" | "shopName" | "tradeOther" | "fullName" | "email" | "password";

// Which step each field is on, so Continue can wake up that step's fields.
const STEP_OF: Record<Field, Step> = {
  code: "code",
  shopName: "name",
  tradeOther: "trade",
  fullName: "owner",
  email: "owner",
  password: "owner",
};

// The setup code: 6 letters and numbers. Spaces and dashes do not count,
// the same as on the server.
function codeProblem(code: string) {
  const kept = code.trim().replace(/[-\s]/g, "");
  if (kept === "") {
    return "Type the setup code.";
  }
  if (!/^[a-zA-Z0-9]{6}$/.test(kept)) {
    return "6 letters and numbers, like K7M-P29";
  }
  return "";
}

// Shape only: something@something.something.
function emailProblem(email: string) {
  const trimmed = email.trim();
  if (trimmed === "") {
    return "Type your email.";
  }
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed)) {
    return "Check the email";
  }
  return "";
}

// Four segments and a word. Advice only: the server's one rule is 8
// characters, and that is the only thing that stops Continue.
function strengthOf(password: string): { filled: number; word: string; tone: "error" | "warning" | "good" } {
  if (password.length < 8) {
    return { filled: 1, word: "Too short", tone: "error" };
  }

  let kinds = 0;
  if (/[a-z]/.test(password)) kinds++;
  if (/[A-Z]/.test(password)) kinds++;
  if (/[0-9]/.test(password)) kinds++;
  if (/[^a-zA-Z0-9]/.test(password)) kinds++;

  const score = kinds + (password.length >= 12 ? 1 : 0);

  if (score <= 1) {
    return { filled: 1, word: "Weak", tone: "warning" };
  }
  if (score === 2) {
    return { filled: 2, word: "Fair", tone: "warning" };
  }
  if (score === 3) {
    return { filled: 3, word: "Good", tone: "good" };
  }
  return { filled: 4, word: "Strong", tone: "good" };
}

// "1,500" or "1500" typed; NaN for anything that is not a number.
function numberOf(text: string) {
  const cleaned = text.replace(/,/g, "").trim();
  return cleaned === "" ? NaN : Number(cleaned);
}

function minorOf(text: string) {
  return Math.round(numberOf(text) * 100);
}

// Today as the shop counts it, from the phone's clock (not UTC).
function todayHere() {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${now.getFullYear()}-${month}-${day}`;
}

// What is wrong with one ticked row on the last step, field by field.
function rowProblems(row: StarterRow) {
  const problems = { name: "", price: "", count: "", paid: "" };
  if (!row.add) {
    return problems;
  }

  if (row.name.trim() === "") {
    problems.name = "Type the name.";
  }

  const price = numberOf(row.priceText);
  if (row.priceText.trim() === "") {
    problems.price = "Type the price.";
  } else if (!(price > 0)) {
    problems.price = "A price above 0.";
  }

  if (row.countText.trim() !== "") {
    const count = numberOf(row.countText);
    const decimals = row.countText.trim().split(".")[1] ?? "";
    if (!(count > 0)) {
      problems.count = "Above 0, or leave empty";
    } else if (decimals.length > 3) {
      problems.count = "At most 3 decimal places.";
    }

    // The books need what the stock cost. Never a made-up figure.
    const paid = numberOf(row.paidText);
    if (row.paidText.trim() === "") {
      problems.paid = "Type the cost.";
    } else if (!(paid > 0)) {
      problems.paid = "An amount above 0.";
    }
  }

  return problems;
}

function hasCount(row: StarterRow) {
  return row.add && row.countText.trim() !== "";
}

// What the server said, in its own words when it gave any.
async function detailOf(response: Response, fallback: string): Promise<string> {
  const body = await response.json().catch(() => null);
  return typeof body?.detail === "string" && body.detail !== "" ? body.detail : fallback;
}

// POST /api/setup/check-code. The shop name the code was issued for, or
// the reason it was refused.
async function checkCode(code: string): Promise<{ ok: true; shopName: string } | { ok: false; why: string; offline: boolean }> {
  try {
    const response = await apiFetch(`${API}/api/setup/check-code`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ code: code.trim() }),
    });
    if (!response.ok) {
      return { ok: false, why: await detailOf(response, "That code doesn't work."), offline: false };
    }
    const body: { shopName?: string } = await response.json();
    return { ok: true, shopName: body.shopName ?? "" };
  } catch {
    return { ok: false, why: "Can't reach Salio.", offline: true };
  }
}

function FieldProblem({ id, text }: { id: string; text: string }) {
  return text ? (
    <p id={id} className="mt-1 text-base font-semibold text-error">
      {text}
    </p>
  ) : null;
}

// A shop front with an awning.
function StorefrontIcon() {
  return (
    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M3 9l2-5h14l2 5" />
      <path d="M3 9a3 3 0 0 0 6 0 3 3 0 0 0 6 0 3 3 0 0 0 6 0" />
      <path d="M5 12v8h14v-8" />
      <path d="M10 20v-5h4v5" />
    </svg>
  );
}

// A spanner.
function SpannerIcon() {
  return (
    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z" />
    </svg>
  );
}

// True when the shop's creation answer carries a real login (the server
// signed the owner in). Checked, not assumed: an older server leaves it out.
function isLoginResult(value: unknown): value is LoginResult {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const login = value as Record<string, unknown>;
  return typeof login.id === "string" && typeof login.fullName === "string" && Array.isArray(login.shops);
}

// What went wrong on the progress screen, and the way on.
type Failure = {
  step: number; // 1 to 5
  what: string; // what failed, in plain words
  next: string; // what to do next
  retry: boolean; // "Try again" carries on from this step
  fix: "code" | "email" | "owner" | "signIn" | null; // a second button
};

export default function SetupScreen({
  codeFromLink,
  onBack,
  onSignedIn,
}: {
  // The code in the link they were sent, or "" when there was none.
  codeFromLink: string;
  onBack: () => void;
  onSignedIn: (result: LoginResult) => void;
}) {
  const [form, setForm] = useState<Draft>(() => {
    // A new link's code wins over anything typed earlier, until the shop
    // is made.
    if (codeFromLink !== "" && draft.done < 2 && draft.code !== codeFromLink) {
      draft = { ...emptyDraft(), code: codeFromLink };
    }
    return draft;
  });

  // True while the link's code is being checked: nothing but a loader
  // shows, so the code box never flashes up.
  const [checkingLink, setCheckingLink] = useState(
    () => codeFromLink !== "" && !linkCodeSent && draft.step === "code" && draft.code === codeFromLink,
  );
  const showCheckingLink = useShowLoader(checkingLink);

  // Fields that have been left at least once, and steps where Continue was
  // pressed. Until one of those is true, a field shows no problem. The last
  // step's fields are named "row-<key>-<field>".
  const [left, setLeft] = useState<string[]>([]);
  const [triedSteps, setTriedSteps] = useState<Step[]>([]);

  // What the server said, beside the field it is about. Cleared as soon as
  // that field changes.
  const [serverSaid, setServerSaid] = useState<{ code?: string; email?: string; form?: string }>({});

  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState(false);

  // The progress screen: which of the five steps is running, and why it
  // stopped if it did.
  const [running, setRunning] = useState(0);
  const [failure, setFailure] = useState<Failure | null>(null);

  // Last step: true while the "this goes into the books" box shows.
  const [confirmingStock, setConfirmingStock] = useState(false);

  const step = form.step;
  const noun = nounFor(form.kind, form.tradeChoice);
  const words = itemWords(noun);
  const trade = form.tradeChoice === SOMETHING_ELSE ? form.tradeOther.trim() : form.tradeChoice;
  const shopName = form.shopName.trim();

  // The link's code, checked once on arrival.
  useEffect(() => {
    if (!checkingLink || linkCodeSent) {
      return;
    }
    linkCodeSent = true;

    checkCode(codeFromLink).then((result) => {
      setCheckingLink(false);
      if (result.ok) {
        const next = { ...draft, shopName: draft.shopName || result.shopName, step: "name" as Step };
        rememberDraft(next);
        setForm(next);
      } else {
        // Then, and only then, the code box, with the reason under it.
        setServerSaid({ code: result.offline ? "Can't reach Salio" : result.why });
      }
    });
    // Runs once, on arrival.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function change(changes: Partial<Draft>) {
    const next = { ...draft, ...changes };
    rememberDraft(next);
    setForm(next);
  }

  function problemOf(field: Field): string {
    switch (field) {
      case "code":
        return codeProblem(form.code);
      case "shopName":
        return shopName === "" ? "Type the shop's name." : "";
      case "tradeOther":
        return form.tradeChoice === SOMETHING_ELSE && form.tradeOther.trim() === ""
          ? "Type your trade."
          : "";
      case "fullName":
        return form.fullName.trim() === "" ? "Type your name." : "";
      case "email":
        return emailProblem(form.email);
      case "password":
        return form.password.length < 8 ? "At least 8 characters." : "";
    }
  }

  // What to show under a field: nothing until it has been left or Continue
  // was pressed on its step. The server's own words come first.
  function shown(field: Field): string {
    if (field === "code" && serverSaid.code) return serverSaid.code;
    if (field === "email" && serverSaid.email) return serverSaid.email;
    if (!left.includes(field) && !triedSteps.includes(STEP_OF[field])) return "";
    return problemOf(field);
  }

  function leave(field: string) {
    if (!left.includes(field)) {
      setLeft([...left, field]);
    }
  }

  function goToStep(target: Step, changes: Partial<Draft> = {}) {
    change({ ...changes, step: target });
    // The page scrolls on a phone, the right panel on a tablet.
    window.scrollTo({ top: 0 });
    document.getElementById("setup-step")?.scrollTo({ top: 0 });
  }

  // Back, one question at a time. Never back past the code once accepted.
  function back() {
    const order: Step[] = ["name", "kind", "trade", "owner"];
    const at = order.indexOf(step);
    if (at > 0) {
      goToStep(order[at - 1]);
    }
  }

  function forward() {
    if (!triedSteps.includes(step)) {
      setTriedSteps([...triedSteps, step]);
    }

    const wrong = (Object.keys(STEP_OF) as Field[]).find((f) => STEP_OF[f] === step && problemOf(f) !== "");
    if (wrong) {
      document.getElementById("field-" + wrong)?.focus();
      return;
    }

    if (step === "code") {
      submitCode();
    } else if (step === "name") {
      goToStep("kind");
    } else if (step === "kind") {
      if (form.kind !== "") goToStep("trade");
    } else if (step === "trade") {
      if (form.tradeChoice !== "") goToStep("owner");
    } else if (step === "owner") {
      setServerSaid({});
      goToStep("creating");
      runSteps(form.done + 1);
    } else if (step === "first") {
      saveStarters();
    }
  }

  async function submitCode() {
    if (busy) {
      return;
    }
    setBusy(true);
    const result = await checkCode(form.code);
    setBusy(false);

    if (!result.ok) {
      setServerSaid({ code: result.offline ? "Can't reach Salio" : result.why });
      return;
    }
    setServerSaid({});
    goToStep("name", { shopName: form.shopName || result.shopName });
  }

  // ---- The progress screen: five real calls, in order ----

  // Runs from step `from` to the end, stopping at the first failure. A
  // retry starts again from the step that failed: never before it, because
  // once step 2 is done the code is spent and the shop exists.
  async function runSteps(from: number) {
    if (busy) {
      return;
    }
    setBusy(true);
    setFailure(null);

    const stop = (failed: Failure) => {
      setFailure(failed);
      setRunning(0);
    };
    const offline = (n: number): Failure => ({
      step: n,
      what: "Can't reach Salio.",
      next: "Check internet, then Try again.",
      retry: true,
      fix: null,
    });
    const finished = (n: number, changes: Partial<Draft> = {}) => change({ ...changes, done: n });

    let n = from;
    try {
      // 1. The code, once more: it may have been used or revoked since.
      if (n === 1) {
        setRunning(1);
        const result = await checkCode(form.code);
        if (!result.ok) {
          if (result.offline) {
            stop(offline(1));
          } else {
            stop({ step: 1, what: result.why, next: "Retype it, or ask for another.", retry: false, fix: "code" });
          }
          return;
        }
        finished(1);
        n = 2;
      }

      // 2. The shop, its books, its tax and its owner: one call, one
      // transaction on the server.
      if (n === 2) {
        setRunning(2);
        const response = await apiFetch(`${API}/api/setup/shop`, {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            setupCode: form.code,
            shopName,
            businessKind: form.kind,
            trade,
            sellableNoun: noun,
            ownerFullName: form.fullName.trim(),
            ownerEmail: form.email.trim(),
            ownerPassword: form.password,
            // Asked at the first month end instead (see the top of the file).
            taxPosition: "NotSure",
          }),
        });

        if (!response.ok) {
          const detail = await detailOf(response, `${shopName} was not created.`);
          if (detail.startsWith(CODE_REFUSED_START)) {
            stop({ step: 2, what: detail, next: "Retype it, or ask for another.", retry: false, fix: "code" });
          } else if (detail.includes("already has a Salio account")) {
            stop({ step: 2, what: detail, next: "Use another email, or sign in.", retry: false, fix: "email" });
          } else if (response.status === 429) {
            stop({ step: 2, what: detail, next: "Nothing created. Try later.", retry: false, fix: "owner" });
          } else {
            stop({ step: 2, what: detail, next: "Nothing created. Check your answers.", retry: true, fix: "owner" });
          }
          return;
        }

        const created: { organizationId: string; login?: LoginResult | null } = await response.json();

        if (isLoginResult(created.login)) {
          // The server signed the owner in and trusted this phone as part
          // of creating the shop. Step 3 is already done: no second sign-in.
          finished(3, { organizationId: created.organizationId, login: created.login, password: "" });
          n = 4;
        } else {
          finished(2, { organizationId: created.organizationId });
          n = 3;
        }
      }

      // 3. Only when the server did not sign the owner in above (an older
      // server, or "Try again" after a failed sign-in): signed in exactly as
      // anyone is, so this phone is trusted the normal way.
      if (n === 3) {
        setRunning(3);
        const response = await apiFetch(`${API}/api/auth/login`, {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ email: form.email.trim(), password: draft.password }),
        });
        if (!response.ok) {
          stop({
            step: 3,
            what: `${shopName} created. Sign-in failed.`,
            next: "Try again, or sign in.",
            retry: true,
            fix: "signIn",
          });
          return;
        }
        // The password is not kept any longer than needed.
        finished(3, { login: await response.json(), password: "" });
        n = 4;
      }

      // 4. The shop, as the server now sees this person.
      if (n === 4) {
        setRunning(4);
        const response = await apiFetch(`${API}/api/auth/me`, { credentials: "include" });
        const me: SignedInUser | null = response.ok ? await response.json() : null;
        const shop = me?.shops.find((s) => s.organizationId === draft.organizationId) ?? me?.shops[0];
        if (me === null || shop === undefined) {
          stop({
            step: 4,
            what: `${shopName} didn't open.`,
            next: "Try again, or sign in.",
            retry: true,
            fix: "signIn",
          });
          return;
        }
        finished(4, {
          organizationId: shop.organizationId,
          login: { ...me, pinSet: draft.login?.pinSet ?? false },
        });
        n = 5;
      }

      // 5. The list the till loads first.
      if (n === 5) {
        setRunning(5);
        const response = await apiFetch(`${API}/api/products?organizationId=${draft.organizationId}`, {
          credentials: "include",
        });
        if (!response.ok) {
          stop({
            step: 5,
            what: `${shopName} is ready. List didn't load.`,
            next: "Tap Try again.",
            retry: true,
            fix: null,
          });
          return;
        }
        finished(5);
        setRunning(0);
      }
    } catch {
      stop(offline(n));
    } finally {
      setBusy(false);
    }
  }

  // The second button under a failure.
  function fixFailure(fix: Failure["fix"]) {
    setFailure(null);
    if (fix === "code") {
      setServerSaid({ code: failure?.what });
      goToStep("code", { done: 0 });
    } else if (fix === "email") {
      setServerSaid({ email: failure?.what });
      goToStep("owner", { done: 0 });
    } else if (fix === "owner") {
      goToStep("owner", { done: 0 });
    } else if (fix === "signIn") {
      rememberDraft(emptyDraft());
      onBack();
    }
  }

  // ---- The last step (Sales only) ----

  function changeRow(key: number, changes: Partial<StarterRow>) {
    setConfirmingStock(false);
    setServerSaid({});
    change({ rows: form.rows.map((row) => (row.key === key ? { ...row, ...changes } : row)) });
  }

  function addRow() {
    change({ rows: [...form.rows, blankRow(form.nextRowKey)], nextRowKey: form.nextRowKey + 1 });
  }

  // A row problem shows once that box was left or the button was pressed.
  function rowShown(row: StarterRow, which: "name" | "price" | "count" | "paid") {
    if (!left.includes(`row-${row.key}-${which}`) && !triedSteps.includes("first")) {
      return "";
    }
    return rowProblems(row)[which];
  }

  const ticked = form.rows.filter((row) => row.add);
  const toAdd = ticked.filter((row) => row.productId === null);
  const counted = ticked.filter(hasCount);
  let stockValueMinor = 0;
  for (const row of counted) {
    stockValueMinor += Math.round(numberOf(row.countText) * minorOf(row.paidText));
  }

  // Into the till, with the owner's login. The PIN screen comes first
  // (App.tsx), as after any password login.
  function openTill() {
    const login = form.login;
    if (login === null) {
      return;
    }
    rememberDraft(emptyDraft());
    onSignedIn(login);
  }

  async function saveStarters() {
    if (busy || form.login === null) {
      return;
    }

    // Nothing ticked: nothing to save.
    if (ticked.length === 0) {
      openTill();
      return;
    }

    for (const row of ticked) {
      const problems = rowProblems(row);
      const wrong = (["name", "price", "count", "paid"] as const).find((f) => problems[f] !== "");
      if (wrong) {
        document.getElementById(`row-${row.key}-${wrong}`)?.focus();
        return;
      }
    }

    // Counts go into the books for good: say so in full before saving.
    if (counted.length > 0 && !form.stockSaved && !confirmingStock) {
      setConfirmingStock(true);
      return;
    }

    setBusy(true);
    setServerSaid({});

    const organizationId = form.organizationId;

    // Kept up to date as rows are created, so a failure part-way keeps
    // what was already done, and a retry does not add anything twice.
    let rows = form.rows;
    let skuNumber = form.nextSkuNumber;

    try {
      for (const row of rows) {
        if (!row.add || row.productId !== null) {
          continue;
        }

        // The server needs a SKU. Made here from the shop's word and a
        // number ("P-001"); the owner can change it later. A SKU already
        // taken (409) is skipped for the next number.
        let createdId: string | null = null;
        for (let attempt = 0; attempt < 20 && createdId === null; attempt++) {
          const sku = words.Plural.charAt(0) + "-" + String(skuNumber).padStart(3, "0");
          skuNumber++;

          const response = await apiFetch(`${API}/api/products`, {
            method: "POST",
            credentials: "include",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              organizationId,
              sku,
              name: row.name.trim(),
              sellPriceMinor: minorOf(row.priceText),
            }),
          });

          if (response.status === 409) {
            continue;
          }
          if (response.status === 401) {
            setServerSaid({ form: "Signed out. Sign in again." });
            return;
          }
          if (!response.ok) {
            setServerSaid({ form: `${row.name.trim()}: ${await detailOf(response, "not added.")}` });
            return;
          }
          const product: { id: string } = await response.json();
          createdId = product.id;
        }

        if (createdId === null) {
          setServerSaid({ form: `${row.name.trim()}: not added.` });
          return;
        }

        const id = createdId;
        rows = rows.map((r) => (r.key === row.key ? { ...r, productId: id } : r));
        change({ rows, nextSkuNumber: skuNumber });
      }

      const stockLines = rows.filter(hasCount);
      if (stockLines.length > 0 && !form.stockSaved) {
        const response = await apiFetch(`${API}/api/opening-stock`, {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            organizationId,
            openingDate: todayHere(),
            lines: stockLines.map((row) => ({
              productId: row.productId,
              quantity: numberOf(row.countText),
              unitCostMinor: minorOf(row.paidText),
            })),
          }),
        });

        if (response.status === 401) {
          setServerSaid({ form: "Signed out. Sign in again." });
          return;
        }
        if (!response.ok) {
          setConfirmingStock(false);
          setServerSaid({
            form: `${words.Plural} added. Counts NOT saved: ${await detailOf(response, "refused.")}`,
          });
          return;
        }
        change({ stockSaved: true });
      }

      openTill();
    } catch {
      setServerSaid({ form: "Can't reach Salio. Nothing added is lost." });
    } finally {
      setBusy(false);
    }
  }

  const strength = strengthOf(form.password);

  // ---- The left panel: each question, or its answer once given ----
  const panel: { step: Step; label: string; answer: string }[] = [
    { step: "name", label: "Your shop", answer: shopName },
    { step: "kind", label: "Business type", answer: form.kind },
    { step: "trade", label: "Trade", answer: trade },
    { step: "owner", label: "Owner", answer: form.fullName.trim() },
    {
      step: "creating",
      label: shopName ? `Creating ${shopName}` : "Creating shop",
      answer: `${shopName} is ready`,
    },
  ];
  if (form.kind !== "Services") {
    // The word is not known until the trade is.
    panel.push({ step: "first", label: form.tradeChoice ? `First ${words.plural}` : "Shelf", answer: "" });
  }
  // The code box comes before every question.
  const at = step === "code" ? -1 : panel.findIndex((p) => p.step === step);
  const allDone = form.done === 5;

  const field =
    "mt-1 h-14 w-full rounded-xl border-2 bg-surface px-4 text-xl " +
    "placeholder:text-muted-foreground focus:border-primary focus:outline-none";

  function fieldClass(name: Field) {
    return field + (shown(name) ? " border-error" : " border-border-strong");
  }

  const label = "block text-lg md:text-base font-bold";

  // One tap on a choice: lit while chosen, so going Back shows the answer.
  function choiceClass(chosen: boolean) {
    return (
      "flex min-h-14 w-full items-center gap-4 rounded-xl border-2 px-4 py-3 text-left " +
      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary " +
      (chosen ? "border-primary bg-primary-soft" : "border-border-strong bg-surface active:bg-primary-soft")
    );
  }

  const primaryButton =
    "min-h-14 min-w-0 flex-1 rounded-xl bg-primary px-3 py-2 text-xl leading-tight font-bold text-primary-foreground disabled:opacity-40";

  const stepsNow = progressSteps(shopName);
  const percent = form.done * 20;

  // The link's code is being checked: nothing else yet.
  if (checkingLink) {
    return (
      <div className="flex min-h-full items-center justify-center bg-surface">
        {showCheckingLink && <SalioLoader size="inline" message="Checking code…" />}
      </div>
    );
  }

  return (
    <div className="flex min-h-full flex-col bg-surface text-foreground md:h-full md:flex-row">
      {/* ---------- The left panel, 768px and up ---------- */}
      <aside className="hidden w-[340px] shrink-0 flex-col bg-primary p-8 text-primary-foreground md:flex">
        <p className="text-sm font-bold uppercase tracking-widest text-primary-foreground-soft">Salio · new shop</p>
        <h1 className="mt-3 break-words text-2xl font-bold">{shopName || "Your shop"}</h1>

        <ol className="mt-10 space-y-6">
          {panel.map((p, index) => {
            const done = index < at || (p.step === "creating" && allDone);
            const current = index === at && !done;

            return (
              <li key={p.step} className="flex items-center gap-4" aria-current={current ? "step" : undefined}>
                <span
                  className={
                    "flex h-9 w-9 shrink-0 items-center justify-center rounded-full border-2 text-base font-bold " +
                    (current
                      ? "border-primary-foreground bg-primary-foreground text-primary"
                      : done
                        ? "border-primary-foreground-soft text-primary-foreground"
                        : "border-primary-foreground-muted text-primary-foreground-muted")
                  }
                >
                  {done ? "✓" : index + 1}
                </span>
                <span
                  className={
                    "min-w-0 break-words text-lg " +
                    (current
                      ? "font-bold text-primary-foreground"
                      : done
                        ? "font-semibold text-primary-foreground"
                        : "text-primary-foreground-muted")
                  }
                >
                  {done && p.answer !== "" ? p.answer : p.label}
                </span>
              </li>
            );
          })}
        </ol>

        <p className="mt-auto text-sm text-primary-foreground-muted">Takes about two minutes.</p>
      </aside>

      {/* ---------- The slim header, below 768px ---------- */}
      <header className="shrink-0 bg-primary px-4 py-3 text-primary-foreground md:hidden">
        <div className="flex items-baseline justify-between gap-3">
          <p className="min-w-0 truncate text-sm font-bold uppercase tracking-widest text-primary-foreground-soft">
            {shopName || "Salio"}
          </p>
          {at >= 0 && (
            <p className="shrink-0 text-base font-bold">
              Step {at + 1} of {panel.length}
            </p>
          )}
        </div>
        <div className="mt-2 flex gap-1.5" aria-hidden="true">
          {panel.map((p, index) => (
            <span
              key={p.step}
              className={"h-1.5 flex-1 rounded-full " + (index <= at ? "bg-primary-foreground" : "bg-primary-fill")}
            />
          ))}
        </div>
      </header>

      {/* ---------- The step ---------- */}
      <main id="setup-step" className="flex min-h-0 flex-1 flex-col md:overflow-y-auto">
        <form
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            forward();
          }}
          className="mx-auto flex w-full max-w-[480px] flex-1 flex-col px-4 py-6 md:justify-center md:py-10"
        >
          {at >= 0 && (
            <p className="hidden text-base font-bold text-muted-foreground md:block">
              Step {at + 1} of {panel.length}
            </p>
          )}

          {/* ----- The setup code: only without a link, or when refused ----- */}
          {step === "code" && (
            <>
              <h2 className="text-2xl font-bold">Setup code</h2>
              <label className={label + " mt-4"}>
                <span className="block text-base font-normal text-muted-foreground">
                  From your message, like K7M-P29
                </span>
                <input
                  id="field-code"
                  value={form.code}
                  onChange={(e) => {
                    change({ code: e.target.value });
                    setServerSaid({ ...serverSaid, code: undefined });
                  }}
                  onBlur={() => leave("code")}
                  aria-label="Setup code"
                  aria-invalid={shown("code") ? true : undefined}
                  aria-describedby="problem-code"
                  autoCapitalize="characters"
                  autoCorrect="off"
                  autoComplete="off"
                  enterKeyHint="next"
                  maxLength={20}
                  className={fieldClass("code") + " font-money tracking-wider uppercase"}
                />
              </label>
              <FieldProblem id="problem-code" text={shown("code")} />
            </>
          )}

          {/* ----- The shop's name ----- */}
          {step === "name" && (
            <>
              <p className="mb-2 text-base font-semibold text-primary">✓ Code accepted</p>
              <h2 className="text-2xl font-bold">Shop name</h2>
              <p className="mt-1 text-base text-muted-foreground">Shown on every receipt</p>

              <input
                id="field-shopName"
                value={form.shopName}
                onChange={(e) => change({ shopName: e.target.value })}
                onBlur={() => leave("shopName")}
                aria-label="Shop name"
                aria-invalid={shown("shopName") ? true : undefined}
                aria-describedby="problem-shopName"
                maxLength={200}
                enterKeyHint="next"
                className={fieldClass("shopName") + " mt-4"}
              />
              <FieldProblem id="problem-shopName" text={shown("shopName")} />
            </>
          )}

          {/* ----- What kind of business: one tap continues ----- */}
          {step === "kind" && (
            <>
              <h2 className="text-2xl font-bold">Business type</h2>
              <div className="mt-5 space-y-3">
                {([
                  { kind: "Sales", icon: <StorefrontIcon />, line: "You sell goods" },
                  { kind: "Services", icon: <SpannerIcon />, line: "You sell your work" },
                ] as const).map((choice) => (
                  <button
                    key={choice.kind}
                    type="button"
                    aria-pressed={form.kind === choice.kind}
                    onClick={() =>
                      goToStep("trade", {
                        kind: choice.kind,
                        // A different kind has a different list.
                        ...(form.kind !== choice.kind ? { tradeChoice: "", tradeOther: "" } : {}),
                      })
                    }
                    className={choiceClass(form.kind === choice.kind) + " min-h-24"}
                  >
                    <span className="shrink-0 text-primary">{choice.icon}</span>
                    <span className="min-w-0">
                      <span className="block text-xl font-bold">{choice.kind}</span>
                      <span className="block text-base">{choice.line}</span>
                    </span>
                  </button>
                ))}
              </div>
            </>
          )}

          {/* ----- What the shop deals in: one tap continues ----- */}
          {step === "trade" && (
            <>
              <h2 className="text-2xl font-bold">Your trade</h2>
              <div className="mt-5 space-y-2">
                {[...(form.kind === "Services" ? SERVICES_TRADES : SALES_TRADES.map((t) => t.label)), SOMETHING_ELSE].map(
                  (choice) => (
                    <button
                      key={choice}
                      type="button"
                      aria-pressed={form.tradeChoice === choice}
                      onClick={() => {
                        if (choice === SOMETHING_ELSE) {
                          change({ tradeChoice: choice });
                          // The box appears below: straight into it.
                          setTimeout(() => {
                            const box = document.getElementById("field-tradeOther");
                            box?.focus();
                            box?.scrollIntoView({ block: "center" });
                          }, 0);
                        } else {
                          goToStep("owner", { tradeChoice: choice });
                        }
                      }}
                      className={choiceClass(form.tradeChoice === choice) + " text-lg font-semibold"}
                    >
                      {choice}
                    </button>
                  ),
                )}
              </div>

              {form.tradeChoice === SOMETHING_ELSE && (
                <>
                  <label className={label + " mt-5"}>
                    Your trade
                    <input
                      id="field-tradeOther"
                      value={form.tradeOther}
                      onChange={(e) => change({ tradeOther: e.target.value })}
                      onBlur={() => leave("tradeOther")}
                      aria-invalid={shown("tradeOther") ? true : undefined}
                      aria-describedby="problem-tradeOther"
                      maxLength={120}
                      enterKeyHint="next"
                      className={fieldClass("tradeOther")}
                    />
                  </label>
                  <FieldProblem id="problem-tradeOther" text={shown("tradeOther")} />
                </>
              )}
            </>
          )}

          {/* ----- The owner ----- */}
          {step === "owner" && (
            <>
              <h2 className="text-2xl font-bold">Owner</h2>

              <label className={label + " mt-5"}>
                Your name
                <input
                  id="field-fullName"
                  value={form.fullName}
                  onChange={(e) => change({ fullName: e.target.value })}
                  onBlur={() => leave("fullName")}
                  aria-invalid={shown("fullName") ? true : undefined}
                  aria-describedby="problem-fullName"
                  maxLength={200}
                  autoComplete="name"
                  enterKeyHint="next"
                  className={fieldClass("fullName")}
                />
              </label>
              <FieldProblem id="problem-fullName" text={shown("fullName")} />

              <label className={label + " mt-5"}>
                Your email
                <input
                  id="field-email"
                  type="email"
                  value={form.email}
                  onChange={(e) => {
                    change({ email: e.target.value });
                    setServerSaid({ ...serverSaid, email: undefined });
                  }}
                  onBlur={() => leave("email")}
                  aria-invalid={shown("email") ? true : undefined}
                  aria-describedby="problem-email email-note"
                  maxLength={256}
                  autoCapitalize="none"
                  autoCorrect="off"
                  autoComplete="username"
                  enterKeyHint="next"
                  className={fieldClass("email")}
                />
              </label>
              <FieldProblem id="problem-email" text={shown("email")} />
              {/* Literal: there is no password reset yet. */}
              <p id="email-note" className="mt-2 rounded-lg bg-warning px-3 py-2 text-base text-warning-foreground">
                Check it: your only way back in.
              </p>

              <label className={label + " mt-5"}>
                Password
                <span className="block text-base font-normal text-muted-foreground">8 characters or more</span>
                <input
                  id="field-password"
                  type={showPassword ? "text" : "password"}
                  value={form.password}
                  onChange={(e) => change({ password: e.target.value })}
                  onBlur={() => leave("password")}
                  aria-invalid={shown("password") ? true : undefined}
                  aria-describedby="password-strength"
                  autoCapitalize="none"
                  autoCorrect="off"
                  autoComplete="new-password"
                  enterKeyHint="done"
                  className={fieldClass("password")}
                />
              </label>

              {/* Strength, live, from the first character. Never an error
                  colour until the field has been left or Continue pressed. */}
              <div id="password-strength" className="mt-2" aria-live="polite">
                <div className="flex gap-1.5" aria-hidden="true">
                  {[1, 2, 3, 4].map((n) => (
                    <span
                      key={n}
                      className={
                        "h-2 flex-1 rounded-full " +
                        (form.password === "" || n > strength.filled
                          ? "bg-border"
                          : strength.tone === "good"
                            ? "bg-primary"
                            : strength.tone === "warning" || !shown("password")
                              ? "bg-warning-strong"
                              : "bg-error")
                      }
                    />
                  ))}
                </div>
                <div className="mt-1 flex items-baseline justify-between gap-3">
                  <p
                    className={
                      "text-base font-semibold " +
                      (shown("password")
                        ? "text-error"
                        : strength.tone === "good"
                          ? "text-primary"
                          : "text-warning-strong")
                    }
                  >
                    {shown("password") || (form.password === "" ? "" : strength.word)}
                  </p>
                  <button
                    type="button"
                    onClick={() => setShowPassword(!showPassword)}
                    aria-pressed={showPassword}
                    className="min-h-11 shrink-0 px-1 text-base font-bold text-primary"
                  >
                    {showPassword ? "Hide" : "Show"}
                  </button>
                </div>
              </div>
            </>
          )}

          {/* ----- Creating the shop: the bar moves only when a step finishes ----- */}
          {step === "creating" && (
            <>
              <h2 className="break-words text-2xl font-bold">
                {allDone ? `${shopName} is ready` : `Setting up ${shopName}`}
              </h2>

              <div className="mt-6 flex items-center gap-3">
                <div
                  role="progressbar"
                  aria-label={`Setting up ${shopName}`}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={percent}
                  className="h-4 min-w-0 flex-1 overflow-hidden rounded-full bg-border"
                >
                  <div
                    className={
                      "h-full rounded-full transition-[width] duration-300 " + (failure ? "bg-error" : "bg-primary")
                    }
                    style={{ width: `${percent}%` }}
                  />
                </div>
                <span className="w-14 shrink-0 text-right font-money text-xl font-bold">{percent}%</span>
              </div>

              <ol className="mt-6 space-y-3" aria-live="polite">
                {stepsNow.map((text, index) => {
                  const n = index + 1;
                  const done = n <= form.done;
                  const failed = failure?.step === n;
                  const current = n === running;

                  return (
                    <li key={n} className="flex items-center gap-3">
                      <span
                        className={
                          "flex h-8 w-8 shrink-0 items-center justify-center rounded-full border-2 text-base font-bold " +
                          (done
                            ? "border-primary bg-primary text-primary-foreground"
                            : failed
                              ? "border-error bg-error text-error-foreground"
                              : current
                                ? "border-primary text-primary"
                                : "border-border text-muted-foreground")
                        }
                        aria-hidden="true"
                      >
                        {done ? "✓" : failed ? "!" : n}
                      </span>
                      <span
                        className={
                          "min-w-0 break-words text-lg " +
                          (current || failed
                            ? "font-bold text-foreground"
                            : done
                              ? "text-foreground"
                              : "text-muted-foreground opacity-70")
                        }
                      >
                        {text}
                        {done && <span className="sr-only"> (done)</span>}
                      </span>
                    </li>
                  );
                })}
              </ol>

              {failure && (
                <div role="alert" className="mt-6 rounded-xl border-2 border-error px-4 py-3">
                  <p className="text-base font-bold text-error">Stopped at: {stepsNow[failure.step - 1]}</p>
                  <p className="mt-1 text-base">{failure.what}</p>
                  <p className="mt-1 text-base font-semibold">{failure.next}</p>
                </div>
              )}
            </>
          )}

          {/* ----- The first things on the shelf (Sales only) ----- */}
          {step === "first" && (
            <>
              <h2 className="text-2xl font-bold">First {words.plural}</h2>
              <p className="mt-1 text-base">
                Add the rest later
              </p>
              <p className="mt-1 text-base text-muted-foreground">
                Quantity is optional
              </p>

              <div className="mt-4 space-y-3">
                {form.rows.map((row, index) => {
                  const added = row.productId !== null;
                  const box = "mt-1 h-12 w-full rounded-lg border-2 bg-surface px-3 text-lg focus:border-primary focus:outline-none read-only:bg-background";
                  const boxClass = (which: "name" | "price" | "count" | "paid") =>
                    box + (rowShown(row, which) ? " border-error" : " border-border-strong");

                  return (
                    <div
                      key={row.key}
                      className={"rounded-xl border-2 p-3 " + (row.add ? "border-primary" : "border-border")}
                    >
                      <label className="flex min-h-11 cursor-pointer items-center gap-3">
                        <input
                          type="checkbox"
                          checked={row.add}
                          disabled={added}
                          onChange={(e) => changeRow(row.key, { add: e.target.checked })}
                          className="h-6 w-6 shrink-0 accent-primary"
                        />
                        <span className="text-lg font-bold">
                          {added ? "✓ Added" : `Add ${words.singular} ${index + 1}`}
                        </span>
                      </label>

                      <label className="mt-1 block text-base font-semibold">
                        Name
                        <input
                          id={`row-${row.key}-name`}
                          value={row.name}
                          readOnly={added}
                          onChange={(e) =>
                            changeRow(row.key, {
                              name: e.target.value,
                              // Typing a name into an empty row ticks it.
                              add: row.add || (row.name === "" && e.target.value.trim() !== ""),
                            })
                          }
                          onBlur={() => leave(`row-${row.key}-name`)}
                          maxLength={200}
                          enterKeyHint="next"
                          className={boxClass("name")}
                        />
                      </label>
                      <FieldProblem id={`problem-row-${row.key}-name`} text={rowShown(row, "name")} />

                      <div className="mt-2 grid grid-cols-2 gap-2">
                        <div>
                          <label className="block text-base font-semibold">
                            Price (KES)
                            <input
                              id={`row-${row.key}-price`}
                              value={row.priceText}
                              readOnly={added}
                              onChange={(e) => changeRow(row.key, { priceText: e.target.value })}
                              onBlur={() => leave(`row-${row.key}-price`)}
                              inputMode="decimal"
                              enterKeyHint="next"
                              className={boxClass("price") + " text-right font-money"}
                            />
                          </label>
                          <FieldProblem id={`problem-row-${row.key}-price`} text={rowShown(row, "price")} />
                        </div>
                        <div>
                          <label className="block text-base font-semibold">
                            Quantity
                            <input
                              id={`row-${row.key}-count`}
                              value={row.countText}
                              readOnly={form.stockSaved}
                              onChange={(e) => changeRow(row.key, { countText: e.target.value })}
                              onBlur={() => leave(`row-${row.key}-count`)}
                              inputMode="decimal"
                              enterKeyHint="next"
                              className={boxClass("count") + " text-center font-money"}
                            />
                          </label>
                          <FieldProblem id={`problem-row-${row.key}-count`} text={rowShown(row, "count")} />
                        </div>
                      </div>

                      {/* Only with a count: what one cost the shop. Opening
                          stock is booked at cost, never at the selling price. */}
                      {row.countText.trim() !== "" && (
                        <>
                          <label className="mt-2 block text-base font-semibold">
                            Cost (KES)
                            <input
                              id={`row-${row.key}-paid`}
                              value={row.paidText}
                              readOnly={form.stockSaved}
                              onChange={(e) => changeRow(row.key, { paidText: e.target.value })}
                              onBlur={() => leave(`row-${row.key}-paid`)}
                              inputMode="decimal"
                              enterKeyHint="next"
                              className={boxClass("paid") + " text-right font-money"}
                            />
                          </label>
                          <FieldProblem id={`problem-row-${row.key}-paid`} text={rowShown(row, "paid")} />
                        </>
                      )}
                    </div>
                  );
                })}
              </div>

              <button
                type="button"
                onClick={addRow}
                className="mt-3 min-h-12 w-full rounded-xl border-2 border-primary text-lg font-bold text-primary"
              >
                + Another {words.singular}
              </button>

              {counted.length > 0 && !form.stockSaved && (
                <div className="mt-4 rounded-xl bg-warning px-4 py-3 text-base text-warning-foreground">
                  <p className="font-bold">
                    Shelf value: <span className="font-money">KES {kes(stockValueMinor)}</span>
                  </p>
                  <p>Counts are saved once</p>
                </div>
              )}
            </>
          )}

          {/* ----- The buttons: in the same place on every step. On a
              phone they stay in sight at the bottom while the page scrolls. ----- */}
          <div className="sticky bottom-0 -mx-4 mt-8 bg-surface px-4 pb-4 pt-3">
            {serverSaid.form && (
              <p role="alert" className="mb-2 text-base font-semibold text-error">
                {serverSaid.form}
              </p>
            )}

            {/* Counts go into the books for good: the full warning first. */}
            {step === "first" && confirmingStock && (
              <p className="mb-2 rounded-xl bg-warning px-4 py-3 text-base text-warning-foreground">
                {counted.length} {counted.length === 1 ? words.singular : words.plural}, KES {kes(stockValueMinor)}, dated
                today. Can't be changed later.
              </p>
            )}

            {/* Every question but the progress screen. */}
            {step !== "creating" && (
              <div className="flex gap-2">
                {(step === "kind" || step === "trade" || step === "owner") && (
                  <button
                    type="button"
                    onClick={back}
                    className="h-14 shrink-0 rounded-xl border-2 border-border-strong px-4 text-lg font-bold"
                  >
                    ‹ Back
                  </button>
                )}
                {/* One tap on a choice moves on, so with nothing chosen yet
                    there is no Continue to look at. */}
                {!((step === "kind" && form.kind === "") || (step === "trade" && form.tradeChoice === "")) && (
                <button type="submit" disabled={busy} className={primaryButton}>
                  {step === "code"
                    ? busy
                      ? "Checking…"
                      : "Continue"
                    : step === "owner"
                      ? "Create shop"
                      : step !== "first"
                        ? "Continue"
                        : busy
                          ? "Saving…"
                          : confirmingStock
                            ? "Yes, save"
                            : ticked.length === 0
                              ? "Open till"
                              : toAdd.length === 0
                                ? "Save and open"
                                : `Add ${toAdd.length} ${toAdd.length === 1 ? words.singular : words.plural}`}
                </button>
                )}
              </div>
            )}

            {/* The progress screen: Try again from the failed step, the
                fix that fits, or on at the end. */}
            {step === "creating" && failure && (
              <div className="flex flex-col gap-2">
                {failure.retry && (
                  <button type="button" disabled={busy} onClick={() => runSteps(failure.step)} className={primaryButton}>
                    Try again
                  </button>
                )}
                {failure.fix && (
                  <button
                    type="button"
                    onClick={() => fixFailure(failure.fix)}
                    className="min-h-14 w-full rounded-xl border-2 border-border-strong px-3 text-lg font-bold"
                  >
                    {failure.fix === "code"
                      ? "Retype code"
                      : failure.fix === "email"
                        ? "Change email"
                        : failure.fix === "owner"
                          ? "‹ Back"
                          : "Sign in"}
                  </button>
                )}
              </div>
            )}
            {step === "creating" && allDone && (
              <button
                type="button"
                onClick={() => (form.kind === "Services" ? openTill() : goToStep("first"))}
                className={primaryButton + " w-full"}
              >
                {form.kind === "Services" ? "Open till" : `Add ${words.plural}`}
              </button>
            )}

            {step === "first" && (
              <button
                type="button"
                disabled={busy}
                onClick={() => (confirmingStock ? setConfirmingStock(false) : openTill())}
                className="mt-2 min-h-12 w-full rounded-xl border-2 border-border-strong text-lg font-bold disabled:opacity-40"
              >
                {confirmingStock ? "Go back" : "Skip"}
              </button>
            )}

            {(step === "code" || step === "name") && (
              <button
                type="button"
                onClick={onBack}
                className="mt-3 min-h-11 w-full text-base font-semibold text-primary underline"
              >
                Already have a shop? Sign in
              </button>
            )}
          </div>
        </form>
      </main>
    </div>
  );
}
