import { useState, useEffect, useRef } from "react";
import LoginScreen from "./LoginScreen";
import CreateShopScreen from "./CreateShopScreen";
import ShopChooser from "./ShopChooser";
import SalesScreen from "./SalesScreen";
import StockInScreen from "./StockInScreen";
import ExpensesScreen from "./ExpensesScreen";
import CustomerPicker, { type Customer } from "./CustomerPicker";
import OwedScreen from "./OwedScreen";
import Receipt, { type ReceiptLine, type ReceiptPayment } from "./Receipt";
import { API, kes, type SignedInUser } from "./auth";

type Product = {
  id: string;
  sku: string;
  name: string;
  sellPriceMinor: number;
  binLocation: string | null;
  quantityOnHand: number;
  averageCostMinor: number | null;
};

type CartLine = {
  productId: string;
  name: string;
  quantity: number;
  unitPriceMinor: number;
};

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

// The server's own words for why a sale was refused. Its ProblemDetails
// "detail" is written for the cashier; the raw body is not.
async function refusalText(response: Response) {
  try {
    const problem = await response.json();

    if (typeof problem.detail === "string" && problem.detail !== "") {
      return problem.detail;
    }

    // Model validation failures carry their reasons in "errors" instead.
    if (problem.errors) {
      const first = Object.values(problem.errors).flat()[0];
      if (typeof first === "string") {
        return first;
      }
    }

    if (typeof problem.title === "string") {
      return problem.title;
    }
  } catch {
    // Not JSON: fall through.
  }

  return "The server refused the sale.";
}

export default function App() {
  // Who is signed in, and which of their shops is open. Both come from the
  // server: the till no longer knows an organisation id of its own.
  const [user, setUser] = useState<SignedInUser | null>(null);
  const [shopId, setShopId] = useState<string | null>(null);
  const [checkingLogin, setCheckingLogin] = useState(true);

  // Which screen is showing. The till is what opens: selling comes first.
  const [view, setView] = useState<"till" | "sales" | "stockIn" | "expenses" | "owed">("till");

  // Before signing in there are two possible screens, and creating a shop is
  // reachable in development only (locked rule 14: no self-signup).
  const [signInView, setSignInView] = useState<"login" | "createShop">("login");

  const [products, setProducts] = useState<Product[]>([]);
  const [search, setSearch] = useState("");

  // One cart per customer at the counter, and which one is on screen.
  const [carts, setCarts] = useState<CartLine[][]>([[]]);
  const [activeCart, setActiveCart] = useState(0);
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
  const [saleProblem, setSaleProblem] = useState("");

  // The cart being served right now. Everything below reads this.
  const lines = carts[activeCart] ?? [];

  // True while a shelf reload is in flight. A ref, not state, because
  // changing it must not re-render anything.
  const reloading = useRef(false);

  // On start-up, ask whether the cookie from last time is still good.
  useEffect(() => {
    fetch(`${API}/api/auth/me`, { credentials: "include" })
      .then((response) => (response.ok ? response.json() : null))
      .then((me: SignedInUser | null) => {
        setUser(me);
        // One shop needs no chooser.
        if (me && me.shops.length === 1) {
          setShopId(me.shops[0].organizationId);
        }
      })
      .catch(() => setUser(null))
      .finally(() => setCheckingLogin(false));
  }, []);

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
      return;
    }

    reloading.current = true;

    try {
      const response = await fetch(
        `${API}/api/products?organizationId=${organizationId}`,
        { credentials: "include" },
      );

      if (response.status === 401) {
        // Not a failed refresh: the login ran out, or an owner signed every
        // device out. The next sale would be refused anyway.
        setUser(null);
        setShopId(null);
        return;
      }

      if (!response.ok) {
        // Leave the shelf exactly as it is. Quantities go stale, which is
        // better than an empty list, and a sale that is already saved is not
        // worth an error box.
        return;
      }

      setProducts(await response.json());
    } catch {
      // Same again: keep what is on screen and say nothing.
    } finally {
      // Runs on every path, including the early returns above, so a failed
      // reload cannot jam the timer shut.
      reloading.current = false;
    }
  }

  // The shelf, for the shop that is open.
  useEffect(() => {
    if (shopId === null) {
      return;
    }

    loadProducts(shopId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopId]);

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

  async function signOut() {
    await fetch(`${API}/api/auth/logout`, {
      method: "POST",
      credentials: "include",
    });
    setUser(null);
    setShopId(null);
    setProducts([]);
    setCarts([[]]);
    setActiveCart(0);
  }

  const visible = products.filter((p) => {
    const q = search.toLowerCase();
    return p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q);
  });

  // Replaces only the active cart and leaves the others exactly as they were.
  function setActiveLines(newLines: CartLine[]) {
    setSaleProblem("");
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

  function updateLine(index: number, changes: Partial<CartLine>) {
    setActiveLines(lines.map((line, i) => (i === index ? { ...line, ...changes } : line)));
  }

  function removeLine(index: number) {
    setActiveLines(lines.filter((_, i) => i !== index));
  }

  function newCart() {
    setSaleProblem("");
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
  setSaleProblem("");

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
  };

  try {
    const response = await fetch(`${API}/api/sales`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (response.ok) {
      const sale = await response.json();

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
      alert("Your login has ended. Please sign in again.");
      setUser(null);
      setShopId(null);
    } else {
      setSaleProblem(await refusalText(response));
    }
  } catch {
    setSaleProblem("Could not reach the till server. Check the connection and try again.");
  } finally {
    setPaying(false);
  }
} 

  if (checkingLogin) {
    return (
      <p className="p-4 text-base text-muted-foreground">Checking your login…</p>
    );
  }

  if (user === null) {
    if (signInView === "createShop") {
      return <CreateShopScreen onBack={() => setSignInView("login")} />;
    }

    return (
      <LoginScreen
        onSignedIn={(signedIn) => {
          setUser(signedIn);
          if (signedIn.shops.length === 1) {
            setShopId(signedIn.shops[0].organizationId);
          }
        }}
        onCreateShop={
          import.meta.env.DEV ? () => setSignInView("createShop") : undefined
        }
      />
    );
  }

  if (shopId === null) {
    return <ShopChooser shops={user.shops} onChoose={setShopId} />;
  }

  const shop = user.shops.find((s) => s.organizationId === shopId);
  const shopName = shop?.name ?? "";

  // Receiving stock is money and stock value: Owner only (locked rule 13).
  // The server refuses a Cashier anyway; this keeps them from reaching it.
  const isOwner = shop?.role === 0;

  if (choosingCustomer) {
    return (
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
          setUser(null);
          setShopId(null);
        }}
      />
    );
  }

  if (receipt !== null) {
    // Done closes it. The cart was already emptied when the sale saved.
    return <Receipt shopName={shopName} {...receipt} onDone={() => setReceipt(null)} />;
  }

  if (view === "stockIn" && isOwner) {
    return (
      <StockInScreen
        shopId={shopId}
        products={products}
        onBack={() => setView("till")}
        onReceived={() => loadProducts(shopId)}
        onExpired={() => {
          setView("till");
          setUser(null);
          setShopId(null);
        }}
      />
    );
  }

  // Money out is money: Owner only, like Stock in.
  if (view === "expenses" && isOwner) {
    return (
      <ExpensesScreen
        shopId={shopId}
        onBack={() => setView("till")}
        onExpired={() => {
          setView("till");
          setUser(null);
          setShopId(null);
        }}
      />
    );
  }

  // Who owes what is money: Owner only.
  if (view === "owed" && isOwner) {
    return (
      <OwedScreen
        shopId={shopId}
        onBack={() => setView("till")}
        onExpired={() => {
          setView("till");
          setUser(null);
          setShopId(null);
        }}
      />
    );
  }

  if (view === "sales") {
    return (
      <SalesScreen
        shopId={shopId}
        shopName={shopName}
        onBack={() => setView("till")}
        onExpired={() => {
          // Same as everywhere else: an ended login goes back to sign in.
          setView("till");
          setUser(null);
          setShopId(null);
        }}
      />
    );
  }

  return (
    <div className="flex h-dvh flex-col bg-background text-foreground">
      {/* Who is serving, which shop, and the way out. */}
      {/* The name gets its own row and the buttons wrap below it: an Owner
          has five, more than a phone fits on one line. */}
      <div className="flex shrink-0 flex-wrap items-center gap-2 border-b border-border bg-surface px-3 py-1.5">
        <span className="min-w-0 basis-full truncate text-sm font-semibold">
          {shopName} · {user.fullName}
        </span>
        <button
          onClick={() => setView("sales")}
          className="min-h-11 shrink-0 rounded-lg border-2 border-primary px-3 text-sm font-bold text-primary"
        >
          Sales
        </button>
        {isOwner && (
          <button
            onClick={() => setView("stockIn")}
            className="min-h-11 shrink-0 rounded-lg border-2 border-primary px-3 text-sm font-bold text-primary"
          >
            Stock in
          </button>
        )}
        {isOwner && (
          <button
            onClick={() => setView("expenses")}
            className="min-h-11 shrink-0 rounded-lg border-2 border-primary px-3 text-sm font-bold text-primary"
          >
            Money out
          </button>
        )}
        {isOwner && (
          <button
            onClick={() => setView("owed")}
            className="min-h-11 shrink-0 rounded-lg border-2 border-primary px-3 text-sm font-bold text-primary"
          >
            Owed
          </button>
        )}
        <button
          onClick={signOut}
          className="min-h-11 shrink-0 rounded-lg border-2 border-border-strong px-3 text-sm font-bold"
        >
          Sign out
        </button>
      </div>

      {/* Search: always at the top, never scrolls away. */}
      <header className="shrink-0 border-b border-border bg-surface p-2">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search name or SKU"
          className="h-12 w-full rounded-lg border-2 border-border-strong bg-surface px-3 text-lg
            placeholder:text-muted-foreground focus:border-primary focus:outline-none"
        />
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
                {product.quantityOnHand <= 0 ? (
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

        {visible.length === 0 && (
          <p className="p-4 text-base text-muted-foreground">No part matches that.</p>
        )}
      </main>

      {/* Cart: pinned to the bottom, so the total is never scrolled off. */}
      <section className="shrink-0 border-t-2 border-border bg-surface">
        {/* One tab per customer. The count stops a half-served cart being forgotten. */}
        <div className="flex gap-1 overflow-x-auto border-b border-border p-1.5">
          {carts.map((cart, index) => (
            <button
              key={index}
              onClick={() => {
                setActiveCart(index);
                setSaleProblem("");
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
            + New
          </button>
        </div>

        <div className="max-h-32 overflow-y-auto">
          {lines.map((line, index) => (
            <div key={index} className="flex items-center gap-2 border-b border-border px-2 py-1.5">
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
            </div>
          ))}
        </div>

        <div className="flex items-baseline justify-between px-3 py-2">
          <span className="text-base font-semibold">Total</span>
          <span className="font-money text-2xl font-bold">KES {kes(total)}</span>
        </div>

        {/* A refused sale. Red, in words, and the cart is still here: it must
            never look like a sale that went through. */}
        {saleProblem !== "" && (
          <div role="alert" className="mx-2 mb-2 rounded-lg bg-error px-3 py-2 text-error-foreground">
            <p className="text-base font-bold">Sale NOT saved</p>
            <p className="text-base">{saleProblem}</p>
          </div>
        )}

        <div className="space-y-2 px-2 pb-2">
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
              setSaleProblem("");
              setChoosingCustomer(true);
            }}
            disabled={lines.length === 0 || paying}
            className="h-14 w-full rounded-xl border-2 border-warning-strong text-xl font-bold
              text-foreground disabled:opacity-40"
          >
            Credit
          </button>
        </div>
      </section>
    </div>
  );
}
