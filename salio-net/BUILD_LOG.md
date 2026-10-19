# Salio — build log

A running record of what has been built, why the non-obvious choices were made,
what has actually been verified, and what is still missing. Rules of the project
live in `CLAUDE.md`; this file is the state of the work.

Last updated: 2026-10-19 (sales list, receipts, stock receipts, expenses and drawings, P&L split).

---

## 1. Status

| Stage | State |
|---|---|
| 1. Database and ledger | Working |
| 2. Products and stock | Working. Stock receipts added — see stage 9 |
| 3. The till | Working in the browser, unpolished |
| 4. Shifts and payments | Payments done. Shifts half-built: entity exists, nothing uses it |
| 5. Reports | Four reports working. Profit and loss now splits cost of sales from running costs |
| 6. Tax view | Turnover Tax, filing pack, filed returns — built, never run against real data |
| 7. Login, users and roles | Login, roles, shop boundary and login rate limit working |
| 8. Seeing the shop | Sales list and receipt built. No day sheet screen |
| 9. Stock receipt | Endpoint and screen built. Endpoint verified live (cash only) |
| — Expenses and drawings | Not a numbered stage in `CLAUDE.md`. Endpoints and screen built; verified live against the P&L |

The till sells: a sale writes a receipt, stock movements and a balanced journal
entry in one database transaction.

---

## 2. What exists

### Salio.Domain (no EF, no ASP.NET)

- `Money` (minor units), entities: Organization, Account, JournalEntry, JournalLine,
  Product, StockMovement, Sale, SaleLine, Payment, Shift, TaxRate, FiledReturn,
  AppUser, Membership.
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
| POST | `/api/setup/organization` | Shop + chart of accounts + tax rate + first Owner. **404 outside Development** |
| POST | `/api/users` | **Owner only.** Adds a Cashier or Owner to the caller's shop |
| POST | `/api/users/sign-out-all-devices` | **Owner only.** New stamps for the shop; kills live cookies |
| GET | `/api/organizations` | Only the caller's own shops |
| GET | `/api/accounts?organizationId=` | **Owner only.** Active accounts |
| POST | `/api/accounts` | **Owner only.** Name only. Always an expense account; the code is the lowest free number from 5001 to 5899. Refuses a duplicate name, and any name containing "drawing" |
| DELETE | `/api/accounts/{id}` | **Owner only.** Switches off (`IsActive = false`), never deletes. Expense accounts only, not 5000, and only if the account has no journal lines |
| GET | `/api/products?organizationId=` | Shelf list. `averageCostMinor` is null for a Cashier |
| GET | `/api/products/{id}?organizationId=` | Whole entity, still includes eTIMS and `isActive` |
| POST | `/api/products` | **Owner only.** 409 on duplicate SKU |
| POST | `/api/sales` | The till's main call. Records who sold. Contract in section 5 |
| GET | `/api/sales?from=&to=` | A Cashier's own sales; an Owner sees all, with seller names |
| GET | `/api/sales/{id}?organizationId=` | One sale as a receipt: lines with name and SKU, payments, seller. 404 for a sale the caller may not see |
| POST | `/api/stock-receipts` | **Owner only.** Goods arriving. `paidWith`: 0 Cash, 1 M-Pesa, **3** Credit (2 is Card and is refused). Movements and journal entry in one transaction |
| POST | `/api/expenses` | **Owner only.** Money out: debit an expense account or 3100 Drawings, credit Cash or M-Pesa. Refuses Sales, Inventory, any other account, and 5000 |
| GET | `/api/expenses?from=&to=` | **Owner only.** Each entry with date, category, amount, Cash or M-Pesa, note, and `isDrawings` |
| POST | `/api/journal-entries` | **Owner only.** Manual entry; 400 if unbalanced |
| GET | `/api/journal-entries/{id}?organizationId=` | **Owner only.** One entry with lines |
| GET | `/api/reports/trial-balance,profit-and-loss,stock-valuation,day-sheet` | **Owner only.** All derived on read |
| GET | `/api/tax/turnover-tax`, `/api/tax/filing-pack` | **Owner only.** Figure plus the exceptions behind it |
| POST | `/api/tax/filed-returns` | **Owner only.** Records that she filed; locks the period |

### web/ (React 19 + Vite + TypeScript + Tailwind v4)

`src/LoginScreen.tsx`, `src/ShopChooser.tsx` and `src/auth.ts` handle signing in;
the chooser appears only for someone who belongs to more than one shop. `src/App.tsx`
is the till itself:

- Search box fixed at the top; results are the only scrolling area.
- Result row: name left, price right in tabular figures; second line is shelf chip,
  SKU, and either "8 left" or an amber "Out of stock".
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

**Not verified**
- **No real sale has ever been recorded.** Everything so far is seeded demo data in
  a sandbox: no real customer, no real shop takings, nothing anyone has relied on.
- The cashier cannot change a price at the till, so the below-cost warning has never
  fired in practice. See gap 3.
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

1. **The demo data is unusable for judging any report.** Stock went negative before
   receiving existed, so stock value, cost of sales and gross profit on the demo shop
   do not describe anything real. *(This entry was cut short when it was written; the
   rest still needs adding.)*
2. **`taxBand` is hard-coded to "A"** in the till. Products have no tax band column, so
   no screen can supply it honestly. A non-VAT shop on Turnover Tax is probably band D —
   confirm with KRA.
3. **The till cannot edit a line's price.** `addToCart` fixes `unitPriceMinor` to the
   product's `sellPriceMinor`, and the only editable field on a cart line is quantity.
   A spares counter haggles. Checked in `web/src/App.tsx` on 2026-10-19: still true.
4. **`saleDate` uses `toISOString()`**, which is UTC. Between midnight and 3am Kenyan
   time it books the sale to the previous day, so it also drops out of "Today" on the
   sales list.
5. **Only the demo shop picks up new chart-of-accounts codes by itself.** The seeder
   runs at Development startup for the demo shop, and when a shop is created. Every
   other existing shop lacks 3100 Drawings and 5100–5900 until the seeder is run for
   it, so its Money out screen shows no categories.
6. **A category that has been used can never be switched off**, because an account
   with journal lines is refused. It stays in the Money out list for good. Undecided:
   whether a used category should be hideable from the picker while its lines still
   count in reports.
7. **Stock movements are tied to their receipt only by the note.** The receipt id's
   first 8 characters are in each movement's `Note`; there is no column. Enough to
   read, not a real link.
8. **Sales are not linked to shifts.** `Sale.ShiftId` is always null; no shift service
   or endpoints exist. `Salio.Tests/ShiftServiceTests.cs` was deleted on 2026-09-19 so
   the test project would build — it covered a `ShiftService` that was never written.
   Recover it from git history when shifts are built.
9. **CORS is a development-only policy** allowing `http://localhost:5173`.
10. `GET /api/products/{id}` still returns the raw entity including eTIMS fields.
11. Duplicate SKU is pre-checked, but two simultaneous creates can still reach the
    database's uniqueness rule and produce a 500.
12. Sale numbers come from `COUNT(*) + 1`, which two tills would collide on.
13. The hand-made `SODA-500` demo product's stock was never booked to Inventory, so the
    books are KES 70 below the shelf.
14. The till's header now holds Sales, Stock in, Money out and Sign out. On a narrow
    phone the shop and user name are squeezed to almost nothing.

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
accounts, the Turnover Tax rate, and 12 motorcycle spare parts with opening stock
(one deliberately at zero, for testing out-of-stock display). Seeding is per SKU, so
it never duplicates and never touches products made by hand.

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
`SaleSellerAndSecurityStamp`.

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
