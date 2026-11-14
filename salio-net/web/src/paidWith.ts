// How a supplier is paid, as the server numbers it. Shared by Stock in, the
// import screen and the invoice commit (PaidWithChoice draws the buttons).

// The server's PaymentMethod numbers. Not 0, 1, 2: 2 is Card, which a
// supplier is never paid with, so Credit is 3. "name" is the enum's own
// name, for the places that send the word instead of the number.
export const PAID_WITH = [
  { value: 0, name: "Cash", label: "Cash" },
  { value: 1, name: "Mpesa", label: "M-Pesa" },
  { value: 3, name: "Credit", label: "Credit" },
];

// "Cash", "M-Pesa", "Credit": what the shop calls the choice.
export function paidWithLabel(value: number | null) {
  return PAID_WITH.find((p) => p.value === value)?.label ?? "";
}
