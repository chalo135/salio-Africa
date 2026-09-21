# Salio — build log

A running record of what has been built, why the non-obvious choices were made,
what has actually been verified, and what is still missing. Rules of the project
live in `CLAUDE.md`; this file is the state of the work.

Last updated: 2026-09-21 (Stage 7 groundwork).

---

## 1. Status

| Stage | State |
|---|---|
| 1. Database and ledger | Working |
| 2. Products and stock | Working (no stock receipt endpoint yet) |
| 3. The till | Working in the browser, unpolished |
| 4. Shifts and payments | Payments done. Shifts half-built: entity exists, nothing uses it |
| 5. Reports | Four reports working |
| 6. Tax view | Turnover Tax, filing pack, filed returns — built, never run against real data |
| 7. Login, users and roles | Rules agreed, nothing built |

The till sells: a sale writes a receipt, stock movements and a balanced journal
entry in one database transaction.

---

## 2. What exists

### Salio.Domain (no EF, no ASP.NET)

- `Money` (minor units), entities: Organization, Account, JournalEntry, JournalLine,
  Product, StockMovement, Sale, SaleLine, Payment, Shift, TaxRate, FiledReturn.
- Enums: AccountClass, SourceType, StockMovementType, SaleStatus, EtimsStatus,
  PaymentMethod, ShiftStatus, TaxType.
- Services: `LedgerService` (balance rules), `SaleService` (builds a sale's journal
  lines), `StockService` (quantity on hand, weighted average cost).

### Salio.Infrastructure

- `SalioDbContext` + one `IEntityTypeConfiguration` per entity.
- `JournalPoster` — the only writer of journal rows. Also enforces the filed-period lock.
- `StockPoster` — the only writer of stock movements. Insert only.
- `ReportService` — trial balance, profit and loss, stock valuation, day sheet.
- `TaxService` — turnover tax, filing pack, recording a filed return.
- Seeders: `ChartOfAccountsSeeder`, `TaxRateSeeder`, `DemoProductSeeder`.

### Salio.Api

| Method | Route | Notes |
|---|---|---|
| GET | `/api/organizations` | All organisations |
| GET | `/api/accounts?organizationId=` | Active accounts |
| GET | `/api/products?organizationId=` | Till shelf list: adds `quantityOnHand`, `averageCostMinor`; no eTIMS fields |
| GET | `/api/products/{id}?organizationId=` | Whole entity, still includes eTIMS and `isActive` |
| POST | `/api/products` | 409 on duplicate SKU |
| POST | `/api/sales` | The till's main call. Contract in section 5 |
| POST | `/api/journal-entries` | Manual entry; 400 if unbalanced |
| GET | `/api/journal-entries/{id}?organizationId=` | One entry with lines |
| GET | `/api/reports/trial-balance,profit-and-loss,stock-valuation,day-sheet` | All derived on read |
| GET | `/api/tax/turnover-tax`, `/api/tax/filing-pack` | Figure plus the exceptions behind it |
| POST | `/api/tax/filed-returns` | Records that the shopkeeper filed; locks the period |

### web/ (React 19 + Vite + TypeScript + Tailwind v4)

`src/App.tsx` is the whole till, one file on purpose:

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

**Not verified**
- **No real sale has ever been recorded.** Everything so far is seeded demo data in
  a sandbox: no real customer, no real shop takings, nothing anyone has relied on.
- The cashier cannot change a price at the till, so the below-cost warning has never
  fired in practice. See gap 4.
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

1. **`taxBand` is hard-coded to "A"** in the till. Products have no tax band column, so
   no screen can supply it honestly. A non-VAT shop on Turnover Tax is probably band D —
   confirm with KRA.
2. **`saleDate` uses `toISOString()`**, which is UTC. Between midnight and 3am Kenyan
   time it books the sale to the previous day.
3. **No stock receipt endpoint.** Stock can only arrive through seeding, so a shop
   cannot restock, and the below-cost warning cannot be triggered from the app.
4. **The till cannot edit a line's price.** `addToCart` fixes `unitPriceMinor` to the
   product's `sellPriceMinor`, and the only editable field on a cart line is quantity.
   A spares counter haggles.
5. **Sales are not linked to shifts.** `Sale.ShiftId` is always null; no shift service
   or endpoints exist. `Salio.Tests/ShiftServiceTests.cs` was deleted on 2026-09-19 so
   the test project would build — it covered a `ShiftService` that was never written.
   Recover it from git history when shifts are built.
6. **Profit and loss treats every expense account as cost of sales.** Correct today
   because account 5000 is the only expense account. Add Rent or Wages and gross profit
   becomes wrong.
7. **No authentication.** `OrganizationId` comes from the client and is hard-coded in
   the till. CORS is a development-only policy allowing `http://localhost:5173`.
8. `GET /api/products/{id}` still returns the raw entity including eTIMS fields.
9. Duplicate SKU is pre-checked, but two simultaneous creates can still reach the
   database's uniqueness rule and produce a 500.
10. Sale numbers come from `COUNT(*) + 1`, which two tills would collide on.
11. The hand-made `SODA-500` demo product's stock was never booked to Inventory, so the
    books are KES 70 below the shelf.
12. Success and failure in the till are `alert()` boxes.
13. `web/src/App.css` is Vite template CSS and is imported nowhere. Dead file.

---

## 7. Running it

The database password is **not in the repo**. `appsettings.json` holds the connection
string without a password, and the full string lives in .NET user secrets under the same
name Program.cs reads (`ConnectionStrings:Default`). A fresh machine needs
`dotnet user-secrets set` before the API will start.

The old password is still in git history (commits c8a3b60, 6cb8bd4, 47b946f), so it
must be treated as compromised and rotated in Postgres. Tick this off here once done.

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

Migrations, in order: `InitialCreate`, `ChartOfAccountsAndJournal`, `ProductsAndStock`,
`SalesAndTaxRates`, `TaxView`, `AddBinLocation`.

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
