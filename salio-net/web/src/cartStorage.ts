// The carts at the counter, kept in this browser so a reload, a lock or a
// flat battery does not empty them.
//
// One saved entry per person per shop: "salio.cart.<userId>.<shopId>". A
// different person signing in on the same tablet reads their own entry
// (usually none), so nobody ever sees someone else's cart.
//
// Storage can be blocked (a private window, a full disk, a strict browser
// setting). Every read and write is wrapped in try/catch: when storage does
// not work, the till still sells; the carts just do not survive a reload.

export type CartLine = {
  productId: string;
  name: string;
  quantity: number;
  unitPriceMinor: number;
};

export type SavedCarts = {
  // One cart per customer at the counter. Always at least one.
  carts: CartLine[][];
  // Which customer's cart was on screen.
  activeCart: number;
};

export function cartKey(userId: string, shopId: string) {
  return `salio.cart.${userId}.${shopId}`;
}

// True when every cart has nothing in it.
export function allCartsEmpty(carts: CartLine[][]) {
  return carts.every((cart) => cart.length === 0);
}

// One line, checked field by field. Anything else in storage (an old
// format, a hand edit, a half-written value) is not trusted.
function isCartLine(value: unknown): value is CartLine {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const line = value as Record<string, unknown>;
  return (
    typeof line.productId === "string" &&
    typeof line.name === "string" &&
    typeof line.quantity === "number" &&
    Number.isFinite(line.quantity) &&
    typeof line.unitPriceMinor === "number" &&
    Number.isFinite(line.unitPriceMinor)
  );
}

// Turns what was read from storage back into carts, or null if it is not
// the shape this file writes. Kept apart from storage so it can be tested
// on its own.
export function parseSavedCarts(text: string | null): SavedCarts | null {
  if (text === null || text === "") {
    return null;
  }

  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return null;
  }

  if (typeof value !== "object" || value === null) {
    return null;
  }
  const saved = value as Record<string, unknown>;

  if (!Array.isArray(saved.carts) || saved.carts.length === 0) {
    return null;
  }
  for (const cart of saved.carts) {
    if (!Array.isArray(cart) || !cart.every(isCartLine)) {
      return null;
    }
  }
  const carts = saved.carts as CartLine[][];

  // A cart number that no longer exists opens the first cart instead.
  const active = saved.activeCart;
  const activeCart =
    typeof active === "number" && Number.isInteger(active) && active >= 0 && active < carts.length
      ? active
      : 0;

  return { carts, activeCart };
}

// The carts saved for this person in this shop, or null if none (or if
// storage cannot be read).
export function loadCarts(key: string): SavedCarts | null {
  try {
    return parseSavedCarts(window.localStorage.getItem(key));
  } catch {
    return null;
  }
}

// Saves the carts. All empty: the entry is removed instead, so storage does
// not fill up with empty carts.
export function saveCarts(key: string, carts: CartLine[][], activeCart: number) {
  try {
    if (allCartsEmpty(carts)) {
      window.localStorage.removeItem(key);
    } else {
      const saved: SavedCarts = { carts, activeCart };
      window.localStorage.setItem(key, JSON.stringify(saved));
    }
  } catch {
    // Storage is blocked or full. The carts are still on screen.
  }
}

// Forgets this person's carts in this shop: on sign out.
export function clearCarts(key: string) {
  try {
    window.localStorage.removeItem(key);
  } catch {
    // Nothing to do: storage is not working.
  }
}
