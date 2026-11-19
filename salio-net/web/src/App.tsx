import { useState, useEffect, useRef, useSyncExternalStore, type ReactNode } from "react";
import BottomNav, { type Tab } from "./BottomNav";
import Sidebar from "./Sidebar";
import InstallPill from "./InstallPill";
import { type NavPlace } from "./NavIcons";
import MoreScreen from "./MoreScreen";
import PartsScreen from "./PartsScreen";
import MonthScreen from "./MonthScreen";
import LoginScreen from "./LoginScreen";
import ChoosePasswordScreen from "./ChoosePasswordScreen";
import ChoosePinScreen from "./ChoosePinScreen";
import PinUnlockScreen from "./PinUnlockScreen";
import PeopleScreen from "./PeopleScreen";
import SetupScreen from "./SetupScreen";
import ShopChooser from "./ShopChooser";
import SalesScreen from "./SalesScreen";
import StockInScreen from "./StockInScreen";
import QuickAddForm, { type QuickAdded } from "./QuickAddForm";
import NeedingCostScreen from "./NeedingCostScreen";
import ImportScreen from "./ImportScreen";
import ImportHistoryScreen from "./ImportHistoryScreen";
import InvoiceScreen from "./InvoiceScreen";
import OpeningStockScreen from "./OpeningStockScreen";
import ExpensesScreen from "./ExpensesScreen";
import ShopWordScreen from "./ShopWordScreen";
import AddPartsChoices from "./AddPartsChoices";
import { buildMenu } from "./menu";
import CustomerPicker, { type Customer } from "./CustomerPicker";
import OwedScreen from "./OwedScreen";
import Receipt, { type ReceiptLine, type ReceiptPayment } from "./Receipt";
import SalioLoader from "./SalioLoader";
import useShowLoader from "./useShowLoader";
import { isGatewayDown, isReachable, onReachableChange } from "./api";
import { API, kes, findWayIn, devicePinOwner, itemWords, type SignedInUser, type WayIn } from "./auth";
import { allCartsEmpty, cartKey, clearCarts, loadCarts, saveCarts, type CartLine } from "./cartStorage";
import { useAutoLock } from "./lock";
import { apiFetch, problemText } from "./api";
import CartPriceButtons from "./CartPriceButtons";
import { belowLowest } from "./priceSteps";

type Product = {
  id: string;
  sku: string;
  name: string;
  sellPriceMinor: number;
  binLocation: string | null;
  quantityOnHand: number;
  // Below zero: more sold than Salio was told arrived.
  needsReceiving: boolean;
  // Null for a Cashier, and null while needsReceiving: cost is unknown then.
  // Left out entirely by the server when this person may not see it.
  averageCostMinor?: number | null;
  // The lowest price to agree to. Sent whoever is signed in; null when the
  // cost is not known.
  stopPriceMinor?: number | null;
  // False when the cost came from an import or nobody ever stated one.
  // Sent only with the cost.
  costIsConfirmed?: boolean;
};

type View =
  | "till"
  | "sales"
  | "owed"
  | "more"
  | "stockIn"
  | "needingCost"
  | "importParts"
  | "importHistory"
  | "readInvoice"
  | "openingStock"
  | "expenses"
  | "parts"
  | "month"
  | "tax"
  | "people"
  | "shopWord";

// The screen each tab at the bottom opens.
const TAB_VIEWS: Record<Tab, View> = {
  sell: "till",
  today: "sales",
  owed: "owed",
  more: "more",
};

// The screen each place in the menu opens (the tablet sidebar and the
// phone's More screen). Import history has no place of its own: it is
// reached from the Import screen.
const PLACE_VIEWS: Record<NavPlace, View> = {
  sell: "till",
  today: "sales",
  owed: "owed",
  more: "more",
  stockIn: "stockIn",
  needingCost: "needingCost",
  importParts: "importParts",
  readInvoice: "readInvoice",
  openingStock: "openingStock",
  moneyOut: "expenses",
  month: "month",
  tax: "tax",
  parts: "parts",
  people: "people",
  shopWord: "shopWord",
};

// 768px and up is a tablet: the sidebar replaces the bottom tabs and More.
// Read in script only where CSS cannot decide, which is which screen shows.
const WIDE = "(min-width: 768px)";

function watchWidth(onChange: () => void) {
  const query = window.matchMedia(WIDE);
  query.addEventListener("change", onChange);
  return () => query.removeEventListener("change", onChange);
}

function useIsWide() {
  return useSyncExternalStore(watchWidth, () => window.matchMedia(WIDE).matches);
}

// What one unit of this line costs the shop today, or 0 if nothing is known
// yet - a part that has never been received has no cost to compare against.
function costOf(line: CartLine, products: Product[]) {
  const product = products.find((p) => p.id === line.productId);
  return product ? product.averageCostMinor ?? 0 : 0;
}

function belowCost(line: CartLine, products: Product[]) {
  const cost = costOf(line, products);
  return cost > 0 && line.unitPriceMinor < cost;
}

// The asking price is the part's own. A line whose part has left the list
// keeps the price it was added at, with no stop.
function askingOf(line: CartLine, products: Product[]) {
  const product = products.find((p) => p.id === line.productId);
  return product ? product.sellPriceMinor : line.unitPriceMinor;
}

function stopOf(line: CartLine, products: Product[]) {
  const product = products.find((p) => p.id === line.productId);
  return product?.stopPriceMinor ?? null;
}

// The buying price for the "you keep" line: only a confirmed one. A cost an
// import carried in is not pricing guidance.
function confirmedCostOf(line: CartLine, products: Product[]) {
  const product = products.find((p) => p.id === line.productId);
  return product?.costIsConfirmed ? product.averageCostMinor : undefined;
}

function lineBelowLowest(line: CartLine, products: Product[]) {
  return belowLowest(askingOf(line, products), stopOf(line, products), line.unitPriceMinor);
}

// The product this line is for, if Salio shows none of it on the shelf.
// The part is in the customer's hand, so the sale goes ahead: this only warns.
function shelfShowsNone(line: CartLine, products: Product[]) {
  const product = products.find((p) => p.id === line.productId);
  return product && product.quantityOnHand <= 0 ? product : null;
}

// Why the last sale is not on a receipt, in two lines.
type SaleProblem = { title: string; text: string };

// The tablet had no internet at all: the sale never left it.
const SALE_NOT_SENT: SaleProblem = {
  title: "Sale NOT saved",
  text: "Can't reach Salio. The cart is kept.",
};

// Sent, but no answer came back. It may have been saved.
const SALE_UNKNOWN: SaleProblem = {
  title: "Check Today before trying again",
  text: "It may have gone through. Don't charge twice.",
};

// The setup code from a setup link (…/setup?code=K7MP29), read once when
// the app loads. "" when the address has none. The code is then taken out of
// the address, so it is not left in the history or shown on screen. Only on
// the till's own addresses: an unknown address stays as it is, not found.
const onAppPath = window.location.pathname === "/" || window.location.pathname === "/setup";
const setupCodeInLink = onAppPath
  ? new URLSearchParams(window.location.search).get("code")?.trim() ?? ""
  : "";
if (setupCodeInLink !== "") {
  window.history.replaceState(null, "", "/setup");
}

// Setting up a shop lives at /setup, so a link or a typed address opens it.
function onSetupPath() {
  return window.location.pathname === "/setup";
}

function setPath(path: string) {
  if (window.location.pathname !== path) {
    window.history.replaceState(null, "", path);
  }
}

// A one-off id for a checkout. crypto.randomUUID needs a secure context, so
// over plain http it is missing: Pay must still work, hence the fallback.
function newCheckoutId(): string {
  try {
    if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
      return crypto.randomUUID();
    }
  } catch {
    // fall through
  }

  const bytes = new Uint8Array(16);

  if (typeof crypto !== "undefined" && typeof crypto.getRandomValues === "function") {
    crypto.getRandomValues(bytes);
  } else {
    for (let i = 0; i < bytes.length; i++) {
      bytes[i] = Math.floor(Math.random() * 256);
    }
  }

  // Version 4, variant 1, so the server reads it as a normal Guid.
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  const hex: string[] = [];
  for (let i = 0; i < bytes.length; i++) {
    hex.push(bytes[i].toString(16).padStart(2, "0"));
  }

  return (
    hex.slice(0, 4).join("") + "-" +
    hex.slice(4, 6).join("") + "-" +
    hex.slice(6, 8).join("") + "-" +
    hex.slice(8, 10).join("") + "-" +
    hex.slice(10, 16).join("")
  );
}

export default function App() {
  // Who is signed in, and which of their shops is open. Both come from the
  // server: the till no longer knows an organisation id of its own.
  const [user, setUser] = useState<SignedInUser | null>(null);
  const [shopId, setShopId] = useState<string | null>(null);
  const [checkingLogin, setCheckingLogin] = useState(true);

  // True when Salio could not be asked who this is (no internet, or the
  // server restarting). Not signed out: a "Try again" screen, nothing lost.
  const [cantStart, setCantStart] = useState(false);
  const reachable = useSyncExternalStore(onReachableChange, isReachable);

  // Which screen is showing. The till is what opens: selling comes first.
  const [view, setView] = useState<View>("till");
  const isWide = useIsWide();

  // Before signing in: the password, the PIN on a trusted phone, or setting
  // up a shop with a setup code (at /setup; locked rule 14).
  const [signInView, setSignInView] = useState<"login" | "setup" | "pin">("login");

  // Whose trusted phone this is, for the PIN screen.
  const [pinName, setPinName] = useState("");

  // Said on the password screen, for example after too many wrong PINs.
  const [loginMessage, setLoginMessage] = useState("");

  // True after a password login on a phone with no PIN yet.
  const [choosingPin, setChoosingPin] = useState(false);

  const [products, setProducts] = useState<Product[]>([]);

  // True once the shelf has loaded at least once, so a shop with no parts
  // yet can be told apart from a shelf still on its way.
  const [shelfLoaded, setShelfLoaded] = useState(false);

  // Left over from the old empty shelf, which opened Parts straight on
  // "Add". Nothing sets it true now; Parts still takes it.
  const [addFirstPart, setAddFirstPart] = useState(false);

  // True while the quick-add form is open from "Add a part" (the empty
  // shelf, or "+ Add parts"): the part goes onto the shelf, not into a cart.
  const [addingToShelf, setAddingToShelf] = useState(false);

  // True while the "+ Add parts" sheet is open over the Sell screen.
  const [choosingHowToAdd, setChoosingHowToAdd] = useState(false);

  // Whether a real invoice reader is set up, and for which person and shop
  // that was asked. Only "real: true" for the pair on screen counts; while
  // unasked, or after any failure, the reader is treated as not there.
  const [reader, setReader] = useState<{ askedFor: string; real: boolean } | null>(null);
  const [search, setSearch] = useState("");

  // One cart per customer at the counter, and which one is on screen.
  const [carts, setCarts] = useState<CartLine[][]>([[]]);
  const [activeCart, setActiveCart] = useState(0);

  // Whose carts are in `carts` above: the storage key for that person and
  // shop (see cartStorage.ts), or null when nobody's are (signed out, or no
  // shop open yet). Only that key is ever written, so one person's carts
  // can never be saved under another person's name.
  const [cartsFor, setCartsFor] = useState<string | null>(null);

  // The lock screen, or null while the till is open. The login, the open
  // shop and the carts all stay while it shows (see lock.ts).
  //   checking: asking the server whether this phone has a PIN.
  //   pin:      the PIN pad, for the PIN's owner, `name`.
  //   password: email and password (this phone has no PIN, or the person
  //             chose "Use email and password instead").
  const [lock, setLock] = useState<
    | { kind: "checking" }
    | { kind: "pin"; name: string }
    | { kind: "password"; message: string }
    | null
  >(null);
  const [paying, setPaying] = useState(false);

  // The receipt for the sale just made, while it is on screen.
  const [receipt, setReceipt] = useState<{
    saleNumber: string;
    occurredAt: string;
    lines: ReceiptLine[];
    payments: ReceiptPayment[];
    totalMinor: number;
    customerName: string | null;
    customerBalanceMinor: number | null;
  } | null>(null);

  // True while the Credit button's "which customer?" screen is open.
  const [choosingCustomer, setChoosingCustomer] = useState(false);

  // Why the last sale was not saved. Shown in the till, never as a receipt.
  const [saleProblem, setSaleProblem] = useState<SaleProblem | null>(null);

  // One id per checkout, made before the sale is sent and kept until it is
  // known to have landed. A second press of Pay sends the SAME id, so Salio
  // hands back the first sale instead of booking another one. Cleared only
  // when a sale comes back, so every retry in between reuses it.
  const checkoutId = useRef<string | null>(null);

  // What was typed in the search when "Add" was tapped: the quick-add form
  // is open while this is not null.
  const [quickAddText, setQuickAddText] = useState<string | null>(null);

  // How many parts have sold, or have stock, with no cost known. The badge
  // on "Parts needing a cost". Owner only; 0 hides it.
  const [needingCostCount, setNeedingCostCount] = useState(0);

  // The part Stock in opens with, when opened from "Parts needing a cost".
  const [stockInPart, setStockInPart] = useState<Product | undefined>(undefined);

  // The cart being served right now. Everything below reads this.
  const lines = carts[activeCart] ?? [];

  // True while a shelf reload is in flight. A ref, not state, because
  // changing it must not re-render anything.
  const reloading = useRef(false);

  // The shop a reload was asked for while another was in flight, or null.
  // That reload runs as soon as the one in flight ends, so a reload asked
  // for after an import or a quick-add is never simply dropped.
  const reloadWanted = useRef<string | null>(null);

  // The carts follow the person and the shop. When either changes (a sign
  // in, a shop chosen, a different person unlocking), the saved carts for
  // the new pair are read in, or a fresh empty cart if there are none.
  // Done while drawing, not in an effect, so the old person's carts are
  // never on screen, or saved, under the new person even for a moment.
  // (React's pattern for "reset state when something changes".)
  const wantedCartsFor = user !== null && shopId !== null ? cartKey(user.id, shopId) : null;
  if (wantedCartsFor !== cartsFor) {
    const saved = wantedCartsFor !== null ? loadCarts(wantedCartsFor) : null;
    setCartsFor(wantedCartsFor);
    setCarts(saved?.carts ?? [[]]);
    setActiveCart(saved?.activeCart ?? 0);
  }

  // Every change to the carts is saved at once: a reload, a lock or a flat
  // battery then loses nothing. A paid cart has already been taken out of
  // `carts`, so it is gone from storage too; the other customers' stay.
  useEffect(() => {
    if (cartsFor !== null) {
      saveCarts(cartsFor, carts, activeCart);
    }
  }, [cartsFor, carts, activeCart]);

  function signedIn(signedInUser: SignedInUser) {
    setUser(signedInUser);
    // One shop needs no chooser.
    if (signedInUser.shops.length === 1) {
      setShopId(signedInUser.shops[0].organizationId);
    }
  }

  function applyWayIn(way: WayIn) {
    setCheckingLogin(false);
    setCantStart(way.kind === "unreachable");

    if (way.kind === "unreachable") {
      return;
    }

    if (way.kind === "signedIn") {
      setPath("/");
      signedIn(way.user);
      // Reopened: always open locked, the till behind it. The PIN pad on a
      // phone with a PIN; email and password on one without, the same as
      // the Lock button does.
      setLock(
        way.lockedFor !== null
          ? { kind: "pin", name: way.lockedFor }
          : { kind: "password", message: "" },
      );
    } else if (onSetupPath()) {
      // Opened at /setup, from a setup link or typed: setting up the shop.
      setSignInView("setup");
    } else if (way.kind === "pin") {
      setPinName(way.name);
      setSignInView("pin");
    } else {
      setSignInView("login");
    }
  }

  // An ended login, from any screen: the session ran out, an owner signed
  // every device out, or the person signed out. A trusted phone with a PIN
  // goes to the PIN pad; anything else to email and password.
  function loginEnded() {
    setLock(null);
    setView("till");
    setUser(null);
    setShopId(null);
    setCheckingLogin(true);
    findWayIn().then(applyWayIn);
  }

  // On start-up: still signed in, the PIN, or the password.
  useEffect(() => {
    findWayIn().then(applyWayIn);
    // applyWayIn only calls state setters, which never change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // "Try again" on the can't-reach screen.
  function tryStartAgain() {
    setCheckingLogin(true);
    findWayIn().then(applyWayIn);
  }

  // The can't-reach screen also tries by itself as soon as the banner's
  // quiet check finds Salio again.
  useEffect(() => {
    if (cantStart && reachable) {
      findWayIn().then(applyWayIn);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cantStart, reachable]);

  // The only place the shelf is loaded: when a shop opens, and again after
  // each sale. One function, so the two can never drift apart.
  //
  // credentials: "include" sends the login cookie; without it the server
  // would not know who is asking.
  async function loadProducts(organizationId: string) {
    // Never two at once. Besides sparing the phone's connection, this stops
    // an older answer landing after a newer one and putting yesterday's
    // quantities back on screen.
    if (reloading.current) {
      reloadWanted.current = organizationId;
      return;
    }

    reloading.current = true;

    try {
      const response = await apiFetch(
        `${API}/api/products?organizationId=${organizationId}`,
        { credentials: "include" },
      );

      if (response.status === 401) {
        // Not a failed refresh: the login ran out, or an owner signed every
        // device out. The next sale would be refused anyway.
        reloadWanted.current = null;
        loginEnded();
        return;
      }

      if (!response.ok) {
        // Leave the shelf exactly as it is. Quantities go stale, which is
        // better than an empty list, and a sale that is already saved is not
        // worth an error box.
        return;
      }

      setProducts(await response.json());
      setShelfLoaded(true);

      // Not awaited: the badge catches up on its own.
      loadNeedingCostCount(organizationId);
    } catch {
      // Same again: keep what is on screen and say nothing.
    } finally {
      // Runs on every path, including the early returns above, so a failed
      // reload cannot jam the timer shut.
      reloading.current = false;

      // One was asked for meanwhile: do it now, with fresh figures.
      const wanted = reloadWanted.current;
      reloadWanted.current = null;
      if (wanted !== null) {
        loadProducts(wanted);
      }
    }
  }

  // The badge count. Asked after anything that can change it: the shelf
  // reload after a sale, a quick-add or a delivery, and opening More. Only
  // for an Owner: cost is theirs (locked rule 13). A failure keeps the old
  // number: a badge is not worth an error box.
  async function loadNeedingCostCount(organizationId: string) {
    const role = user?.shops.find((s) => s.organizationId === organizationId)?.role;
    if (role !== 0) {
      setNeedingCostCount(0);
      return;
    }

    try {
      const response = await apiFetch(
        `${API}/api/products/needing-cost/count?organizationId=${organizationId}`,
        { credentials: "include" },
      );
      if (response.ok) {
        const found: { count: number } = await response.json();
        setNeedingCostCount(found.count);
      }
    } catch {
      // Keep the number on screen.
    }
  }

  // Opening More (or the list itself) asks again, so the badge is fresh.
  useEffect(() => {
    if (shopId !== null && (view === "more" || view === "needingCost")) {
      loadNeedingCostCount(shopId);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, view]);

  // The shelf, for the shop that is open.
  useEffect(() => {
    if (shopId === null) {
      return;
    }

    loadProducts(shopId);
    // Again when a different person unlocks the same shop: a Cashier must
    // not keep the Owner's copy, which carries costs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId, user?.id]);

  // Coming back to Sell reads the shelf again, so the count and the list
  // are right after an import, a delivery or a change on the Parts screen.
  useEffect(() => {
    if (shopId !== null && view === "till") {
      loadProducts(shopId);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view]);

  // Is a real invoice reader set up? Asked once per person and shop, and
  // only for an Owner: reading invoices is cost and stock (locked rule 13).
  // Anything but a clear "real: true" (a 404 from an older server, a 403, no
  // connection, not JSON) means no: the demo reader is never offered.
  const openShopRole = user?.shops.find((s) => s.organizationId === shopId)?.role;
  const readerAskFor = user !== null && shopId !== null && openShopRole === 0 ? `${user.id}:${shopId}` : null;

  useEffect(() => {
    if (readerAskFor === null || shopId === null) {
      return;
    }

    let current = true;
    const askedFor = readerAskFor;

    apiFetch(`${API}/api/supplier-invoices/reader-status?organizationId=${shopId}`, { credentials: "include" })
      .then(async (response) => {
        const found: { real?: unknown } | null = response.ok ? await response.json() : null;
        if (current) {
          setReader({ askedFor, real: found !== null && found.real === true });
        }
      })
      .catch(() => {
        if (current) {
          setReader({ askedFor, real: false });
        }
      });

    return () => {
      current = false;
    };
  }, [readerAskFor, shopId]);

  const readerReal = reader !== null && reader.askedFor === readerAskFor && reader.real;

  // Two phones serve the same shop, so each one picks up the other's sales
  // by asking again every 30 seconds.
  useEffect(() => {
    if (shopId === null) {
      return;
    }

    // Copied into a typed const so the timer functions below know it is a
    // string, without writing "!" anywhere.
    const organizationId: string = shopId;

    let timer: number | undefined;

    function startTimer() {
      stopTimer();
      timer = window.setInterval(() => loadProducts(organizationId), 30000);
    }

    function stopTimer() {
      if (timer !== undefined) {
        window.clearInterval(timer);
        timer = undefined;
      }
    }

    // A phone in a pocket should not be calling the API all day. When the
    // tab comes back, catch up at once rather than waiting out the 30
    // seconds: the numbers on screen are as old as the sleep was long.
    function onVisibilityChange() {
      if (document.hidden) {
        stopTimer();
        return;
      }

      loadProducts(organizationId);
      startTimer();
    }

    startTimer();
    document.addEventListener("visibilitychange", onVisibilityChange);

    // React runs this when the till closes or the shop changes, so timers
    // can never pile up one on top of another.
    return () => {
      stopTimer();
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId]);

  // Asks the server again who this is: after the owner changes what the
  // shop calls its things, so every screen picks up the new word.
  async function refreshUser() {
    try {
      const me = await apiFetch(`${API}/api/auth/me`, { credentials: "include" });
      if (me.status === 401) {
        loginEnded();
        return;
      }
      if (me.ok) {
        setUser(await me.json());
      }
    } catch {
      // The word stays as it was until the next start-up.
    }
  }

  async function signOut() {
    try {
      await apiFetch(`${API}/api/auth/logout`, { method: "POST", credentials: "include" }, 15000);
    } catch {
      // Salio cannot be reached, so the login could not be ended. Stay as
      // we are, carts and all: the banner at the top says why.
      return;
    }
    setProducts([]);
    setShelfLoaded(false);
    // Signing out forgets this person's carts in this shop. (Locking does
    // not.) Other people's saved carts on this tablet are left alone.
    if (cartsFor !== null) {
      clearCarts(cartsFor);
    }
    setCarts([[]]);
    setActiveCart(0);
    // The phone stays trusted, so the PIN works next time.
    loginEnded();
  }

  // Locks the till now: the Lock button, or useAutoLock below. Shows a
  // plain screen at once, then the PIN pad if this phone has a PIN, or
  // email and password if it does not (safer than not locking at all).
  async function lockNow() {
    setLock({ kind: "checking" });
    try {
      const pinOwner = await devicePinOwner();
      setLock(pinOwner !== null ? { kind: "pin", name: pinOwner } : { kind: "password", message: "" });
    } catch {
      // Salio cannot be reached. Still locked: the PIN pad, which says so
      // when tried and offers email and password.
      setLock({ kind: "pin", name: user?.fullName ?? "" });
    }
  }

  // The lock screen let someone in. The server started a fresh login for
  // whoever that is, which may not be the person who locked it.
  function unlocked(result: SignedInUser & { pinSet?: boolean }) {
    setLock(null);

    if (user !== null && result.id !== user.id) {
      // A different person: start them clean, as a new sign-in. Their own
      // carts are read in by the "carts follow the person" code above, and
      // nothing on screen from the last person is kept.
      setView("till");
      setShopId(null);
      setProducts([]);
      setShelfLoaded(false);
      setReceipt(null);
      setChoosingCustomer(false);
      setQuickAddText(null);
      setAddingToShelf(false);
      setChoosingHowToAdd(false);
      setSearch("");
      setSaleProblem(null);
      setStockInPart(undefined);
      setAddFirstPart(false);
      setNeedingCostCount(0);
    }

    // A password unlock on a phone with no PIN offers one, as any password
    // sign-in does. A PIN unlock never asks (pinSet is left out or true).
    setChoosingPin(result.pinSet === false);
    signedIn(result);
  }

  // Locking by itself: idleness, or coming back from sleep. Never over the
  // choose-a-password screen or the PIN offer, and never twice.
  useAutoLock({
    enabled: user !== null && lock === null && !user.mustChangePassword && !choosingPin,
    anyCartHasItems: !allCartsEmpty(carts),
    onLock: lockNow,
  });

  const visible = products.filter((p) => {
    const q = search.toLowerCase();
    return p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q);
  });

  // Replaces only the active cart and leaves the others exactly as they were.
  function setActiveLines(newLines: CartLine[]) {
    setSaleProblem(null);
    setCarts(carts.map((cart, index) => (index === activeCart ? newLines : cart)));
  }

  function addToCart(product: Product) {
    setActiveLines([
      ...lines,
      {
        productId: product.id,
        name: product.name,
        quantity: 1,
        unitPriceMinor: product.sellPriceMinor,
      },
    ]);
  }

  // A part added from the search: straight into the cart being served, and
  // onto the shelf at once, before the reload brings the server's own copy.
  function quickAdded(added: QuickAdded, quantity: number | null) {
    const product: Product = {
      id: added.id,
      sku: added.sku,
      name: added.name,
      sellPriceMinor: added.sellPriceMinor,
      binLocation: null,
      quantityOnHand: quantity ?? 0,
      needsReceiving: false,
      averageCostMinor: null,
    };

    setProducts([...products, product]);
    // From a search the part is for the customer at the counter. From
    // "Add a part" it is only being put on the shelf.
    if (!addingToShelf) {
      addToCart(product);
    }
    setQuickAddText(null);
    setAddingToShelf(false);
    setSearch("");

    if (shopId !== null) {
      loadProducts(shopId);
    }
  }

  function updateLine(index: number, changes: Partial<CartLine>) {
    setActiveLines(lines.map((line, i) => (i === index ? { ...line, ...changes } : line)));
  }

  function removeLine(index: number) {
    setActiveLines(lines.filter((_, i) => i !== index));
  }

  function newCart() {
    setSaleProblem(null);

    // A different customer. Never reuse the last checkout's id, or Salio
    // would hand back the previous sale's receipt.
    checkoutId.current = null;
    setCarts([...carts, []]);
    setActiveCart(carts.length);
  }

  let total = 0;
  for (const line of lines) {
    total = total + line.quantity * line.unitPriceMinor;
  }

  // method: 0 Cash, 1 M-Pesa, 3 Credit (the server's numbers; 2 is Card).
  // A credit sale is for the whole total and names the customer.
  async function pay(method: number, customer?: Customer) {
  setPaying(true);
  setSaleProblem(null);

  // Kept from the last try if there was one, so a retry is recognised.
  if (checkoutId.current === null) {
    checkoutId.current = newCheckoutId();
  }

  const body = {
    organizationId: shopId,
    saleDate: new Date().toISOString().slice(0, 10),
    lines: lines.map((line) => ({
      productId: line.productId,
      quantity: line.quantity,
      unitPriceMinor: line.unitPriceMinor,
      taxBand: "A",
    })),
    payments: [{ method: method, amountMinor: total, reference: null }],
    customerId: customer?.id ?? null,
    clientRequestId: checkoutId.current,
  };

  let response: Response;
  try {
    response = await apiFetch(
      `${API}/api/sales`,
      {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      },
      // 30 seconds, then stop waiting: the Pay buttons must come back.
      30000,
    );
  } catch {
    // The cart is untouched. If the tablet itself has no internet, the sale
    // never left it. Otherwise it may have reached Salio with only the
    // answer lost, so a second try could record the sale twice.
    setSaleProblem(navigator.onLine === false ? SALE_NOT_SENT : SALE_UNKNOWN);
    setPaying(false);
    return;
  }

  if (isGatewayDown(response)) {
    // Salio's front door answered, Salio did not. Same doubt as above.
    setSaleProblem(SALE_UNKNOWN);
    setPaying(false);
    return;
  }

  try {
    if (response.ok) {
      const sale = await response.json();

      // Landed. The next customer starts a new checkout.
      checkoutId.current = null;

      // What was sold, taken from the cart before it is cleared below.
      setReceipt({
        saleNumber: sale.saleNumber,
        occurredAt: sale.occurredAt,
        lines: lines.map((line) => ({
          name: line.name,
          quantity: line.quantity,
          unitPriceMinor: line.unitPriceMinor,
        })),
        payments: [
          { method: method === 3 ? "Credit" : method === 0 ? "Cash" : "Mpesa", amountMinor: total },
        ],
        totalMinor: sale.totalMinor,
        customerName: sale.customerName ?? null,
        customerBalanceMinor: sale.customerBalanceMinor ?? null,
      });

      const remaining = carts.filter((_, index) => index !== activeCart);
      setCarts(remaining.length > 0 ? remaining : [[]]);
      setActiveCart(0);

      // The cart is closed and the next customer can be served now. The
      // shelf catches up behind them: this is deliberately not awaited, so
      // selling never waits for the reload.
      if (shopId !== null) {
        loadProducts(shopId);
      }
    } else if (response.status === 401) {
      alert("Signed out. Sale NOT saved.");
      loginEnded();
    } else {
      setSaleProblem({
        title: "Sale NOT saved",
        text: await problemText(response, "Cart kept. Try again"),
      });
    }
  } catch {
    // The answer came but could not be read, so what happened is not known.
    setSaleProblem(SALE_UNKNOWN);
  } finally {
    setPaying(false);
  }
}

  // Start-up: asking the server whether this phone is still signed in.
  const showChecking = useShowLoader(checkingLogin);

  if (checkingLogin) {
    return showChecking ? <SalioLoader size="inline" message="Opening Salio…" /> : null;
  }

  // Salio could not be asked who this is. Not the sign-in screen: nobody
  // was signed out, and the carts saved on this tablet are untouched.
  if (cantStart && user === null) {
    return (
      <div className="flex min-h-full flex-col items-center justify-center bg-background px-4 py-6 text-center text-foreground">
        <h1 className="text-2xl font-bold">Can't reach Salio</h1>
        <p className="mt-2 max-w-md text-lg">
          Your carts are kept
        </p>
        <button
          onClick={tryStartAgain}
          className="mt-6 h-14 w-full max-w-sm rounded-xl bg-primary text-xl font-bold text-primary-foreground"
        >
          Try again
        </button>
      </div>
    );
  }

  if (user === null) {
    // A client setting up their own shop with a code (locked rule 14). It
    // signs in with a password at step 3, the same as the login screen,
    // but hands over only when step 4 is done: until then `user` stays
    // null here and the setup screen keeps the phone.
    if (signInView === "setup") {
      return (
        <SetupScreen
          codeFromLink={setupCodeInLink}
          onBack={() => {
            setPath("/");
            setSignInView("login");
          }}
          onSignedIn={(result) => {
            setPath("/");
            setSignInView("login");
            setChoosingPin(!result.pinSet);
            signedIn(result);
            // Called at the end of setup (after the first few things for
            // a Sales shop, which may be skipped). Then the PIN, then the
            // till. An empty shelf says what to do first.
            setView("till");
          }}
        />
      );
    }

    if (signInView === "pin") {
      return (
        <PinUnlockScreen
          name={pinName}
          onUnlocked={signedIn}
          onUsePassword={(message) => {
            setLoginMessage(message);
            setSignInView("login");
          }}
        />
      );
    }

    return (
      <LoginScreen
        message={loginMessage}
        onSignedIn={(result) => {
          setLoginMessage("");
          // A phone that already has a PIN goes straight on. Otherwise a
          // PIN is offered, and may be declined.
          setChoosingPin(!result.pinSet);
          signedIn(result);
        }}
      />
    );
  }

  // The lock screen, when locked. The PIN pad if this phone has a PIN, with
  // "Use email and password instead" under it; email and password if not.
  // Either way, whoever gets in goes through unlocked() above.
  let lockScreen: ReactNode = null;
  if (lock?.kind === "checking") {
    // A moment while the server says whether this phone has a PIN. Plain,
    // so nothing of the till shows.
    lockScreen = <div className="min-h-full bg-background" />;
  } else if (lock?.kind === "pin") {
    lockScreen = (
      <PinUnlockScreen
        locked
        name={lock.name}
        onUnlocked={unlocked}
        onUsePassword={(message) => setLock({ kind: "password", message })}
      />
    );
  } else if (lock?.kind === "password") {
    lockScreen = <LoginScreen locked message={lock.message} onSignedIn={unlocked} />;
  }

  // Every screen from here on is drawn through this. While locked, the
  // screen is hidden but kept, exactly as it was (a half-typed delivery,
  // the carts, the open shop), and the lock screen shows instead.
  // "contents" means the wrapper adds nothing to the layout when unlocked.
  function behindLock(node: ReactNode) {
    return (
      <>
        <div className={lock === null ? "contents" : "hidden"}>{node}</div>
        {lockScreen}
      </>
    );
  }

  // A temporary password: this screen and nothing else. No tabs, no way
  // round it. The server refuses every other call until it is done.
  if (user.mustChangePassword) {
    return behindLock(
      <ChoosePasswordScreen
        userName={user.fullName}
        onChanged={(changed) => {
          signedIn(changed);
          // The shelf was refused while the password was temporary, and
          // the shop id has not changed, so nothing else would reload it.
          if (shopId !== null) {
            loadProducts(shopId);
          }
        }}
        onExpired={loginEnded}
      />,
    );
  }

  // After the password (and a new password, if one was owed): the PIN for
  // next time. An offer: "Not now" goes on to the till (or the shop
  // chooser, for someone with more than one shop).
  if (choosingPin) {
    return behindLock(
      <ChoosePinScreen
        onChosen={() => setChoosingPin(false)}
        onExpired={() => {
          setChoosingPin(false);
          loginEnded();
        }}
      />,
    );
  }

  if (shopId === null) {
    return behindLock(<ShopChooser shops={user.shops} onChoose={setShopId} />);
  }

  const shop = user.shops.find((s) => s.organizationId === shopId);
  const shopName = shop?.name ?? "";

  // What this shop calls its things ("Parts", "Products"), set at setup.
  const words = itemWords(shop?.itemWord);

  // "Sales" or "Services": only the order of the screens (decision D14).
  const businessKind = shop?.businessKind ?? "Sales";

  // Receiving stock is money and stock value: Owner only (locked rule 13).
  // The server refuses a Cashier anyway; this keeps them from reaching it.
  const isOwner = shop?.role === 0;

  // The demo invoice reader is for the developer only: in a real build the
  // menu entry shows only when a real reader is set up.
  const showInvoiceReader = isOwner && (readerReal || import.meta.env.DEV);

  // Every place this person may open, for the sidebar, More and the tabs.
  const menu = buildMenu({
    isOwner,
    words,
    businessKind,
    showInvoiceReader,
    needingCostCount: isOwner ? needingCostCount : 0,
  });

  // A place chosen in the sidebar or on More.
  function choosePlace(chosen: NavPlace) {
    setAddFirstPart(false);
    setStockInPart(undefined);
    setChoosingHowToAdd(false);
    setView(PLACE_VIEWS[chosen]);
  }

  // "Add a part": the quick-add form with nothing typed in yet.
  function addOnePart() {
    setChoosingHowToAdd(false);
    setAddingToShelf(true);
    setQuickAddText("");
  }

  // The shelf has loaded and holds no active parts. Never true while it is
  // still loading or the load failed: shelfLoaded is only set by an answer.
  const shelfEmpty = shelfLoaded && products.length === 0;

  if (choosingCustomer) {
    return behindLock(
      <CustomerPicker
        shopId={shopId}
        totalMinor={total}
        onCancel={() => setChoosingCustomer(false)}
        onConfirm={(customer) => {
          setChoosingCustomer(false);
          pay(3, customer);
        }}
        onExpired={() => {
          setChoosingCustomer(false);
          loginEnded();
        }}
      />,
    );
  }

  if (quickAddText !== null) {
    // One screen, then straight back to the till: with the part in the cart
    // when it came from a search, or just on the shelf from "Add a part".
    return behindLock(
      <QuickAddForm
        shopId={shopId}
        typed={quickAddText}
        heading={addingToShelf ? `Add ${words.singular}` : undefined}
        saveLabel={addingToShelf ? "Save" : undefined}
        isOwner={isOwner}
        onAdded={quickAdded}
        onCancel={() => {
          setQuickAddText(null);
          setAddingToShelf(false);
        }}
        onExpired={() => {
          setQuickAddText(null);
          setAddingToShelf(false);
          loginEnded();
        }}
      />,
    );
  }

  if (receipt !== null) {
    // Done closes it. The cart was already emptied when the sale saved.
    // No tabs here: the phone is turned round to the customer.
    return behindLock(
      // overflow-hidden: while the receipt rises, its first 24px sit below
      // the screen; without this a scrollbar flashes and Done narrows.
      <div className="h-full overflow-hidden">
        <Receipt shopName={shopName} {...receipt} justSold onDone={() => setReceipt(null)} />
      </div>,
    );
  }

  // From 768px up the sidebar lists everything More held, so there is no
  // More screen: a phone turned sideways on More lands on Sell instead.
  const current: View = isWide && view === "more" ? "till" : view;

  // What fills the space beside the sidebar (above the tabs on a phone),
  // whether the phone's bottom tabs show, and which sidebar item is lit.
  let screen: ReactNode;
  let withTabs = false;
  let place: NavPlace = "sell";

  // Stock in, Money out, Parts, People and End of month are reached from
  // More on a phone and fill the screen there, with no tabs: a stray tap on
  // a tab would throw away a half-typed delivery. On a tablet the sidebar
  // stays beside them.
  if (current === "stockIn" && isOwner) {
    place = "stockIn";
    screen = (
      <StockInScreen
        // A new part to start with is a fresh delivery.
        key={stockInPart?.id ?? "delivery"}
        shopId={shopId}
        products={products}
        startWith={stockInPart}
        backLabel={stockInPart ? "Back" : "More"}
        onBack={() => setView(stockInPart ? "needingCost" : "more")}
        onReceived={() => loadProducts(shopId)}
        onExpired={loginEnded}
      />
    );
  } else if (current === "needingCost" && isOwner) {
    // Cost and profit: Owner only (locked rule 13). A row opens Stock in
    // with that part already on the delivery.
    place = "needingCost";
    screen = (
      <NeedingCostScreen
        shopId={shopId}
        words={words}
        onBack={() => setView("more")}
        onChoose={(item) => {
          // The shelf's copy carries the selling price for Stock in's warning.
          // Inactive parts are not on the list (D9), so it is normally there.
          setStockInPart(
            products.find((p) => p.id === item.productId) ?? {
              id: item.productId,
              sku: item.sku,
              name: item.name,
              sellPriceMinor: 0,
              binLocation: null,
              quantityOnHand: item.quantityOnHand,
              needsReceiving: false,
              averageCostMinor: null,
            },
          );
          setView("stockIn");
        }}
        onExpired={loginEnded}
      />
    );
  } else if (current === "importParts" && isOwner) {
    // A file of parts can carry cost and opening stock: Owner only.
    place = "importParts";
    screen = (
      <ImportScreen
        shopId={shopId}
        shopName={shopName}
        words={words}
        shelfEmpty={shelfEmpty}
        onBack={() => setView("more")}
        onHistory={() => setView("importHistory")}
        onImported={() => loadProducts(shopId)}
        onExpired={loginEnded}
      />
    );
  } else if (current === "importHistory" && isOwner) {
    // Reached from the Import screen, so Import stays lit in the menu.
    place = "importParts";
    screen = (
      <ImportHistoryScreen
        shopId={shopId}
        words={words}
        onBack={() => setView("more")}
        onImport={() => setView("importParts")}
        onUndone={() => loadProducts(shopId)}
        onExpired={loginEnded}
      />
    );
  } else if (current === "readInvoice" && showInvoiceReader) {
    // Cost and stock: Owner only. Committing reloads the shelf.
    place = "readInvoice";
    screen = (
      <InvoiceScreen
        shopId={shopId}
        words={words}
        products={products}
        onBack={() => setView("more")}
        onReceived={() => loadProducts(shopId)}
        onExpired={loginEnded}
      />
    );
  } else if (current === "openingStock" && isOwner) {
    // Stock value: Owner only. Saving reloads the till's shelf so the new
    // quantities show there.
    place = "openingStock";
    screen = (
      <OpeningStockScreen
        shopId={shopId}
        words={words}
        onBack={() => setView("more")}
        onOpenParts={() => setView("parts")}
        onSaved={() => loadProducts(shopId)}
        onExpired={loginEnded}
      />
    );
  } else if (current === "expenses" && isOwner) {
    // Money out is money: Owner only, like Stock in.
    place = "moneyOut";
    screen = (
      <ExpensesScreen
        shopId={shopId}
        onBack={() => setView("more")}
        onExpired={loginEnded}
      />
    );
  } else if (current === "parts" && isOwner) {
    // A change reloads the till's shelf so it shows new names and prices
    // straight away.
    place = "parts";
    screen = (
      <PartsScreen
        shopId={shopId}
        words={words}
        startAdding={addFirstPart}
        onBack={() => {
          setAddFirstPart(false);
          setView("more");
        }}
        onChanged={() => {
          setAddFirstPart(false);
          loadProducts(shopId);
        }}
        onWordChanged={refreshUser}
        onExpired={loginEnded}
      />
    );
  } else if (current === "people" && isOwner) {
    // Who can sign in: Owner only.
    place = "people";
    screen = (
      <PeopleScreen
        shopId={shopId}
        onBack={() => setView("more")}
        onExpired={loginEnded}
      />
    );
  } else if (current === "shopWord" && isOwner) {
    // What Salio calls what the shop sells: Owner only.
    place = "shopWord";
    screen = (
      <ShopWordScreen
        shopId={shopId}
        words={words}
        onBack={() => setView("more")}
        onSaved={refreshUser}
        onExpired={loginEnded}
      />
    );
  } else if ((current === "month" || current === "tax") && isOwner) {
    // Month end is money and tax: Owner only (locked rule 13). "Tax to
    // file" is the same screen, opened on this month at its tax part. The
    // key makes moving between the two start the screen afresh.
    place = current === "tax" ? "tax" : "month";
    screen = (
      <MonthScreen
        key={current}
        startAtTax={current === "tax"}
        shopId={shopId}
        userName={user.fullName}
        words={words}
        onBack={() => setView("more")}
        onExpired={loginEnded}
      />
    );
  } else {
    withTabs = true;
  }

  // Which tab is lit. Owed is money, Owner only: a Cashier has no Owed tab,
  // so for them "owed" can only mean the till.
  let activeTab: Tab = "sell";

  if (current === "sales") {
    activeTab = "today";
  } else if (current === "owed" && isOwner) {
    activeTab = "owed";
  } else if (current === "more") {
    activeTab = "more";
  }

  if (withTabs) {
    place = activeTab;
  }

  // The four tab screens each fill the space above the tab bar. The screens
  // without tabs were chosen above.
  if (withTabs && activeTab === "owed") {
    screen = <OwedScreen shopId={shopId} onExpired={loginEnded} />;
  } else if (withTabs && activeTab === "today") {
    screen = <SalesScreen shopId={shopId} shopName={shopName} onExpired={loginEnded} />;
  } else if (withTabs && activeTab === "more") {
    screen = (
        <MoreScreen
          shopName={shopName}
          userName={user.fullName}
          words={words}
          menu={menu}
          onChoose={choosePlace}
          onLock={lockNow}
          onSignOut={signOut}
        />
      );
    } else if (withTabs && shelfEmpty && allCartsEmpty(carts)) {
      // A shop with nothing to sell yet: not an empty list with a search
      // box over it, but the ways to put parts in. (If a cart still holds
      // lines, the normal screen shows instead, so it can be paid for.)
      screen = (
        <div className="flex h-full flex-col bg-background text-foreground">
          <div className="shrink-0 border-b border-border bg-surface px-3 py-2 md:hidden">
            <span className="block truncate text-sm font-semibold">
              {shopName} · {user.fullName}
            </span>
          </div>

          <main className="min-h-0 flex-1 overflow-y-auto px-4 py-6 md:px-8 md:py-10">
            <h1 className="text-2xl font-bold">Your shelf is empty</h1>

            <div className="mt-6">
              <AddPartsChoices
                words={words}
                isOwner={isOwner}
                readerReal={readerReal}
                sideBySide
                onAddOne={addOnePart}
                onImport={() => setView("importParts")}
                onReadInvoice={() => setView("readInvoice")}
              />
            </div>

            <p className="mt-6 max-w-2xl text-base font-semibold">
              No rush. Add {words.plural} as they sell.
            </p>
          </main>
        </div>
      );
    } else if (withTabs) {
      screen = (
      // One column on a phone and a small tablet. From 1024px, two panes:
      // the shelf on the left, the cart always in sight on the right.
      <div className="flex h-full flex-col bg-background text-foreground lg:flex-row">
        <div className="flex min-h-0 min-w-0 flex-1 flex-col">
        {/* Who is serving, in which shop. One line: the way out is under More.
            A tablet's sidebar already says both, so not repeated there. */}
        <div className="shrink-0 border-b border-border bg-surface px-3 py-2 md:hidden">
          <span className="block truncate text-sm font-semibold">
            {shopName} · {user.fullName}
          </span>
        </div>

        {/* Search: always at the top, never scrolls away. */}
        <header className="shrink-0 border-b border-border bg-surface p-2">
          <div className="flex gap-2">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={`Search ${words.plural}`}
              className="h-12 min-w-0 flex-1 rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
                placeholder:text-muted-foreground focus:border-primary focus:outline-none"
            />
            {/* An Owner chooses how; a Cashier has one way, so it opens. */}
            <button
              onClick={() => (isOwner ? setChoosingHowToAdd(true) : addOnePart())}
              className="h-12 shrink-0 rounded-lg border-2 border-primary px-3 text-base font-bold text-primary
                active:bg-primary-soft"
            >
              Add {words.plural}
            </button>
          </div>
          {shelfLoaded && (
            <p className="mt-1 px-1 text-sm text-muted-foreground">
              {products.length.toLocaleString("en-KE")} {products.length === 1 ? words.singular : words.plural}
            </p>
          )}
        </header>

        {/* Results: the only part of the screen that scrolls. */}
        <main className="min-h-0 flex-1 overflow-y-auto">
          {visible.map((product) => (
            <button
              key={product.id}
              onClick={() => addToCart(product)}
              className="flex min-h-14 w-full items-center justify-between gap-3 border-b
                border-border bg-surface px-3 py-2 text-left active:bg-primary-soft"
            >
              <span className="min-w-0">
                <span className="block truncate text-base font-semibold">{product.name}</span>
                <span className="mt-1 flex flex-wrap items-center gap-1.5 text-sm">
                  <span className="rounded bg-primary-soft px-1.5 py-0.5 font-bold text-primary">
                    {product.binLocation ?? "No shelf"}
                  </span>
                  <span className="text-muted-foreground">{product.sku}</span>
                  {product.needsReceiving ? (
                    <span className="rounded bg-warning px-1.5 py-0.5 font-bold text-warning-foreground">
                      Needs receiving
                    </span>
                  ) : product.quantityOnHand <= 0 ? (
                    <span className="rounded bg-warning px-1.5 py-0.5 font-bold text-warning-foreground">
                      Out of stock
                    </span>
                  ) : (
                    <span className="font-money font-semibold text-muted-foreground">
                      {product.quantityOnHand} left
                    </span>
                  )}
                </span>
              </span>
              <span className="shrink-0 font-money text-lg font-bold">{kes(product.sellPriceMinor)}</span>
            </button>
          ))}

          {/* Nothing matches what was typed: offer to add it, there and then.
              The till never refuses a sale, so a part the shelf does not
              know yet is one short form away from the cart. */}
          {visible.length === 0 && search.trim() !== "" && (
            <div className="mx-auto max-w-sm px-4 py-6">
              <button
                onClick={() => setQuickAddText(search.trim())}
                className="h-14 w-full truncate rounded-xl bg-primary px-4 text-xl font-bold text-primary-foreground"
              >
                Add "{search.trim()}"
              </button>
            </div>
          )}
        </main>
        </div>

        {/* Cart: pinned to the bottom, so the total is never scrolled off.
            From 1024px it is the right-hand pane instead, full height, with
            the lines filling the middle and the total and Pay at the foot. */}
        <section className="shrink-0 border-t-2 border-border bg-surface lg:flex lg:w-96 lg:flex-col lg:border-l-2 lg:border-t-0">
          {/* One tab per customer. The count stops a half-served cart being forgotten. */}
          <div className="flex gap-1 overflow-x-auto border-b border-border p-1.5">
            {carts.map((cart, index) => (
              <button
                key={index}
                onClick={() => {
                  setActiveCart(index);
                  setSaleProblem(null);
                }}
                className={
                  "flex min-h-11 shrink-0 items-center gap-1.5 rounded-lg px-3 text-base font-semibold " +
                  (index === activeCart
                    ? "bg-primary text-primary-foreground"
                    : "border-2 border-border-strong bg-surface text-foreground")
                }
              >
                Customer {index + 1}
                <span
                  className={
                    "font-money rounded px-1.5 text-sm font-bold " +
                    (index === activeCart
                      ? "bg-primary-foreground text-primary"
                      : "bg-primary-soft text-primary")
                  }
                >
                  {cart.length}
                </span>
              </button>
            ))}

            <button
              onClick={newCart}
              className="min-h-11 shrink-0 rounded-lg border-2 border-primary px-3
                text-base font-bold text-primary"
            >
              New
            </button>
          </div>

          <div className="max-h-32 overflow-y-auto max-lg:[@media(min-height:900px)]:max-h-80 lg:max-h-none lg:min-h-0 lg:flex-1">
            {/* The cart pane is always showing on a wide screen, so say what
                it is for while it is empty. */}
            {lines.length === 0 && (
              <p className="hidden p-4 text-base md:text-sm text-muted-foreground lg:block">
                Cart empty
              </p>
            )}
            {lines.map((line, index) => (
              // motion-line-in plays once, when a line first appears.
              <div
                key={index}
                className={
                  "motion-line-in flex flex-wrap items-center gap-2 border-b border-border px-2 py-1.5 " +
                  (lineBelowLowest(line, products) ? "border-l-4 border-l-error" : "")
                }
              >
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-base">{line.name}</span>
                  {/* Selling under cost is allowed - the shop may have a reason.
                      Say so and let it through: a till that refuses a sale gets
                      worked around, and then there is no record of it at all. */}
                  {belowCost(line, products) && (
                    <span className="block text-sm font-semibold text-warning-strong">
                      Below cost (KES {kes(costOf(line, products))})
                    </span>
                  )}
                  {/* Locked rule 16: stock may go below zero. Warn now, never
                      block - the Pay buttons stay as they are. */}
                  {shelfShowsNone(line, products) && (
                    <span className="block text-sm font-semibold text-warning-strong">
                      Needs receiving: {shelfShowsNone(line, products)?.quantityOnHand} in stock
                      {isOwner && ". Cost unknown"}
                    </span>
                  )}
                </span>
                <input
                  type="number"
                  inputMode="numeric"
                  value={line.quantity}
                  onChange={(e) => updateLine(index, { quantity: parseInt(e.target.value) || 0 })}
                  className="h-11 w-14 rounded-lg border-2 border-border-strong text-center font-money text-base"
                />
                <span className="w-20 shrink-0 text-right font-money text-base font-semibold">
                  {kes(line.unitPriceMinor * line.quantity)}
                </span>
                <button
                  onClick={() => removeLine(index)}
                  className="h-11 w-11 shrink-0 rounded-lg border-2 border-border-strong text-xl font-bold"
                  aria-label={"Remove " + line.name}
                >
                  ×
                </button>
                {/* The price: tapped, never typed. Its own row under the line. */}
                <div className="basis-full">
                  <CartPriceButtons
                    name={line.name}
                    askingMinor={askingOf(line, products)}
                    stopMinor={stopOf(line, products)}
                    agreedMinor={line.unitPriceMinor}
                    costMinor={confirmedCostOf(line, products)}
                    onAgree={(priceMinor) => updateLine(index, { unitPriceMinor: priceMinor })}
                  />
                </div>
              </div>
            ))}
          </div>

          <div className="flex items-baseline justify-between px-3 py-2">
            <span className="text-base font-semibold">Total</span>
            {/* The new total shows at once. key={total} only replays the
                fading highlight behind it; -mx-1 px-1 gives the highlight
                room without moving the figure. */}
            <span key={total} className="motion-highlight -mx-1 px-1 font-money text-2xl font-bold">
              KES {kes(total)}
            </span>
          </div>

          {/* A refused sale. Red, in words, and the cart is still here: it must
              never look like a sale that went through. */}
          {saleProblem !== null && (
            <div role="alert" className="mx-2 mb-2 rounded-lg bg-error px-3 py-2 text-error-foreground">
              <p className="text-base font-bold">{saleProblem.title}</p>
              <p className="text-base">{saleProblem.text}</p>
            </div>
          )}

          {/* Only when there is something to pay for. Three disabled buttons
              on an empty cart did nothing and took the space the parts list
              needs. On a phone they are not there at all until then. In the
              wide cart pane their space is kept, invisible, so the total
              does not jump up and down as the first part goes in. */}
          {(
            <div
              aria-hidden={lines.length === 0 ? true : undefined}
              className={
                lines.length > 0 ? "space-y-2 px-2 pb-2" : "hidden space-y-2 px-2 pb-2 lg:invisible lg:block"
              }
            >
              <button
                onClick={() => pay(0)}
                disabled={lines.length === 0 || paying}
                className="h-14 w-full rounded-xl bg-primary text-xl font-bold
                  text-primary-foreground disabled:opacity-40"
              >
                Pay Cash
              </button>
              <button
                onClick={() => pay(1)}
                disabled={lines.length === 0 || paying}
                className="h-14 w-full rounded-xl border-2 border-primary text-xl font-bold
                  text-primary disabled:opacity-40"
              >
                Pay M-Pesa
              </button>
              <button
                onClick={() => {
                  setSaleProblem(null);
                  setChoosingCustomer(true);
                }}
                disabled={lines.length === 0 || paying}
                className="h-14 w-full rounded-xl border-2 border-warning-strong text-xl font-bold
                  text-foreground disabled:opacity-40"
              >
                Credit
              </button>
            </div>
          )}
        </section>

        {/* "+ Add parts": the same choices as the empty shelf, in a sheet. */}
        {choosingHowToAdd && (
          <div
            onClick={() => setChoosingHowToAdd(false)}
            className="fixed inset-0 z-20 flex items-end justify-center bg-foreground/50 md:items-center"
          >
            <div
              role="dialog"
              aria-label={`Add ${words.plural}`}
              onClick={(e) => e.stopPropagation()}
              className="w-full max-w-md space-y-3 rounded-t-2xl bg-surface p-3 md:rounded-2xl"
            >
              <p className="text-xl font-bold">Add {words.plural}</p>
              <AddPartsChoices
                words={words}
                isOwner={isOwner}
                readerReal={readerReal}
                sideBySide={false}
                onAddOne={addOnePart}
                onImport={() => {
                  setChoosingHowToAdd(false);
                  setView("importParts");
                }}
                onReadInvoice={() => {
                  setChoosingHowToAdd(false);
                  setView("readInvoice");
                }}
              />
              <button
                onClick={() => setChoosingHowToAdd(false)}
                className="min-h-12 w-full rounded-xl border-2 border-border-strong bg-surface text-base font-bold"
              >
                Cancel
              </button>
            </div>
          </div>
        )}
      </div>
    );
  }

  // Below 768px: the screen, then the bottom tabs on the four tab screens,
  // exactly as before. From 768px: the sidebar on the left instead.
  //
  // Every screen except Sell is held to 48rem (768px) wide from 768px up and
  // centred: a list of name-on-the-left, amount-on-the-right rows read
  // across 1100px puts the amount too far from its name. Sell uses the
  // whole width for the shelf and the cart.
  return behindLock(
    <div className="flex h-full bg-background text-foreground">
      <Sidebar
        shopName={shopName}
        userName={user.fullName}
        menu={menu}
        active={place}
        onChoose={choosePlace}
        onLock={lockNow}
        onSignOut={signOut}
      />
      <div className="flex min-w-0 flex-1 flex-col">
        {/* key: a new place starts a fresh 150ms fade (motion-screen). */}
        <div
          key={place}
          className={
            "motion-screen min-h-0 flex-1" + (place === "sell" ? "" : " md:mx-auto md:w-full md:max-w-3xl")
          }
        >
          {screen}
        </div>
        {/* The install nudge: on Today, Owed and More only. Never on Sell,
            where it could sit over the cart or Pay, and never over a form. */}
        {withTabs && place !== "sell" && (
          <InstallPill cartHasItems={carts.some((cart) => cart.length > 0)} />
        )}
        {withTabs && (
          <div className="shrink-0 md:hidden">
            <BottomNav
              active={activeTab}
              top={menu.top}
              moreBadge={isOwner ? needingCostCount : 0}
              onChoose={(tab) => setView(TAB_VIEWS[tab])}
            />
          </div>
        )}
      </div>
    </div>,
  );
}
