import { apiFetch } from "./api";
// Shared types and the API base. The cookie is HttpOnly, so nothing here
// can read it: we only ever ask the server who we are.
//
// Where the API is. Set at build time by VITE_API_URL (see web/README.md);
// it cannot be changed afterwards without a rebuild.
//
// - Empty, or not set in a real build (npm run build): "the same address the
//   till was loaded from". That is how the server serves it: Caddy sends
//   /api to the API. A real build never falls back to localhost.
// - Not set with npm run dev on the laptop: the local API.
// - No slash at the end: "https://api.example.com", not ".../".
const DEFAULT_API = import.meta.env.PROD ? "" : "http://localhost:5077";
export const API = import.meta.env.VITE_API_URL ?? DEFAULT_API;

export type Shop = {
  organizationId: string;
  name: string;
  role: number; // 0 = Owner, 1 = Cashier
  // What this shop calls its things, plural: "Parts", "Products".
  itemWord: string;
  // "Sales" or "Services". Services only changes the order of the screens:
  // stock is never hidden or refused. Older servers may leave it out.
  businessKind?: string;
  // What the shop deals in, as chosen at setup.
  trade?: string;
};

export type SignedInUser = {
  id: string;
  fullName: string;
  shops: Shop[];
  // True until they replace a temporary password. The till then shows the
  // choose-your-password screen and nothing else.
  mustChangePassword: boolean;
};

// What a password login sends back: the same, plus whether this phone has a
// PIN yet. True when the phone was already this person's, with a PIN.
export type LoginResult = SignedInUser & { pinSet: boolean };

// How this phone gets in: still signed in, a PIN on a trusted phone, or the
// password. Asked at start-up and whenever a login ends.
//
// signedIn always opens locked, with the till kept behind it: Salio was
// reopened. lockedFor is the name of the PIN's owner when this browser is a
// trusted phone with a PIN (the PIN pad), or null (email and password).
export type WayIn =
  | { kind: "signedIn"; user: SignedInUser; lockedFor: string | null }
  | { kind: "pin"; name: string }
  | { kind: "password" }
  // Salio could not be asked: no internet, or the server is restarting.
  // Nothing is known about the login, so nothing is treated as signed out.
  | { kind: "unreachable" };

const START_TIMEOUT = 12000;

export async function findWayIn(): Promise<WayIn> {
  try {
    // 12 seconds, then give up: this question holds up the first screen.
    const me = await apiFetch(`${API}/api/auth/me`, { credentials: "include" }, START_TIMEOUT);

    // The server broke or is restarting. That is not "signed out".
    if (me.status >= 500) {
      return { kind: "unreachable" };
    }

    // Asked either way: it decides both "open locked" and "PIN or password".
    // If only this second question fails, a live login still opens.
    const pinOwner = await devicePinOwner().catch(() => null);

    if (me.ok) {
      return { kind: "signedIn", user: await me.json(), lockedFor: pinOwner };
    }

    if (pinOwner !== null) {
      return { kind: "pin", name: pinOwner };
    }
  } catch {
    // No answer at all. A lost connection is not a sign-out.
    return { kind: "unreachable" };
  }

  return { kind: "password" };
}

// Whose PIN this phone has, or null if it is not a trusted phone (204) or
// has no PIN yet. Throws if the server cannot be reached.
export async function devicePinOwner(): Promise<string | null> {
  const device = await apiFetch(`${API}/api/auth/device`, { credentials: "include" }, START_TIMEOUT);
  if (device.status === 200) {
    const found: { fullName: string; pinSet: boolean } = await device.json();
    if (found.pinSet) {
      return found.fullName;
    }
  }
  return null;
}

// Money is whole cents everywhere. Divide by 100 only to show it, and always
// inside font-money so columns of figures line up.
export function kes(minor: number) {
  return (minor / 100).toLocaleString("en-KE", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

// Quantities are decimals on the server. 1.000 shows as 1, 2.500 as 2.5.
export function qty(quantity: number) {
  return quantity.toLocaleString("en-KE", { maximumFractionDigits: 3 });
}

// "Mpesa" is the enum's name; "M-Pesa" is what the shop calls it.
export function paymentName(method: string) {
  return method === "Mpesa" ? "M-Pesa" : method;
}

// The shop's own word for its things, in the forms a sentence needs.
// Stored plural, as setup chose it or the owner typed it: "Parts", "Items",
// "Goods", "Products", "Services", or anything else.
//
// The singular is made by plain English rules: Parts/part, Batteries/battery,
// Dresses/dress. A word with no natural singular ("Goods", "Clothes", or a
// word that is not plural at all, like "Stock") uses "item" for the one, so
// a Goods shop sees "goods" everywhere except "Add an item" and "1 item".
export type ItemWords = {
  Plural: string; // "Parts", for titles
  plural: string; // "parts", mid-sentence
  singular: string; // "part"
  aSingular: string; // "a part", "an item"
};

// Plural words whose singular is not made by the rules below.
const NO_SINGULAR = ["goods", "clothes"];

export function itemWords(word: string | undefined): ItemWords {
  const trimmed = (word ?? "").trim() || "Parts";
  const Plural = trimmed.charAt(0).toUpperCase() + trimmed.slice(1);
  const plural = Plural.toLowerCase();

  let singular = "item";
  if (NO_SINGULAR.includes(plural)) {
    singular = "item";
  } else if (plural.endsWith("ies")) {
    singular = plural.slice(0, -3) + "y";
  } else if (/(ss|sh|ch|x|z)es$/.test(plural)) {
    singular = plural.slice(0, -2);
  } else if (plural.endsWith("s") && !plural.endsWith("ss")) {
    singular = plural.slice(0, -1);
  }

  const aSingular = (/^[aeiou]/.test(singular) ? "an " : "a ") + singular;

  return { Plural, plural, singular, aSingular };
}
