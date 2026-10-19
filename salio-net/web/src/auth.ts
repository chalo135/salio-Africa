// Shared types and the API base. The cookie is HttpOnly, so nothing here
// can read it: we only ever ask the server who we are.
export const API = "http://localhost:5077";

export type Shop = {
  organizationId: string;
  name: string;
  role: number; // 0 = Owner, 1 = Cashier
};

export type SignedInUser = {
  id: string;
  fullName: string;
  shops: Shop[];
};

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
