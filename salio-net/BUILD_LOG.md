# Salio — build log

A running record of what has been built, why the non-obvious choices were made,
what has actually been verified, and what is still missing. Rules of the project
live in `CLAUDE.md`; this file is the state of the work.

Last updated: 2026-11-02 (Shop setup with setup codes; the shop's own word for its things; counting the stock).

---

## 1. Status

| Stage | State |
|---|---|
| 1. Database and ledger | Working |
| 2. Products and stock | Working. Stock receipts added — see stage 9 |
| 3. The till | Working in the browser, unpolished |
| 4. Shifts and payments | Payments done. Shifts half-built: entity exists, nothing uses it |
| 5. Reports | Four reports working. Profit and loss now splits cost of sales from running costs |
| 6. Tax view | Turnover Tax, filing pack, filed returns — built, never run against real data. End of month screens built over them (2026-11-01) |
| 7. Login, users and roles | Login, roles, shop boundary and login rate limit working |
| 8. Seeing the shop | Sales list and receipt built. No day sheet screen |
| 9. Stock receipt | Endpoint and screen built. Endpoint verified live (cash only) |
| — Expenses and drawings | Not a numbered stage in `CLAUDE.md`. Endpoints and screen built; verified live against the P&L |
| 10. Setup screens | A client sets up their own shop with a setup code (2026-11-02). Built; never run against the real API. Migration not yet generated |

The till sells: a sale writes a receipt, stock movements and a balanced journal
entry in one database transaction.

---

## 2. What exists

### Salio.Domain (no EF, no ASP.NET)

- `Money` (minor units), entities: Organization, Account, JournalEntry, JournalLine,
  Product, StockMovement, Sale, SaleLine, Payment, Shift, TaxRate, FiledReturn,
  AppUser, Membership, SetupCode.
- `Organization` gained `HoldsStock` (bool), `ItemWord` (what the shop calls its
  things, plural: "Parts", "Products") and `TaxPosition` (`TurnoverTax`,
  `VatRegistered`, `NotSure`). Existing shops get `true`, "Parts" and Turnover Tax from
  the column defaults.
- `SetupCode`: the code (stored lower case, no dashes), who it was issued to (free
  text), created, expires, used at, and the shop it made. No `OrganizationId` to be
  scoped by: a code exists before its shop. The third exemption from locked rule 2,
  after `AppUser` and `TrustedDevice`.
- Enums: AccountClass, SourceType, StockMovementType, SaleStatus, EtimsStatus,
  PaymentMethod, ShiftStatus, TaxType, MembershipRole, MembershipAccessOutcome.
  `SourceType` gained `Expense` (4) and `Drawings` (5), added at the end so the
  numbers already stored keep their meaning. No migration: it is stored as a number.
- Services: `LedgerService` (balance rules), `SaleService` (builds a sale's journal
  lines), `StockService` (quantity on hand, weighted average cost),
  `MembershipRules` (locked rule 9, tested without a database),
  `StockReceiptService` (a delivery's journal lines: debit Inventory, credit Cash,
  M-Pesa or Accounts Payable), `ExpenseService` (debit the category, credit the money
  account). Both build lines and write nothing, like `SaleService`.

### Salio.Infrastructure

- `SalioDbContext` + one `IEntityTypeConfiguration` per entity.
- `JournalPoster` — the only writer of journal rows. Also enforces the filed-period lock.
- `StockPoster` — the only writer of stock movements. Insert only.
- `ReportService` — trial balance, profit and loss, stock valuation, day sheet.
- `TaxService` — turnover tax, filing pack, recording a filed return.
- `Security/`: `PasswordService` (Identity's hasher), `UserCreator` (user + membership
  in one save), `MembershipResolver` (loads memberships, applies rule 9).
- Seeders: `ChartOfAccountsSeeder`, `TaxRateSeeder`, `DemoProductSeeder`.
  `ChartOfAccountsSeeder` now adds **per code**, not per shop: it reads the codes a
  shop already has and adds only the missing ones, so an existing shop picks up new
  accounts. Switched-off accounts count as present and are not re-created.
- The chart of accounts now also has 3100 Drawings (Equity) and the running costs
  5100 Rent - Shop, 5200 Electricity, 5300 Water, 5400 Wages - Casual, 5500 Transport,
  5600 Bank and M-Pesa Charges, 5900 Other. 5000 Cost of Goods Sold is unchanged.

### Salio.Api

Every route below needs the login cookie and goes through `OrganizationGuard`
(locked rule 9). **Owner only** marks what a Cashier may not touch.

| Method | Route | Notes |
|---|---|---|
| POST | `/api/auth/login`, `/api/auth/logout` | Login returns the user's shops; logout always 200 |
| GET | `/api/auth/me` | Same shape as login. 401 when the cookie is gone |
| POST | `/api/setup/shop` | **No login; a setup code instead.** Shop, chart of accounts, tax rate (not for VAT registered), first Owner with their own password, and the code marked used, in one transaction. Rate limited with the login policy |
| POST | `/api/setup/codes` | Makes a setup code. **404 outside Development**; on the server, codes are made with the SQL in section 7 |
| POST | `/api/setup/organization` | The old developer-made shop, no code. **404 outside Development** |
| ~~POST~~ | ~~`/api/users`~~ | **Removed 2026-11-07.** An Owner chose the person's permanent password. People are added through `POST /api/people` (temporary password, changed on first sign-in) |
| POST | `/api/users/sign-out-all-devices` | **Owner only.** New stamps for the shop; kills live cookies |
| GET | `/api/organizations` | Only the caller's own shops |
| GET | `/api/accounts?organizationId=` | **Owner only.** Active accounts |
| POST | `/api/accounts` | **Owner only.** Name only. Always an expense account; the code is the lowest free number from 5001 to 5899. Refuses a duplicate name, and any name containing "drawing" |
| DELETE | `/api/accounts/{id}` | **Owner only.** Switches off (`IsActive = false`), never deletes. Expense accounts only, not 5000, and only if the account has no journal lines |
| GET | `/api/products?organizationId=` | Shelf list. `needsReceiving` is true when quantity is below zero. `averageCostMinor` is null for a Cashier, and null for everyone while `needsReceiving` |
| GET | `/api/products/{id}?organizationId=` | Whole entity, still includes eTIMS and `isActive` |
| POST | `/api/products` | **Owner only.** 409 on duplicate SKU |
| POST | `/api/sales` | The till's main call. Records who sold. Contract in section 5 |
| GET | `/api/sales?from=&to=` | A Cashier's own sales; an Owner sees all, with seller names |
| GET | `/api/sales/{id}?organizationId=` | One sale as a receipt: lines with name and SKU, payments, seller. 404 for a sale the caller may not see |
| POST | `/api/stock-receipts` | **Owner only.** Goods arriving. `paidWith`: 0 Cash, 1 M-Pesa, **3** Credit (2 is Card and is refused). Movements and journal entry in one transaction |
| POST | `/api/stock-counts` | **Owner only.** What is really on the shelf, one shelf or the whole shop. One adjustment movement per product that differs, at its average cost; one journal entry for the net value (5700 Stock Loss against 1200 Inventory). Matching lines write nothing. See "Counting the stock" at the end |
| POST | `/api/expenses` | **Owner only.** Money out: debit an expense account or 3100 Drawings, credit Cash or M-Pesa. Refuses Sales, Inventory, any other account, and 5000 |
| GET | `/api/expenses?from=&to=` | **Owner only.** Each entry with date, category, amount, Cash or M-Pesa, note, and `isDrawings` |
| POST | `/api/journal-entries` | **Owner only.** Manual entry; 400 if unbalanced |
| GET | `/api/journal-entries/{id}?organizationId=` | **Owner only.** One entry with lines |
| GET | `/api/reports/trial-balance,profit-and-loss,stock-valuation,day-sheet` | **Owner only.** All derived on read |
| GET | `/api/tax/turnover-tax`, `/api/tax/filing-pack` | **Owner only.** Figure plus the exceptions behind it |
| POST | `/api/tax/filed-returns` | **Owner only.** Records that she filed; locks the period |

### web/ (React 19 + Vite + TypeScript + Tailwind v4)

`src/SetupScreen.tsx` is the way a new client in: "Set up a new shop" on the login
screen, then three steps — the code and the shop's name; what it sells (stock or
services only, its word for its things, and tax with "I'm not sure" as a full answer);
the owner's name, email and password twice. It ends by signing in through
`/api/auth/login` with what was typed, so the phone is trusted the usual way and the
owner then chooses a PIN. Choosing services only shows that Salio does not support it
yet and stops there. `CreateShopScreen.tsx` (the dev-only form) was deleted.

**The shop's own word.** `/api/auth/me` and login now carry each shop's `itemWord`;
`itemWords()` in `auth.ts` makes the forms a sentence needs ("Parts", "parts", "part",
"a part" / "an item"). The sidebar, the More list and the Parts screens and form use it.

`src/LoginScreen.tsx`, `src/ShopChooser.tsx` and `src/auth.ts` handle signing in;
the chooser appears only for someone who belongs to more than one shop. `src/App.tsx`
is the till itself:

- Search box fixed at the top; results are the only scrolling area.
- Result row: name left, price right in tabular figures; second line is shelf chip,
  SKU, and "8 left", an amber "Out of stock" at exactly zero, or an amber
  "Needs receiving" below zero (`needsReceiving` from the API).
- A cart line whose product Salio shows at or below zero says "Needs receiving: Salio
  shows N in stock", where the below-cost warning appears; an Owner also sees "Cost
  unknown until stock is received". A warning only: the Pay buttons are unchanged
  (locked rule 16).
- Multiple carts: `carts: CartLine[][]` plus `activeCart`. Every change goes through
  `setActiveLines`, which rebuilds the outer list and leaves other carts untouched.
  Tabs show each cart's line count; "+ New" appends and switches.
- Cart pinned to the bottom, total always visible, full-width Cash and M-Pesa buttons,
  disabled while a payment is in flight so a double tap cannot record two sales.
- A cart line whose price is under that product's average cost shows
  "Below cost (KES X)" — a warning only, never a block.
- On a successful pay, that cart is removed and the till switches to cart 0, creating
  an empty cart if none remain.
- **A receipt replaces the success alert** (`src/Receipt.tsx`): shop name, sale number,
  date and time, each line, total, how it was paid, and one Done button. Large type,
  no grey text, because the cashier turns the phone round to the customer. The same
  component shows any past sale from the sales list: one receipt, two places. The till
  does not record cash tendered, so the receipt shows no change.
- **A refused sale shows in the till**, in a red "Sale NOT saved" box with the server's
  `detail`, and the cart stays. One `alert()` remains: "Your login has ended", because
  the till is replaced by the login screen at once.
- **The shelf reloads after each sale and every 30 seconds**, because two owners sell
  from separate phones at once. The timer stops while the tab is hidden and catches up
  as soon as it is shown again. A ref stops two reloads running at once, so an older
  answer cannot overwrite a newer one.

Header buttons: **Sales** for everyone; **Stock in** and **Money out** for an Owner
only (hidden from a Cashier, and refused by the server as well).

- **`src/SalesScreen.tsx`**: today's sales by default, newest first, each with time,
  total, Cash / M-Pesa / both, and who sold it. Quick ranges Today, Yesterday, This
  week (from Monday), This month, plus a custom from–to. Takings at the top with Cash
  and M-Pesa as separate figures, added up in the browser from the rows on screen, and
  a label naming the period whenever it is not today. A failed load says so and offers
  a retry; totals show "—" rather than 0.00 while loading or after a failure. Tap a
  sale for its receipt.
- **`src/StockInScreen.tsx`**: date, paid with (no default: it must be chosen), an
  optional supplier, products found by name or SKU, quantity and unit cost per line
  typed in shillings, and a running delivery total. Warns when a cost is above the
  selling price, never blocks. Refuses a zero quantity or cost before sending, naming
  the line. Typed lines survive a failed save; leaving the screen clears them.
- **`src/ExpensesScreen.tsx`** ("Money out"): large category buttons, Drawings shown
  apart under "Not a shop cost" as money taken out for the family, amount, Cash or
  M-Pesa (no default), date, note. Adds a new category without leaving the screen.
  Below the form, this month's entries with **shop costs and drawings totalled
  separately, never together**.
- **`src/MonthScreen.tsx`** ("End of month", Owner only, from More): opens on last
  month, with Earlier / Later buttons (Later stops at the current month, which is
  labelled "not over yet"). Sales, minus cost of the parts sold, giving "Left after
  parts"; running costs one row per account; then "What the shop made" (or "lost").
  Drawings in their own box, saying they are the family's share and NOT taken off what
  the shop made. An amber line says the stock has never been counted, so profit is only
  as right as the stock figure — always shown, because no stock count exists yet. The
  Turnover Tax figure is a row that opens the tax screen; if the filing pack is refused
  (no rate for the month), the server's reason shows instead.
  Calls `GET /api/reports/profit-and-loss`, `GET /api/expenses` (drawings are added up
  in the browser from the rows marked `isDrawings`) and `GET /api/tax/filing-pack`, all
  with the month's first and last day.
- **`src/TaxScreen.tsx`**: the figure to file, large and alone, with "Salio does not
  file anything and never contacts KRA". The working: everything sold (with the number
  of sales), rate, tax, and a sentence naming both turnover and what the shop made, so
  the smaller tax is not read as a mistake. "Before you file": the rate and the date it
  took effect, then the filing pack's `exceptions` exactly as the server wrote them.
  "I have filed this on iTax" opens a box for the iTax reference, says that saving
  closes the month, and repeats how many problems are listed if any. Saving calls
  `POST /api/tax/filed-returns` with the pack's figure, `taxType` 0 (Turnover Tax) and
  the current time as `filedAt`. The closed state shows what, how much, when, by whom
  and the reference, and that a later mistake is corrected in the next month, never by
  changing this one. A refusal (for example a return already recorded) shows the
  server's words and keeps the reference typed. Filing is offered only once the month
  is over.

---

## 3. Decisions worth remembering

1. **A sale with no recorded cost still goes through.** When a product's weighted
   average cost is 0 (never received, or sold out), the COGS/Inventory pair is left
   out of the journal instead of being written as two zero lines, which `PostJournal`
   rejects. Before this, the first sale of an unreceived part failed with
   "A journal line must have a debit or a credit" at the till.
2. **`TaxRate` gained a `TaxType` column** (`TurnoverTax`, `Vat`); `Band` became
   nullable because Turnover Tax has no VAT band.
3. **A filed return closes the period.** `JournalPoster` refuses any entry dated inside
   a recorded return, sales included, so a filed figure cannot silently change.
4. **Seeded opening stock is also booked** (debit Inventory, credit Owner's Equity).
   Without it, the first sale would push Inventory below zero in the books.
5. **Products endpoint avoids N+1 with two queries total.** One for products, one for
   every stock movement in the shop, then grouped in memory. Weighted average cost
   depends on the order movements happened, so a `SUM` in SQL is not enough.
6. **Tailwind's default palette and type scale are switched off.** Only semantic tokens
   exist, so a component cannot reach for a raw colour. `bg-primary` works,
   `bg-green-500` does not.
7. **Selling below cost warns, never blocks.** A till that refuses a sale gets worked
   around, and then the money and the record are both lost.
8. **Vite's starter CSS was deleted** from `index.css` (1126px centred column, 56px
   headings, dark mode) because it fought the phone layout.
9. **Profit and loss: 5000 is cost of sales, every other expense account is a running
   cost.** Gross profit is sales minus 5000; net profit is gross profit minus running
   costs, listed by account. Before this, every expense account counted as cost of
   sales. The rule is "not 5000" rather than "5100 and above" because an owner's own
   categories get codes from 5001, which "5100 and above" would have dropped from the
   report altogether.
10. **Drawings is equity, so it can never reach the P&L.** The report reads only Income
    and Expense accounts (the `AccountClass` filter in
    `ReportService.GetProfitAndLossAsync`). A second Drawings cannot be made through
    `POST /api/accounts`, which only makes expense accounts: an expense called
    "Drawings" would quietly count the family's money as a shop cost.
11. **Expenses cannot post to 5000.** Cost of goods sold is written by sales from what
    the stock cost; typing into it by hand would make gross profit wrong.
12. **An account with journal lines is never switched off.** Reports are derived from
    lines on every read, so an account that has them is part of the books.
13. **Stock may go below zero (locked rule 16).** The till never refuses a sale for
    stock. Negative stock is shown on the shelf list and in the cart until stock is
    received, and no cost figure is given for a product below zero.
14. **The demo shop's transactions are wiped and reseeded (SQL written 2026-10-21).**
    Its stock had gone negative before stock receiving existed. CDI-BX150 was seeded
    while its opening quantity was 0, and when the code later said 10 the seeder
    skipped it because the SKU existed. It sold down to −7, so no report on the shop
    described anything. Development database only, demo shop only, by SQL the owner
    runs by hand. It deletes journal lines and entries, stock movements, sale lines,
    payments, sales and customers. It keeps products, accounts, organisations, users
    and memberships. No deleting code was added to the app. This is the one exception
    to locked rule 5, allowed because it was demo data that nothing relied on.
    The SQL and the reseed were run end to end on a throwaway copy of the dev database,
    which was then dropped. Afterwards: 12 products at their seed quantities, SODA-500
    at 0, one opening entry of KES 88,180.00 each side (Inventory / Owner's Equity),
    nothing below zero, and a second start added nothing.
    `DemoProductSeeder` now gives opening stock to an existing demo product that has
    **no movements at all**, not only to a missing one, so a wiped shop refills on
    the next start. A product with any history is never topped up.
    `SetUpDevelopmentDatabaseAsync` throws outside Development.
15. **Shops are set up by clients, with a code (locked rule 14, rewritten
    2026-11-02).** One message for an unknown, used or expired code, so trying codes
    reveals nothing. The code is claimed first inside the transaction, with an update
    that only matches an unused code, so two people using one code at once cannot both
    get a shop. The first Owner chooses their own password and has no
    `MustChangePassword`. An email that already has an account is refused with "sign
    in instead", and the code is left unused.
16. **Tax position at setup.** VAT registered gets no Turnover Tax rate, because a VAT
    registered business does not pay Turnover Tax. "I'm not sure" gets the Turnover Tax
    rate: below the VAT threshold it is the usual case, and the answer is kept on the
    shop so it can be asked again. Nothing else reads `TaxPosition` yet.
17. **Only stock-holding shops.** `HoldsStock` is stored, and setup refuses `false`
    rather than make a shop whose till, Stock in and cost of sales do not fit it.

### What weighted average cost does below zero

`StockService.CalculateWeightedAverageCost` (owner-written, unchanged) returns **0**
whenever running quantity ends at or below zero, and resets the running value to 0
every time quantity touches zero. Both are deliberate and safe. The problem is the
**first receipt after going negative**: the whole delivery's value lands on only the
units left above zero. Found 2026-10-21 by reading the code, then reproduced on a
throwaway copy of the dev database: CDI-BX150 had 10 at KES 1,050, 12 were sold (−2),
5 were received at KES 1,050, and the average came back as **KES 1,750**, not 1,050.

- If the oversold units were costed at the sale (sold from positive stock, as here),
  cost is counted twice. The sale had already credited Inventory for all 12. The
  inflated average then credits it again for the 2 missing units when the last 3 sell.
  Cost of sales ends KES 2,100 too high, and Inventory goes below zero in the books.
- If they were sold at exactly zero (cost 0, no COGS lines; decision 1), the
  inflation happens to put the missing cost back. The total is right, but it lands
  on later sales, in the wrong period.

**Not fixed: `StockService` is owner-written.** It is the owner's decision what the
calculation should do when a receipt lands on a negative quantity. Until then the
products endpoint hides the figure only while quantity is negative. The inflated
figure after receiving is still shown, in the stock valuation report and in the
below-cost warning. See gap 1.

### Theme tokens (`web/src/tailwind.css`)

Deep desaturated forest green, warm greys, amber for warnings, red for errors only.
Contrast measured, not guessed:

| Pair | Ratio |
|---|---|
| foreground on surface | 17.3:1 |
| muted-foreground on surface | 7.5:1 |
| primary-foreground on primary | 6.9:1 |
| warning-foreground on warning (badge) | 7.9:1 |
| warning-strong on surface (amber text) | 5.7:1 |
| border-strong on surface (control outlines) | 3.2:1 |

`--color-border` is 1.6:1 and is for row dividers only, never for anything tappable.
`font-money` turns on tabular figures; put it on every currency value.

---

## 4. Verified vs not verified

**Verified**
- 12 domain tests pass (ledger, sale, stock). `Salio.Tests` builds again.
- The till runs in a browser and sells end to end: sales made against the seeded
  demo products are recorded correctly. This is sandbox development, not a shop.
- API and web both build clean; web includes the TypeScript check.
- Seeding ran against the dev database: 12 products, 11 opening movements, 1 journal
  entry; trial balance balanced at 7,785,000 both sides; a second start inserted nothing.
- `/api/products` live output checked, including average cost per product.
- `/api/sales` rejection paths checked live (bad enum, missing organisation, payments
  not matching the total). No sale was written by those probes.
- JSON casing confirmed camelCase, nulls included.
- `GET /api/sales` and `GET /api/sales/{id}` verified live with real data.
- `POST /api/stock-receipts` verified live: a cash receipt raised Inventory and
  credited Cash.
- Expenses verified live against the profit and loss: an expense left gross profit
  untouched and reduced net profit; a drawing changed neither and appears nowhere in
  the report.
- **31 domain tests pass** (adds `StockReceiptServiceTests` and `ExpenseServiceTests`:
  journal shape for cash, credit and multi-line receipts; expense lines balance; a
  zero or negative amount is refused).
- **39 tests pass** (2026-10-21).
- **Negative stock, on a throwaway copy of the dev database (2026-10-21):** a sale
  of 12 CDI-BX150 with 10 on hand was accepted. `/api/products` then gave
  `quantityOnHand: -2`, `needsReceiving: true`, `averageCostMinor: null`. The wipe
  SQL, the new migration and the reseed were run on the same copy (decision 14).
  The copy was dropped afterwards.
- The till's "Needs receiving" badge and cart warning passed the build and TypeScript
  check. They have **not** been looked at in a browser.

**Not verified**
- **No real sale has ever been recorded.** Everything so far is seeded demo data in
  a sandbox: no real customer, no real shop takings, nothing anyone has relied on.
- The cashier cannot change a price at the till, so the below-cost warning has never
  fired in practice. See gap 4.
- **The screens built since Stage 7 are not recorded as verified in a browser**: the
  receipt, the till's in-screen failure message, the sales list's date ranges, Stock
  in, and Money out (including adding a category). Each passed lint and the TypeScript
  check when written; none is known to have been clicked through.
- The stock receipt transaction has never been made to fail, so the rollback is
  reasoned from the code, not shown. M-Pesa and Credit receipts are not recorded as
  verified live; only cash is.
- `POST /api/accounts` and `DELETE /api/accounts/{id}` are not recorded as verified
  live.
- Tax endpoints (turnover tax, filing pack, filed returns) have never been called
  against real data.
- The filed-period lock has never been exercised.
- No tests cover anything in Salio.Infrastructure.
- **Shop setup (2026-11-02) has never run against the real API or database.** The
  API builds, 5 new `SetupCodesTests` pass (a code matches however it is typed; codes
  are three groups of four with no look-alike letters), and the web type check
  passes. Not run: a real setup end to end, a used / expired / unknown code, the race
  on one code, VAT registered getting no rate, the sign-in afterwards.
- The End of month screens (2026-11-01) were checked only in headless Edge at 360px
  against a faked API: figures, drawings kept apart, the no-rate message, the empty
  and refused reference, the closed state, no sideways scroll, nothing under 44px, and
  no End of month row for a Cashier. They have never been run against the real API.

---

## 5. `POST /api/sales` contract

```json
{
  "organizationId": "00000000-0000-0000-0000-000000000001",
  "saleDate": "2026-09-19",
  "lines": [{ "productId": "…", "quantity": 2, "unitPriceMinor": 55000, "taxBand": "A" }],
  "payments": [{ "method": 1, "amountMinor": 110000, "reference": "TIH4K2QX9P" }]
}
```

`[Required]` does **not** catch a missing Guid, date, number or enum: the value silently
becomes zero, an all-zero Guid, or `0001-01-01`. Send everything except `reference`.

- `method` is a number: 0 Cash, 1 M-Pesa. 2 and 3 are rejected as unsupported.
  Missing becomes Cash.
- `taxBand` is a one-letter string. Missing produces a null character and a **500**.
- Payments must add up to the sale total exactly. Send the sale total, not cash
  tendered; the till works out change itself.
- An unknown `productId` gives a **500** (foreign key), not a 400.
- Success returns **200** with `saleNumber`, `totalMinor`, `journalEntryId`.
- Two error shapes: model-binding errors carry an `errors` object; business refusals
  carry `title: "The sale was rejected"` and a `detail` worth showing to the cashier.

---

## 6. Known gaps, roughly in priority order

1. **Weighted average cost is wrong after a receipt lands on negative stock.** See
   "What weighted average cost does below zero" in section 3. `StockService` is
   owner-written, so the fix is the owner's. Until it is fixed, the stock valuation
   report also shows a negative-stock product at cost 0 and value 0, as if that were
   a figure. Rule 16 says it must not be.
2. **The demo data is unusable for judging any report until the wipe SQL is run**
   (decision 14). CDI-BX150 went to −7 before receiving existed, so stock value, cost
   of sales and gross profit on the demo shop describe nothing real. Delete this entry
   once the SQL has been run and the API restarted. Gap 14 (SODA-500) goes with it,
   because its unbooked movement is deleted too.
3. **`taxBand` is hard-coded to "A"** in the till. Products have no tax band column, so
   no screen can supply it honestly. A non-VAT shop on Turnover Tax is probably band D —
   confirm with KRA.
4. **The till cannot edit a line's price.** `addToCart` fixes `unitPriceMinor` to the
   product's `sellPriceMinor`, and the only editable field on a cart line is quantity.
   A spares counter haggles. Checked in `web/src/App.tsx` on 2026-10-19: still true.
5. **`saleDate` uses `toISOString()`**, which is UTC. Between midnight and 3am Kenyan
   time it books the sale to the previous day, so it also drops out of "Today" on the
   sales list.
6. **Only the demo shop picks up new chart-of-accounts codes by itself.** The seeder
   runs at Development startup for the demo shop, and when a shop is created. Every
   other existing shop lacks 3100 Drawings and 5100–5900 until the seeder is run for
   it, so its Money out screen shows no categories.
7. **A category that has been used can never be switched off**, because an account
   with journal lines is refused. It stays in the Money out list for good. Undecided:
   whether a used category should be hideable from the picker while its lines still
   count in reports.
8. **Stock movements are tied to their receipt only by the note.** The receipt id's
   first 8 characters are in each movement's `Note`; there is no column. Enough to
   read, not a real link.
9. **Sales are not linked to shifts.** `Sale.ShiftId` is always null; no shift service
   or endpoints exist. `Salio.Tests/ShiftServiceTests.cs` was deleted on 2026-09-19 so
   the test project would build — it covered a `ShiftService` that was never written.
   Recover it from git history when shifts are built.
10. **CORS is a development-only policy** allowing `http://localhost:5173`.
11. `GET /api/products/{id}` still returns the raw entity including eTIMS fields.
12. Duplicate SKU is pre-checked, but two simultaneous creates can still reach the
    database's uniqueness rule and produce a 500.
13. Sale numbers come from `COUNT(*) + 1`, which two tills would collide on.
14. The hand-made `SODA-500` demo product's stock was never booked to Inventory, so the
    books are KES 70 below the shelf.
15. The till's header now holds Sales, Stock in, Money out and Sign out. On a narrow
    phone the shop and user name are squeezed to almost nothing.
16. **A filed month cannot be read back.** There is no `GET` for filed returns, so the
    End of month screen shows a month as closed only until the screen is left. Coming
    back, it offers "I have filed" again; the server refuses the second record, and the
    screen shows that refusal. Needs a `GET /api/tax/filed-returns`.
17. **Who filed is not stored.** `FiledReturn` has no user column. The closed state's
    "By" is the name signed in on that phone at the moment of saving.
18. **The filing pack does not report** unpaid credit counted in the figure, sales with
    no recorded cost, or stock not counted. It reports sales with no payment, payments
    that do not match the sale, and sales that do not match the books' turnover. The
    screens show only what it returns.
19. **A stock count can be recorded (`POST /api/stock-counts`, 2026-11-02) but has no
    screen yet, and nothing reads when stock was last counted**, so the month screen
    still always says the stock has never been counted.
20. **To do: re-check the device cookie once hosted on HTTPS.** `salio_device` is
    Secure outside Development, SameSite=Lax, Path=/api/auth. None of that has been
    seen on a real HTTPS host.
21. **`PinUnlockRules.Decide` is not written yet** (the owner writes it). Until it is,
    `POST /api/auth/pin-unlock` throws, and its 7 tests fail.
22. **"Part" is still said in six places** the shop's word has not reached, because
    those areas were not to be changed: the Sell screen ("No part matches that.",
    "Tap a part on the left to add it."), Stock in ("No part matches that.") and End of
    month ("Cost of the parts sold", "Left after parts", and the stock warning).
23. **`TaxPosition` is stored and nothing reads it.** A VAT registered shop gets no
    Turnover Tax rate, so its End of month screen shows the "could not be worked out"
    message rather than anything about VAT. A "not sure" shop is shown Turnover Tax as
    if it were certain.
24. **The singular of the shop's word is guessed** (drop the final "s"; "ies" becomes
    "y"). Right for Parts, Products, Items, Batteries; wrong for a word like "Goods".
25. **No screen lists setup codes or who used them.** Read the `SetupCodes` table.
26. **The People migration was never generated.** `MustChangePassword` and
    `DeactivatedAt` (2026-11-01) are in the code but not in any migration, so the next
    migration carries them together with the setup fields. See section 7.
27. **Stock count placeholders, pending the owner's decision.** A count is refused whole,
    nothing written, when a counted product stands below zero, or when the count finds
    stock for a product with no cost on record (this includes a product Salio shows at
    exactly 0). Proposed: below zero, correct the quantity and book no value; found
    with no cost, let the owner type a unit cost on that line, as opening stock does.
28. **Money out still shows a "Stock Loss" button.** The server refuses 5700 as money out,
    but `web/src/ExpensesScreen.tsx` filters only 5000 from the categories. Fix: also
    leave out 5700 there.
29. **Stock Loss sits under running costs** in the profit and loss, not cost of sales,
    because only 5000 is cost of sales in `ReportService`. A month where a count found
    more than was missing shows it as a negative running cost.
30. **Existing shops lack 5700 Stock Loss** until the chart seeder runs for them (only the
    demo shop and newly set-up shops get it). Until then a count that changes the books
    is refused. Insert-only SQL to add it to every shop that lacks it:
    `INSERT INTO "Accounts" ("Id","OrganizationId","Code","Name","AccountClass","ParentId","IsActive") SELECT gen_random_uuid(), o."Id", '5700', 'Stock Loss', 4, NULL, true FROM "Organizations" o WHERE NOT EXISTS (SELECT 1 FROM "Accounts" a WHERE a."OrganizationId" = o."Id" AND a."Code" = '5700');`

---

## 7. Running it

The database password is **not in the repo**. `appsettings.json` holds the connection
string without a password, and the full string lives in .NET user secrets under the same
name Program.cs reads (`ConnectionStrings:Default`). A fresh machine needs
`dotnet user-secrets set` before the API will start.

The old password is still in git history (commits c8a3b60, 6cb8bd4, 47b946f), so it
must be treated as compromised. **Done: the password has been rotated in Postgres.**
The old one stays in git history for good.

```bash
# Postgres must be up (docker container salio-db)

# API — use the http profile. The https profile redirects, which breaks the
# browser's CORS check from the till.
dotnet run --project Salio.Api --launch-profile http     # http://localhost:5077

# Till
cd web && npm run dev                                    # http://localhost:5173
```

In Development, startup applies migrations and seeds an organisation, the chart of
accounts, the Turnover Tax rate, and 12 motorcycle spare parts with opening stock.
None is at zero any more: CDI-BX150 now opens at 10. Seeding is per SKU, so it never
duplicates and never touches products made by hand. A demo product that exists but
has no stock movements at all gets its opening stock again (decision 14).

### Logging in

Nothing can sign in until a password is chosen. There is no default and none is stored
in the repo.

```bash
dotnet user-secrets set "SeedTestUserPassword" "<your choice, 8+ characters>" --project Salio.Api
# then restart the API: startup creates the user and logs that it did
```

That seeds **`test@salio.local`** ("Test Owner") as **Owner of the demo shop** — the one
holding the 12 motorcycle parts. It is the only account attached to a shop with stock,
so it is the one to test the till with. Without the secret the seeder skips the user and
logs that it skipped; the account then does not exist at all.

**Type emails exactly, all lowercase.** Email is case-sensitive by decision, so
`Test@salio.local` is a different account and will be refused.

**Accounts in the dev database as of 2026-09-22:** only `verify.owner@salio.local` and
`verify.cashier@salio.local`, both in "ZZ Verification Shop". They exist to prove the
shop boundary and the role gates, their shop has one dummy product and no stock, and
their passwords are in the session transcript, never in this repo. **Delete them** once
`test@salio.local` works — SQL is in the third-slice notes.

Migrations, in order: `InitialCreate`, `ChartOfAccountsAndJournal`, `ProductsAndStock`,
`SalesAndTaxRates`, `TaxView`, `AddBinLocation`, `UsersAndMemberships`,
`SaleSellerAndSecurityStamp`, `CustomersAndCustomerIds`, `SaleLineNameAndSku`,
`TrustedDevices`.

**Next migration, not yet generated (2026-11-02).** It holds the People fields
(`AppUsers.MustChangePassword`, `Memberships.DeactivatedAt`) and the setup fields
(`Organizations.HoldsStock`, `ItemWord`, `TaxPosition`, the `SetupCodes` table):

```bash
dotnet ef migrations add PeopleAndShopSetup --project Salio.Infrastructure --startup-project Salio.Api
```

### Issuing a setup code

On the server, with the database (Postgres 13 or later has `gen_random_uuid()`). The
code is stored lower case with no dashes; give the client the same letters in three
groups, for example `7f3a-91c2-d4e8`:

```sql
INSERT INTO "SetupCodes" ("Id", "Code", "IssuedTo", "CreatedAt", "ExpiresAt", "UsedAt", "UsedByOrganizationId")
VALUES (
  gen_random_uuid(),
  substr(replace(gen_random_uuid()::text, '-', ''), 1, 12),
  'Wanjiku, Kamau Motor Spares, Thika',
  now(),
  now() + interval '14 days',
  NULL,
  NULL
)
RETURNING "Code", "ExpiresAt";
```

On this laptop, in Development, `POST /api/setup/codes` with
`{ "issuedTo": "…", "daysValid": 14 }` does the same and answers with the code already
grouped.

To see who has used theirs: `SELECT "IssuedTo", "UsedAt", "UsedByOrganizationId" FROM "SetupCodes" ORDER BY "CreatedAt" DESC;`

`CustomersAndCustomerIds` (2026-10-21) was missing. `Customer`, `Sale.CustomerId` and
`JournalLine.CustomerId` were in the code with no migration, so the dev database had
none of them. Every journal-line insert, including the opening-stock seed, would
have failed. The migration was generated from the existing configuration and holds
only those: the `Customers` table, two nullable `CustomerId` columns, their indexes
and foreign keys (Restrict). It is applied at the next Development start.

```bash
dotnet ef migrations add <Name> --project Salio.Infrastructure --startup-project Salio.Api
```

---

## 8. Tax note

The seeded Turnover Tax rate is **1.5%, effective 27 December 2024**, with its source
recorded in the row. Kenyan rates are contested between sources and change with each
Finance Act — Turnover Tax has been 1%, then 3%, now 1.5%. **Verify with KRA before any
real customer uses this.** Salio never files anything and never talks to iTax or eTIMS;
it computes the figure and the shopkeeper files it herself.

---

## 9. Stage 7 decisions

Agreed before any code. The rules themselves are locked rules 9–14 in `CLAUDE.md`.

- A user may belong to several organisations, so membership is its own table.
- Staff use their own phones, so a login lasts a working day and an owner can sign out
  every device.
- Roles are Owner and Cashier for now.
- No self-signup: the developer creates each organisation and its Owner.
- The pilot shop is a design partner, not the product. Features only a spare parts
  counter needs do not go in unless a hardware shop or a chemist would use them too.

### First slice: the tables (2026-09-21)

Two tables and an enum. Nothing reads them yet.

- **`AppUser`** — one row per person who can log in: `FullName`, `Email`,
  `PasswordHash`, `CreatedAt`, `IsActive`. Unique index on `Email`. Leavers are
  marked inactive, never deleted, because their sales stay in the books.
- **`AppUser` has no `OrganizationId`** — the only entity in Salio exempt from locked
  rule 2, and exempt because of rule 10. A person may work at two shops, and one
  password belongs to the person, not the shop.
- **`Membership`** — one row per person per shop: `OrganizationId`, `AppUserId`,
  `Role`, `CreatedAt`. Unique index on (`OrganizationId`, `AppUserId`), so nobody can
  hold two roles in one shop. Both foreign keys are `Restrict`: deleting a shop or a
  person who appears in the books fails loudly instead of erasing who did what.
- **`MembershipRole`** — `Owner = 0`, `Cashier = 1`.

**Deliberately not built yet, and owner-written:** login and logout endpoints, password
checking, token issuing or session handling, sign-out-every-device, anything that reads
the logged-in user, the code that resolves which `OrganizationId` a request may touch,
seeding of the first Owner, and every change to existing controllers and to `web/`.
Controllers still take `organizationId` from the query string and trust it; rule 9 is
not enforced anywhere yet.

**Decided 2026-09-21: email stays case-sensitive.** `Ann@shop.co.ke` and
`ann@shop.co.ke` are two different accounts, and the unique index on `Email` stays as
it is. No normalising, no `NormalizedEmail` column, no `lower(Email)` index. The
consequence to remember when login is built: a cashier must type their email exactly
as it was created, and a phone keyboard capitalises the first letter by default, so the
login field needs `autocapitalize="none"` or people will be locked out by a capital A.

### Second slice: passwords (2026-09-21)

- Passwords can now be **stored and checked**. `Salio.Infrastructure/Security/` holds
  `PasswordService` (hash and check, using ASP.NET Core Identity's
  `PasswordHasher<AppUser>` from `Microsoft.Extensions.Identity.Core`) and
  `UserCreator` (a user plus its membership row in one `SaveChangesAsync`).
  No cryptography is written in this repo — locked rule 11.
- **One test user seeds in Development only**: `test@salio.local`, "Test Owner", Owner
  of the demo shop. Its password is read from configuration key
  `SeedTestUserPassword`. With that key unset the user is **skipped and logged**; there
  is no default password and none is written to any file. Seeding is per email, so a
  second start inserts nothing.
- **There is still no login.** No endpoint, no token, no cookie, nothing reads the
  logged-in user, and no controller checks membership. Rule 9 remains unenforced.
- **Delete the test user before any real shop goes live.** It is an Owner account with
  a password that lives in a developer's secrets, and nothing expires it.

### Third slice: login, and the shop boundary (2026-09-21)

- **Cookie login.** `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`.
  The cookie is HttpOnly, SameSite=Lax, Secure outside Development, persistent, and
  dies 12 hours after sign-in with sliding off (rule 12). Failures return 401 and 403,
  never an HTML redirect. The cookie carries **only** the user id, full name and a
  security stamp: no organisation, no role.
- **Rule 9 is enforced.** `MembershipRules` in Domain holds the decision and is tested
  without a database; `MembershipResolver` loads the memberships; `OrganizationGuard`
  is what controllers call. **Every controller now uses the OrganizationId the guard
  returns, never the one the client sent.** All seven are `[Authorize]`, and
  `/api/organizations` returns only the caller's own shops instead of every shop in
  the database.
- **Roles (rule 13).** Owner only: accounts, journal, reports, tax, adding products,
  adding users, signing out devices. Both roles: the products list and selling.
  `averageCostMinor` is null for a Cashier.
- **Sales record who sold them.** `Sale.AppUserId`, nullable, set from the cookie.
  Product ids on a sale are now checked to belong to the resolved shop — that closes a
  cross-shop hole and turns a former 500 into a readable 400.
- **Sign out every device (rule 12).** `AppUser.SecurityStamp` travels in the cookie
  and is compared on every request. `POST /api/users/sign-out-all-devices` (Owner)
  gives every member of that shop a new stamp, which kills their live cookies without
  touching a password. It signs the Owner out too.
- **Making shops and people (rule 14).** `POST /api/setup/organization` creates a shop,
  its chart of accounts, its tax rate and its first Owner in one transaction — and
  answers 404 outside Development. `POST /api/users` lets an Owner add Cashiers.
- **The till has a login.** `LoginScreen`, `ShopChooser` (only shown when someone
  belongs to more than one shop — nothing auto-picks), a sign-out button, and 401
  handling that returns to the login screen. The hardcoded `ORG_ID` is gone.
- **Migration `SaleSellerAndSecurityStamp`** adds both columns. Applied and verified.

**Verified live** against the database, end to end: every endpoint refuses an
unauthenticated caller; an Owner of one shop naming another shop's id gets 403; a
Cashier gets 403 on reports, accounts, tax and adding users but may sell; cost is a
number for an Owner and null for a Cashier; a sale naming a foreign product id is
refused; sign-out-all-devices killed two live cookies; the setup endpoint is 404
outside Development; the login cookie carries an expiry. 19 domain tests pass.

**Left behind by that verification, delete it:** a shop called "ZZ Verification Shop"
with `verify.owner@salio.local` and `verify.cashier@salio.local`, whose throwaway
passwords are in the session transcript. Cleanup SQL is with that session's notes.

### Fourth slice: closing the gaps (2026-09-22)

- **Login rate limit.** ASP.NET Core's built-in rate limiter, no package: 5 attempts a
  minute per IP on `/api/auth/login` only, then 429 with `Retry-After: 60` and a
  readable message. **Verified live**: attempts 1–5 passed through, 6 and 7 got 429,
  the 429 carries the CORS header so the till can read it, and other endpoints are
  not limited. Behind a reverse proxy every caller shares one IP, so production
  needs forwarded headers configured before this is meaningful.
- **`GET /api/sales?from=&to=`.** A Cashier sees only the sales they made; an Owner
  sees the whole shop's, with the seller's name. No cost or profit in the response.
  Verified: 401 without a login. Later verified live with real data (section 4).
- **`PasswordServiceTests`**: 6 tests — the hash is not the password, right and wrong
  passwords, an empty hash never matches, two hashes of one password differ, one
  user's hash does not open another. `Salio.Tests` now references
  `Salio.Infrastructure` (a project reference, not a package). **25 tests pass.**

**Decided 2026-09-22: no database test for the resolver.** The rule-9 decision is
covered by the 7 `MembershipRules` tests; the resolver's own database part is a single
`Where(m => m.AppUserId == userId)`, which does not justify a new package.

**Still open:** No per-account lockout:
the limit is per address, so guessing spread across many addresses is slowed, not
stopped.

**Rule 15 (self-service) is parked** by the owner for now. It is not a constraint on
the current slices, so the hardcoded demo organisation in `Program.cs` and
`DemoProductSeeder` stays until rule 15 is picked up. (`web/src/App.tsx` no longer
holds one: the till gets its shop from the login.)

### Fifth slice: PIN login on trusted phones (2026-11-01)

Email and password once per phone, then a 4-digit PIN on that phone.

- **`TrustedDevice`** (table `TrustedDevices`, migration `TrustedDevices`, not yet
  applied): user, a hash of the device secret, a hash of the PIN (null until chosen),
  wrong PINs in a row, a copy of the user's SecurityStamp, created, last used, revoked.
  Never deleted: a dead device gets `RevokedAt`.
- **Why it has no OrganizationId.** Like `AppUser`, a device belongs to a person, not a
  shop (locked rule 10): one phone, one person, who may work in several shops. Which
  shops they can touch is still decided by their memberships after unlocking, exactly
  as after a password login. It is the second exemption from locked rule 2.
- **A password login trusts the phone afresh.** It revokes whatever device the phone's
  cookie named, makes a new one with a 64-character secret from
  `RandomNumberGenerator`, stores only the secret's hash (`PasswordService`, so
  Identity's hasher: locked rule 11), and sets the second cookie, **`salio_device`**:
  `{deviceId}.{secret}`, HttpOnly, SameSite=Lax, Secure outside Development,
  Path=/api/auth, **90 days**. The login response gains `pinSet: false`.
- **`GET /api/auth/device`**, no login needed: `{ fullName, pinSet }` for a trusted phone,
  otherwise the cookie is deleted and the answer is 204. A device is trusted when its
  secret matches, it is not revoked, the person is active, and its stamp equals the
  person's current stamp. One private helper in `AuthController` does all of that.
- **`POST /api/auth/pin`** (signed in): only for this phone's trusted device and its own
  user, else 403. `PinRules.IsAcceptable` refuses anything but 4 digits, all the same
  digit, and straight runs up or down: "Choose 4 digits that aren't all the same or in
  a row". Blocked by `PasswordChangeGate` while a temporary password is owed, so a PIN
  comes after the new password.
- **`POST /api/auth/pin-unlock`**, rate limited with the login policy (one shared
  5-a-minute bucket per address for passwords and PINs). The decision is
  `PinUnlockRules.Decide`, written by the owner. Unlocked gives the same 12-hour
  session cookie as a password login and the same body (`pinSet: true`). A wrong PIN
  answers 401 `{ attemptsLeft }`. Five wrong in a row, or a device no longer trusted,
  revokes it, deletes the cookie, and answers 401 `{ signInWithPassword: true }`.
- **Sign out every device kills devices too**: it changes the stamp, and a device whose
  stamp copy no longer matches is dead. **Changing your own password** also changes
  the stamp; it carries this phone's device over to the new stamp, so the phone keeps
  its PIN and every other phone's device dies.
- **Logout is unchanged**: it ends the session and keeps the device, so the PIN works
  next time.
- `PasswordService` gained `Check(user, storedHash, plain)`, the same Identity check
  against a hash other than the password.
- **web**: `PinPad.tsx` (four dots, 64px keys, 28px digits, a Delete key with the word,
  keyboard digits and Backspace), `ChoosePinScreen.tsx` (typed twice; "Those didn't
  match. Try again."; the server's refusal shown), `PinUnlockScreen.tsx` (the person's
  name, "Wrong PIN. N tries left.", "Use email and password instead"). `auth.ts`
  `findWayIn()` decides at start-up, on sign out and on every 401: `/api/auth/me`, then
  `/api/auth/device`; PIN pad if `pinSet`, otherwise email and password. The shop
  chooser still follows for anyone in more than one shop.

**Verified:** API builds; 21 `PinRulesTests` pass; the 7 `PinUnlockRulesTests` fail with
`NotImplementedException` until `Decide` is written. Web type check passes. In headless
Edge at 360px against a faked API: the PIN pad on start-up with the name, wrong-PIN
counts down to 1, the fifth wrong PIN lands on email and password with the message,
password login leads to choosing a PIN, a mismatch starts over, 1234 shows the server's
refusal, sign out returns to the PIN pad, the right PIN reaches the till, no sideways
scroll.

**Not verified:** anything against the real API or database (the migration is not
applied), the cookie in a real browser's DevTools, the 429, sign-out-all-devices
killing a PIN. The hand checklist in the conversation of 2026-11-01 covers these.

### Counting the stock (2026-11-02)

- **`POST /api/stock-counts`**, Owner only. Body: `countDate` and lines of `productId`
  and `countedQuantity` (0 or more, at most three decimals, no product twice). A count
  need not cover the whole shop.
- **Each product that differs** gets one `Adjustment` movement for counted minus
  expected, carrying the product's weighted average cost from `StockService`. The value
  of each line is rounded once to whole cents.
- **One journal entry for the whole count**, dated `countDate`, `SourceType`
  `StockAdjustment`: missing stock debits 5700 Stock Loss and credits 1200 Inventory;
  found stock is the reverse; a count with both is netted into one entry. Matching lines
  write nothing. A count where every line matches writes nothing at all. Differences
  whose values cancel exactly write the movements but no journal entry.
- **5700 Stock Loss** was added to `ChartOfAccountsSeeder` as an expense (still adds
  only missing codes). It cannot be used as money out, and cannot be switched off,
  like 5000.
- Movements and the journal entry go through `StockPoster` and `JournalPoster` in one
  transaction. A count dated in a filed tax period is refused by `JournalPoster`.
- **Expected stock is what Salio shows now**, not on `countDate`; a past-dated count is
  compared with today's figure.
- Placeholders and open questions: known gaps 27-30.

**Verified:** Release build; the journal and stock guard tests pass with it.
**Not verified:** never run against the API or a database.

### The Salio loader (2026-11-07, web only)

- **`SalioWordmark.tsx`**: the wordmark from `assets/salio-wordmark.svg` as JSX. Five
  letter groups (`salio-letter`), the i's dot as `salio-dot`. `animated` adds
  `salio-animated`, which runs the wave and the dot's hop (CSS under "Salio loader" in
  `tailwind.css`; none with reduced motion).
- **`SalioLoader.tsx`**: the animated wordmark in `text-primary` with a message in
  `text-muted-foreground`, `role="status"`. `"full"` covers the screen with the form left
  mounted underneath; `"inline"` sits in one section. No timers of its own.
- **`useShowLoader.ts`**: the rule for when it is drawn. **Shown only after a wait has
  lasted 300 ms; once shown, kept at least 600 ms.** It never delays the request; it only
  decides what is drawn. Timers are cleared when the screen goes.
- **Where:** full screen on "Create my shop" (`Creating your shop…`) and on sign-in
  (`Signing you in…`). Inline in place of the old "Loading…" spinner on start-up, Sales,
  Owed (list and statement), the customer picker, Money out (categories and entries),
  Opening stock, End of month and People.
- **Not covered, on purpose:** the till never shows a loader. Paying keeps its disabled
  Cash/M-Pesa/Credit buttons and the cart stays visible. The till's shelf and the Parts
  list had no loading state before and still have none. Buttons keep their "Saving…" text.
- **Breaks a motion rule in `index.css` ("Nothing loops"):** the loader repeats for as
  long as the wait lasts, with no stop after 10 seconds like the old spinner.
  `.motion-loading` in `index.css` is now unused.

**Verified:** web type check passes. **Not verified:** not run in a browser.

### Setup codes, the setup endpoints and UTC dates (2026-11-08, API only)

- **One setup-code service.** `SetupCodes` (static: `Generate`, `Normalise`, `IsWellFormed`,
  `Format`) and `SetupCodeService` (scoped: `IssueAsync`, `FindRedeemableAsync`, `ClaimAsync`,
  `RevokeAsync`, `ListAsync`) in `Salio.Infrastructure/Security`. The command line uses it now;
  the admin page will use the same one. **Six characters** from `ABCDEFGHJKMNPQRSTVWXYZ23456789`
  (no I, L, O, U, 0, 1), stored upper case with no dash, shown as `K7M-P29`. Typed with or
  without the dash, any case. **Expires after 7 days, works once, one shop.** Stored in plain
  text on purpose (the reason is written on the class): it is a short-lived token for one empty
  shop, not a password, and the developer must be able to read it back to resend it.
- **`dotnet run --project Salio.Api -- new-setup-code "<shop name>" <email>`** prints three
  lines (Code, Link, Expires) and exits without starting the web server. All app logging is off
  for it. Bad arguments: one usage line on stderr, exit code 1. The link is `PublicBaseUrl` +
  `/setup?code=K7MP29`; `PublicBaseUrl` is `https://salio.example.com` in `appsettings.json`
  (placeholder: set the real host) and `http://localhost:5173` in Development. The SDK itself
  still prints "Using launch settings..." and "Building..." before them; `dotnet run -v q
  --no-build ...` prints only the three lines.
- **`POST /api/setup/check-code`** `{ code }` returns `{ shopName }` only. **`POST /api/setup/shop`**
  now takes `businessKind` ("Sales" or "Services"), `trade` (1-120) and `sellableNoun` (1-30)
  and stores all three on the shop. The "services not supported" refusal is gone: Services never
  switches stock off. Unknown, expired, revoked, used and badly typed codes all get the same
  400 words. The shop row is now written before the code is claimed (the code's foreign key
  points at it, so claiming first failed with a 500); a lost race still rolls everything back.
- **Rate limit `setup-redeem`**: 10 an hour per address, shared by check-code and shop. The 429
  says "Too many tries. Wait an hour and try again, or ask for a new code." Login keeps its own
  words. The limiter lives in memory, so a restart clears it.
- **Deleted:** the Development-only `POST /api/setup/codes` and its two DTOs. `POST /api/users`
  was already gone (404); nothing in `web/src` or `Salio.Tests` calls it.
- **`GET /api/auth/me`**: each shop also carries `businessKind` and `trade`. `itemWord` stays and
  is the shop's `SellableNoun`.
- **`PUT /api/organizations/sellable-noun`** `{ sellableNoun }`: Owner only, shop decided by
  `OrganizationGuard`, trimmed, 1-30 characters, 204.
- **Every DateTimeOffset a client sends is made UTC as it is read** (`UtcDateTimeOffsetConverter`,
  registered on the controllers' JSON options). A filed return sent with `+03:00` used to be a
  500 from Npgsql; it is now saved as the same moment in UTC.

**Verified:** build clean; `dotnet test` 117 passed (102 before, plus new setup-code and
converter tests). Against the dev database: the command made codes; check-code gave the shop
name for a good code typed `a8h-2qg`, and identical 400s for unknown, expired, revoked and used
codes; a Services shop was set up, signed in, `/me` showed its kind and trade, the word was
changed; a filed return with `+03:00` saved as UTC; the 11th setup try in the hour got the 429.

**Left in the dev database:** the test shop "Phase Two Test Garage" (owner
phase2-owner@example.com, one filed return) and its used code A8H2QG.

### Setup rebuild, web side (2026-11-08)

- **`SetupScreen.tsx` rewritten.** `/setup?code=...` checks the code quietly (POST
  `/api/setup/check-code`) and opens on the shop's name, prefilled from the reply; the
  code is dropped from the address with `history.replaceState`. `/setup` with no code, or
  a refused code, starts on the code box (6 letters and numbers, dash optional). The
  login screen's "Set up a new shop" still works and now sets the path to `/setup`.
- **Questions:** shop name, "What kind of business is this?" (Sales / Services cards,
  one tap), "What does the shop deal in?" (PM's lists, one tap; "Something else" opens
  one text box), the owner. The Parts/Products/Items/Goods chips are gone: the word is
  derived silently from the trade (table in `SetupScreen.tsx`, the only copy). Nothing
  refuses Services. Tax is still sent as "not sure".
- **Progress bar** replaces the "Creating your shop" loader: five real calls (D9), each
  moving the bar 20% only when it returns OK. A failure stops the bar, says what failed
  and what to do, and Try again carries on from that step (never re-creating the shop).
- **After setup:** Sales gets "Your first <word>" as before; Services goes straight to
  the till (D14).
- **`itemWords` (auth.ts):** singular by English rules (ies, ches/xes/sses, s). "Goods",
  "Clothes", and words that are not plural use "item" for the one ("Add an item").
- **Wording sweep:** End of month's "Cost of the parts sold" / "Left after parts" now use
  the shop's word. No other hard-coded user-visible part/product/item was found.
- **What Salio calls what you sell:** `SellableNounSetting.tsx`, Owner only, PUT
  `/api/organizations/sellable-noun?organizationId=...`, then `/api/auth/me` again. On
  More (phone) and at the foot of the Parts list (tablet: there is no More screen there).
- **Services nav order (D14):** Sidebar and More put Parts / Stock in / Opening stock
  after the other places. Nothing hidden.

**Verified:** web type check and build. Headless Edge at 1024x768 and 360px against a
throwaway fake API: code box, refused code, link arrival, kind, both trade lists,
Something else, progress at 40% and 60%, a failed step, 100%, Services nav order, the
word setting. No sideways scroll at 360px.
**Not verified:** against the real API (being rebuilt at the same time).

### The admin area's API (2026-11-08, API only)

- **`/api/admin`** (`AdminController`), for the platform admin only. Not `[Authorize]`:
  `PlatformAdminFilter` (Salio.Api/Security) runs before every admin request, reads the
  user's row from the database (never the cookie), and lets in only `IsActive &&
  !MustChangePassword && IsPlatformAdmin`. Everyone else gets a bare 404 (no body, no
  content type), byte for byte what `/api/zzz` gets. A catch-all action under `/api/admin`
  makes wrong methods and unknown paths 404 too, instead of a 405 that would give the
  area away.
- **Endpoints:** `GET session` (204), `POST setup-codes` `{ shopName, email }` (code, link,
  expiry and a ready-to-paste WhatsApp message), `GET setup-codes` (newest first, with
  status Unused/Redeemed/Expired/Revoked), `POST setup-codes/{id}/revoke` (204; 404
  unknown; 409 used; already revoked is a quiet 204), `GET shops` (name, first active
  Owner's email, created, time of the last sale and nothing else from its sales, D8).
- **Audit:** issuing and revoking each write one `AdminAuditLogs` row in the same
  transaction as the change. Viewing a list writes nothing.
- **One link builder:** `SetupLink.Build` is used by both the command line and the admin
  page; both call `SetupCodeService`.
- **Rate limits:** policy `admin`, 60 per address per 10 minutes on every `/api/admin`
  request, refused with the same bare 404 (D6 said 20; the lead raised it to 60 so the
  admin is not locked out of their own page). The `login` policy is now two chained limits:
  5 a minute AND 20 an hour per address. The 429 words are unchanged, so the hourly limit
  also says "wait a minute".
- **Making someone the admin** is psql only:
  `UPDATE "AppUsers" SET "IsPlatformAdmin" = true WHERE lower("Email") = lower('<email>');`
- **Test:** `PlatformAdminTests.No_request_can_change_IsPlatformAdmin` fails if EF would
  write the column, if any request type or DTO has a property of that name, or if any
  production file other than the entity, its configuration and the filter names it (the
  filter only reads it; no string anywhere may name it). Salio.Tests now references
  Salio.Api for this.

**Verified:** build; `dotnet test` 121 passed. Against the dev database: stranger and a
signed-in owner got identical bare 404s from admin paths, `/api/admin/nothing` and
`/api/zzz`; an admin issued, listed and revoked a code (2 audit rows); setting the flag
false in psql made the next request with the same cookie a 404; the 61st admin request
in 10 minutes was a 404; the 21st sign-in in the hour was a 429.

**Left in the dev database:** test@salio.local was admin during the test and is false
again; no account is admin. Codes YJQ-P8K (revoked from the admin page) and AEZ-7XW (from
the command line, revoked in psql). Two AdminAuditLogs rows.

### The admin page and "Page not found" (2026-11-08, web only)

- **Routing** (`web/src/Root.tsx`, rendered by `main.tsx`): `/` and `/setup` open the till as
  before; `/admin` asks `GET /api/admin/session` and shows the admin page only on a 204;
  every other address, and `/admin` for anyone else (signed out, or a signed-in owner),
  shows the same `NotFound.tsx` ("Page not found", a "Go to Salio" link to `/`, title
  "Page not found"). Before this, an unknown address showed the login screen or the till.
  `App.tsx` now reads `?code=` only on `/` and `/setup`.
- **Admin page** (`web/src/AdminScreen.tsx`, title "Salio admin", lazy-loaded so its words
  are not in the main file; the chunk can still be fetched by URL, the API is the gate):
  issue a code (shop name + email; code, link and WhatsApp message each with Copy, falling
  back to selecting the text), the codes table (Revoke on Unused rows only, with a confirm
  step), and the shops table (name, owner email, created, last sale or "No sales yet").
  Sign out only. Nothing links to `/admin`.
- **Verified** against the real API and Vite on 5173 with headless Edge at 1024x768:
  `/zzz` signed out, `/admin` signed out, `/admin` and `/zzz` as a signed-in owner gave
  byte-identical screenshots. As admin: issued, copied view, revoked, lists refreshed. No
  sideways page scroll at 1024 or 360 (tables scroll in their own box). Build passes; lint
  has only the known App.tsx error.
- **Left in the dev database:** test@salio.local was admin during the test and is false again
  (no admin accounts). Three revoked test codes for "Kamau Motor Spares" (ZJQ-QVZ, WST-7KY,
  XS7-DKF) and their AdminAuditLogs rows.

### Quick-add at the till, and "Parts needing a cost" (2026-11-09, web only)

- **Quick-add** (`web/src/QuickAddForm.tsx`, opened from `App.tsx`): when the till's search
  finds nothing, the empty state is one button, `Add "<what was typed>"`. It opens one screen:
  Name (prefilled, focused), Selling price, Unit cost ("leave blank if you do not know yet"),
  Quantity. Enter saves (it is a form). Sends `POST /api/products/quick-add` with money in
  cents; the answer goes straight into the open cart and onto the shelf, then the shelf
  reloads. Quantity without a unit cost is stopped before sending with "Add a unit cost to
  record the quantity, or leave the quantity empty." (D8). A Cashier sees Name and Selling
  price only and sends cost and quantity as null. A refusal shows the server's `detail`.
- **"<Parts> needing a cost"** (`web/src/NeedingCostScreen.tsx`, Owner only): from More on a
  phone and the sidebar on a tablet. `GET /api/products/needing-cost`; each row shows name,
  SKU, how many sold and the KES booked with no cost. Above the list: "Your profit is
  overstated: KES X of sales were booked with no cost." and "Setting a cost applies from
  now on. Sales already made stay as they were booked." Empty: "Every <part> has a cost."
  Tapping a row opens Stock in with that part already on the delivery (`StockInScreen`
  gained optional `startWith` and `backLabel`; quantity box focused).
- **Badge**: `GET /api/products/needing-cost/count`, Owner only, asked after every shelf
  reload (so after a sale, a quick-add, a delivery, and the 30-second poll) and when More
  opens. Shown on the More row, the tablet sidebar item, and the phone's More tab when above 0.
  New nav place `needingCost` with its own icon in `NavIcons.tsx`.
- **Verified** against a fake API (not in the repo) with headless Edge at 1024x768 and 360px:
  no sideways scroll on any of the screens; build passes; lint has only the known App.tsx
  error. Not yet run against the real Phase 2 API.

### Product import, quick-add and parts needing a cost: the API (2026-11-09, API only)

- **D3, writers:** the opening-stock and stock-receipt logic moved unchanged out of their
  controllers into `Salio.Infrastructure/OpeningStockWriter.cs` and `StockReceiptWriter.cs`
  (same checks in the same order, same accounts, same SourceTypes, every message word for
  word). A writer opens its own transaction only when none is open, so the import can wrap
  many in one. `OpeningStockWriter` takes an optional SourceId (D4) and a sale check:
  `NoSaleInShop` (the Opening stock screen, unchanged) or `NoSaleOfTheseProducts` (import and
  quick-add, for parts created a moment ago; lead to confirm).
- **D5, `JournalReverser`:** posts the mirror of an entry through JournalPoster (dated today in
  Nairobi, SourceType Reversal, SourceId = original), refuses a second reversal, and posts
  opposing Adjustment movements through StockPoster. Nothing is edited or deleted.
- **`ImportRowRules`** (Domain, pure) + `ImportRowRulesTests`; CSV and XLSX readers in
  `Salio.Infrastructure/Imports/` (no packages) + `ImportFileReaderTests`.
- **`/api/imports`:** `GET products.csv`, `POST preview`, `POST commit`, `GET`, `POST {id}/undo`,
  Owner only, through `ProductImportService`. Preview also returns `rowsUnchanged`.
- **`/api/products`:** `POST quick-add` (Owner and Cashier), `GET needing-cost`,
  `GET needing-cost/count`; product list now carries category, barcode, reorderLevel.
- **D12:** `deploy/salio.caddy` lets `/api/imports*` and `/api/supplier-invoices*` take 11MB;
  everything else under /api keeps 1MB. Import actions have a 6 MB request limit.
- **Guard:** `ImportGuardTests` fails if import code names JournalLine, DebitMinor or CreditMinor,
  or if JournalReverser sets them to anything but the original line's swapped values.

**Verified:** build; `dotnet test` 192 passed (125 before). Against the dev database, as
phase2-owner on "Phase Two Test Garage": export, preview with errors (nothing written),
refused commit, clean commit (1200 and 3000 each moved by 600,200), undo (the original entry
is still there and a Reversal entry mirrors it; the net is back to 300,000), a second undo
gave 409, kept-because-received and kept-because-sold, quick-add with and without cost,
needing-cost, .xlsx preview.

**Left in the dev database (Phase Two Test Garage):** parts Q-W3W4YQ (wiper, sold once with
no cost), Q-VJDT8P and Q-5ERDYX (quick-add opening stock), HDL-1 (kept, with a stock receipt),
CHN-1 (kept, sold once), and switched-off parts named `...~undone-...`. Three import batches,
two sales, and their journal entries.

### Supplier invoice reader: the API, with the provider slot left open (2026-11-09, API only)

- **Reader:** `Salio.Infrastructure/Invoices/IInvoiceReader.cs` (`ReadAsync(Stream, contentType, ct)` ->
  `InvoiceExtraction`). `FakeInvoiceReader`: six canned motorcycle-spares lines, always `IsDemo = true`,
  printed total KES 30,840.00 against lines of 30,240.00 so the totals check shows a difference.
  `ConfiguredInvoiceReader` (registered as `IInvoiceReader`): no `InvoiceReader:ApiKey` -> the demo reader;
  a key -> the provider named in `InvoiceReader:Provider`, and since none is built it throws
  `InvoiceReaderNotBuiltException` -> 503, nothing kept. A key with no provider named also refuses. No
  provider chosen, no key anywhere in the repo; `appsettings.json` has empty `InvoiceReader:Provider` and
  `InvoiceStorage:Path` placeholders only.
- **Matching** (`Salio.Domain/Services/InvoiceMatchRules.cs`, pure): top 3 active parts per line,
  0.6 x shared words (near-typos count) + 0.4 x letters alike; SKU in the line text -> at least 0.95;
  below 0.25 not shown. Suggest only: `MatchedProductId` is never pre-set. Lines start ticked when
  quantity and cost are both above zero.
- **Totals** (`InvoiceTotals.cs`, pure): ticked lines only, rounded exactly as
  `StockReceiptService.TotalCost` (a test holds them together). Unreadable total -> `totalsAgree` false and
  `differenceMinor` 0; the screen tells that apart by `extractedTotalMinor` being null.
- **`/api/supplier-invoices`** (Owner only): `POST` (multipart `file`, PNG/JPEG/PDF by first bytes, 10 MB,
  saved as `<invoice id>.<ext>` under `InvoiceStorage:Path`, Development default `App_Data/invoices`, now
  git-ignored), `GET {id}`, `PUT {id}` (-> Reviewed), `POST {id}/commit`, `POST {id}/discard` (204, keeps
  file and rows). Commit body `{ paidWith, receiptDate, lines? }`: there is no column for createNew, so
  new parts can only be asked for in the commit's `lines` (same shape as PUT). In one transaction: claim
  the invoice, create new parts (SKU `Q-xxxxxx` via the new shared `ProductSkus`, now also used by
  quick-add; no opening stock), then ONE `StockReceiptWriter.WriteAsync`. Invoice code never touches
  journal lines, entries or stock movements (`InvoiceGuardTests`).
- The upload reads its form by hand (`ReadFormByHandAttribute`) so a file over the limit gets a plain
  sentence rather than a validation dump.

**Verified:** build; `dotnet test` 224 passed (192 before). As phase2-owner on "Phase Two Test Garage":
PNG upload -> demo, 6 lines, total 30,840.00 vs 30,240.00, difference 600.00; refused commits (no part
chosen, no paidWith, no price for a new part, zero cost) wrote nothing; PUT (spark plug matched to
Q-VJDT8P, headlamp to HDL-1, brake pad as new at 750.00, three lines unticked) -> Reviewed, lines 10,400.00;
commit -> JournalEntries for the shop 8 -> 9 (Purchase 1 -> 2), one entry SourceType Purchase,
SourceId = stockReceiptId, Dr 1200 / Cr 1000 10,400.00, three Receipt movements. Spark plug average
(10 x 300.00 + 20 x 220.00) / 30 = 246.6667 -> 246.67 (was 300.00); headlamp (3 x 406.67 held as
1,220.00 + 10 x 150.00) / 13 = 209.23. Second commit, PUT and discard after commit -> 409. PDF upload ->
demo, then discarded (204; commit after -> 409). Text named .png -> 400; 10.5 MB -> 400; 11 MB -> 413,
both "The file is larger than 10 MB...". With `InvoiceReader__Provider=someprovider` and a dummy
`InvoiceReader__ApiKey` -> 503 "InvoiceReader:Provider 'someprovider' is configured but no reader for it
has been built yet", no row and no file kept.

**Left in the dev database (Phase Two Test Garage):** invoice INV-2026-0457 committed (stock receipt
bcc5941d), a discarded PDF invoice, part Q-YDQXX8 "Brake pad Boxer BM150" (10 in stock at 450.00), and
the stock received on Q-VJDT8P and HDL-1. Two files in `Salio.Api/App_Data/invoices/`.

### CSV import screens and "Read a supplier invoice" (2026-11-09, web only)

- **Import <parts>** (`web/src/ImportScreen.tsx`, Owner only, from More and the tablet sidebar):
  step 1 "Start from your own <parts>" downloads `GET /api/imports/products.csv` (no blank
  template); step 2 drop or choose a .csv/.xlsx, refused in the browser above 5 MB; step 3 the
  preview from `POST /api/imports/preview`: "148 new parts, 12 updated, 3 problems" (singular
  and plural right), problems with row, column, the cell as it is ("(empty)" for blank) and the
  server's sentence; warnings in a quieter list; Import disabled while there are problems or
  nothing would change. Import re-sends the same File to `POST /api/imports/commit`. Step 4
  "Imported: N new, M updated" with "Undo this import" (asks first), then shows what was undone
  and every part kept, with its reason.
- **Import history** (`web/src/ImportHistoryScreen.tsx`): `GET /api/imports`, when (en-KE),
  who, file, counts, Undo or "Undone <date> by <name>". Shared undo pieces in
  `web/src/importApi.ts` and `web/src/ImportUndo.tsx`.
- **Read a supplier invoice** (`web/src/InvoiceScreen.tsx`, `InvoiceCleanUp.tsx`,
  `invoiceImage.ts`, `InvoiceReview.tsx`, Owner only): take a photo or choose a photo/PDF. A
  photo is cleaned in the browser before upload: crop box with four corner handles, 90° turns
  and a ±15° straighten slider; then grey, lighting flattened, contrast stretched, black/white
  with an Otsu default and an Ink slider; scaled so the longest edge is 2500 px (never more
  than 2x); sent as PNG (never JPEG), with "Original X MB → cleaned Y KB" shown. A PDF goes
  as it is. The review: a full-width "Demo extraction — not a real reading" banner when
  `isDemoExtraction`; supplier, number and date editable; every line editable with Accept,
  suggestions with score as %, search all parts, or "Create as a new part" with a required
  selling price. Totals line in the PM's words, live as lines change, amber with the gap in
  red when they differ, green when they agree. Save = PUT. Commit asks Paid with (no default)
  and the date, PUTs, then `POST .../commit` with `{ paidWith, receiptDate, lines }` (the lines
  carry createNew/newSellPriceMinor, which PUT does not store). Discard asks first.
- **Quick-add**: unit cost filled with quantity empty is now stopped before sending with "Add
  the quantity on the shelf to record the unit cost, or leave the unit cost empty."
- **Nav**: three new places (`importParts`, `importHistory`, `readInvoice`) with icons; moved
  with the stock places for a Services shop.
- **Verified** against a fake API (not in the repo) with headless Edge at 1024x768 and 360px:
  no sideways scroll on any screen. A 2.6 MB JPEG test photo (3000x4000, tilted 3.5°) was sent
  as a 152 KB PNG (1746x2500). Build passes; lint has only the known App.tsx error. Not yet
  run against the real Phase 3 API.
