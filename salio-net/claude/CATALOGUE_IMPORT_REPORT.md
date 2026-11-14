# Four ways parts get into Salio: report

Run: 2026-11-09. Nothing was committed or pushed. Every change is uncommitted in the working tree, ready for your review.

## In one paragraph

All five phases ran. There is **one migration**. Tests went from **125 to 224, with 0 failing**. QA checked all 18 points by hand at 1024x768, using a browser against the real API, on a fresh shop: **18 PASS, 0 FAIL**, plus four extra checks, all passing. Two paths were built as screens:
- **counter quick-add**, with the "Parts needing a cost" screen;
- **CSV/XLSX import**, with undo.

The invoice reader works end to end on a demo reading, with the provider slot left empty. Path 2, you importing a client's fast movers, is covered by the same import screens and not a separate tool; decision D11 below explains why. No protected file was edited. One existing guard test got a one-line named exemption, which you should look at (decision D15).

---

## The books: what this run does and does not do

**No new code makes up its own debits and credits.** Every journal entry in this run reaches the database through the existing `JournalPoster.PostAsync`, which calls `LedgerService.PostJournal` first. Every stock row goes through the existing `StockPoster`.

What each path calls:
- **Opening stock** (quick-add with quantity, and import of a new part with quantity): `OpeningStockWriter`. Debit 1200 Inventory, credit 3000 Owner's Equity. These are the same lines the Opening stock screen already posted. The code was **moved out of `OpeningStockController`**, with its checks and messages unchanged, so that the import calls the same code as the screen. That controller had never been committed, so the move can't be shown as a git diff. The agent reports it moved unchanged, and the old messages were re-checked live.
- **Stock receipts** (the invoice commit, and setting a cost from "Parts needing a cost"): `StockReceiptWriter`. That code was moved out of `StockReceiptsController` the same way. Its lines come from the existing `StockReceiptService.BuildJournalLines`.
- **Weighted-average cost** is only ever read from the existing `StockService`. An import never sets cost on an existing part.

**Undo reverses; it never deletes or edits.** `JournalReverser` takes an existing entry and posts its mirror image through `JournalPoster`: each debit becomes a credit of the same amount on the same account. The mirror is dated today, with SourceType `Reversal` and SourceId set to the original entry. It also posts an opposing stock movement through `StockPoster`. The original entry and movement stay untouched. QA confirmed this in the database (check 12). That swap is the only place this run builds journal lines, and it computes nothing. Two guard tests hold this:
- `ImportGuardTests.Import_code_never_builds_its_own_journal_lines`
- `InvoiceGuardTests`, which applies the same check to the invoice code.

The existing `JournalGuardTests` and `StockGuardTests` still pass.

---

## Phase 1: schema

**Migration:** `Salio.Infrastructure/Migrations/20261109075442_CatalogueImportAndInvoices.cs` (with its Designer file and the updated snapshot). It only adds; nothing is dropped or renamed. Applied to the dev database, with no data lost (Products 3 rows before and after).

| Table | Contents |
|---|---|
| ImportBatches (new) | Id, OrganizationId, FileName, UploadedByUserId, StartedAt, CompletedAt?, RowsCreated, RowsUpdated, UndoneAt?, UndoneByUserId? |
| ImportBatchChanges (new) | Id, OrganizationId, ImportBatchId, ProductId, ChangeType (Created/Updated), PreviousValuesJson? |
| SupplierInvoices (new) | Id, OrganizationId, UploadedAt, UploadedByUserId, ImagePath, Status (Pending/Reviewed/Committed/Discarded), SupplierName?, InvoiceNumber?, InvoiceDate?, ExtractedTotalMinor?, RawExtractionJson?, CommittedStockReceiptId? |
| SupplierInvoiceLines (new) | Id, OrganizationId, SupplierInvoiceId, LineNumber, RawText, ProposedName, ProposedQuantity, ProposedUnitCostMinor, MatchedProductId?, Accepted |
| Products | New nullable columns **Category**, **Barcode**, **ReorderLevel** (D1) |
| (enum) SourceType | `Reversal` added at the end (D2). It is stored as a number, so no SQL was needed. |

- **Organisation:** all four new tables carry a required OrganizationId. No new exemptions.
- **Foreign keys:** all are Restrict.
- **Money:** all money is `long *Minor`.

**Files:**
- New enums: `Salio.Domain/Enums/ImportChangeType.cs`, `SupplierInvoiceStatus.cs`
- `SourceType.cs`
- Entities: `ImportBatch.cs`, `ImportBatchChange.cs`, `SupplierInvoice.cs`, `SupplierInvoiceLine.cs`, `Product.cs`
- The matching four configurations, plus `ProductConfiguration.cs`
- `SalioDbContext.cs`
- `Salio.Tests/StockGuardTests.cs` (D15)

## Phase 2: import backend and counter

**API:**
- **Moved logic:** `OpeningStockWriter.cs` and `StockReceiptWriter.cs` (with `StockLineInput.cs`). The two controllers are now thin.
- **Reversal:** `JournalReverser.cs`
- **Import rules:** `Salio.Domain/Services/ImportRowRules.cs`, pure with no database, in the house style of MembershipRules. Tests are in `ImportRowRulesTests.cs`.
- **File readers:** `Salio.Infrastructure/Imports/` holds `CsvReader.cs` and `XlsxReader.cs`. They use no package: an .xlsx file is a zip of XML, read with .NET's own zip and XML classes. Also `ImportFile.cs` (limits) and `ImportResults.cs`. Tests are in `ImportFileReaderTests.cs`.
- **Import service and endpoints:** `ProductImportService.cs` and `Controllers/ImportsController.cs`:
  - export CSV
  - preview (writes nothing)
  - commit (one transaction)
  - history
  - undo
- **Products:** `ProductsController.cs` gained quick-add and needing-cost, and its list now carries category, barcode and reorder level. New DTOs `QuickAddProductRequest.cs`, `NeedingCostResponse.cs`; `ProductStockResponse.cs` changed.
- **Registration:** `Program.cs`
- **Upload limit:** `deploy/salio.caddy` (D12).

**Web:**
- **New files:** `QuickAddForm.tsx`, `NeedingCostScreen.tsx`.
- **Sell screen and badge:** `App.tsx`. An empty search now shows `Add "<typed>"`. The new part goes straight into the cart. The badge count lives here.
- **Stock in:** `StockInScreen.tsx` can now open with a part already filled in.
- **Menus:** `MoreScreen.tsx`, `Sidebar.tsx`, `BottomNav.tsx` and `NavIcons.tsx` carry the entries and the badge.

## Phase 3: invoice reader and import screens

**API:**
- **Reader:** `Salio.Infrastructure/Invoices/` holds `IInvoiceReader.cs`, `InvoiceExtraction.cs`, `FakeInvoiceReader.cs`, `ConfiguredInvoiceReader.cs`, `InvoiceReaderNotBuiltException.cs`, `InvoiceFile.cs` (checks the file type from its first bytes) and `InvoiceViews.cs`.
- **Invoice service:** `SupplierInvoiceService.cs`.
- **Pure rules:** `Salio.Domain/Services/InvoiceMatchRules.cs` (suggestions) and `InvoiceTotals.cs` (the arithmetic check).
- **SKU generator:** `ProductSkus.cs`, shared by quick-add and new parts made from an invoice.
- **Endpoints:** `Controllers/SupplierInvoicesController.cs`, plus `ReadFormByHandAttribute.cs`.
- **Settings:** `appsettings.json` has empty placeholders only, with no key. `.gitignore` now ignores `App_Data/`.
- **Tests:** `InvoiceReaderTests`, `InvoiceMatchRulesTests`, `InvoiceTotalsTests`, `InvoiceGuardTests`.

**Web:**
- **Import:** `ImportScreen.tsx`, `ImportHistoryScreen.tsx`, `ImportUndo.tsx`, `importApi.ts`.
- **Invoice:** `InvoiceScreen.tsx` and `InvoiceCleanUp.tsx` (crop corners, 90° turns, ±15° straighten, ink slider). `invoiceImage.ts` does the processing: grey, even lighting, contrast stretch, black-and-white threshold, longest edge 2500px, PNG only. `InvoiceReview.tsx` has the editable lines, matches, totals bar, demo banner and commit dialog.
- **Changed:** `QuickAddForm.tsx` (cost without quantity is caught in the browser), `App.tsx`, `MoreScreen.tsx`, `Sidebar.tsx`, `NavIcons.tsx`.

---

## Trial balance (QA checks 11 and 12)

QA used a fresh shop, "QA2 Spares", and imported 20 new parts, 5 of them with quantity and cost. QA's hand total from the file was KES 12,713.00. There is no trial balance screen, so the figures come from `GET /api/reports/trial-balance`.

| Point | 1200 Inventory (net debit) | 3000 Owner's Equity (net credit) |
|---|---|---|
| Before the import | 2,500.00 | 0.00 |
| After the import | 15,213.00 (+12,713.00) | 12,713.00 (+12,713.00) |
| After undo | 2,500.00 | 0.00 |

The 2,500.00 is the cost QA set from "Parts needing a cost" in check 4. After undo, all 5 original opening-stock entries still exist, and 5 Reversal entries point at them.

## QA results (seen by the QA agent)

| # | Result | What was seen |
|---|---|---|
| 1 | PASS | Searching for a missing part showed only the button `Add "Clutch cable Boxer"`. |
| 2 | PASS | Quick-add at 850 with no cost created Q-6YZATH. It sold (sale S-000001, line cost 0) and appeared in "Parts needing a cost", with a badge of 1. |
| 3 | PASS | "Your profit is overstated: KES 850.00 of sales were booked with no cost." That figure equals the sale. |
| 4 | PASS | Tapping it opened Stock in with the part filled in; QA received 5 @ 500. The part left the list ("Every part has a cost."). A database diff showed no existing journal entry, line or stock movement changed: only 1 new Purchase entry and 1 movement were added. |
| 5 | PASS | Excel 16 opened the exported CSV as 2 rows x 8 columns: the header plus the real part. The file starts with a UTF-8 BOM. |
| 6 | PASS | The export plus 5 new rows previewed as "5 new parts, 0 updated, 0 problems". |
| 7 | PASS | One changed price previewed as "0 new parts, 1 updated". Committing it wrote no journal rows. |
| 8 | PASS | Blocking, and Import was disabled. "Row 2: there is a quantity (6) but no unit cost. Salio needs to know what the stock cost you, or your profit will look bigger than it is. Add the unit cost, or clear the quantity." |
| 9 | PASS | All three caught. Row 3, SKU, `QA9-001`: already used on row 2. Row 4, Name, (empty): no name. Row 5, SellingPrice, `abc`: not a number. |
| 10 | PASS | An unknown column "Colour" was a quiet warning; Import stayed enabled. |
| 11 | PASS | Inventory and Owner's Equity both rose by 12,713.00, matching the hand total. There were 5 separate opening-stock entries. |
| 12 | PASS | The trial balance went back exactly. The originals were **reversed, not deleted**: all 5 still exist, with 5 Reversal entries pointing at them, plus opposing stock movements. |
| 13 | PASS | After selling one imported part, undo switched off the other 2. It kept "Throttle cable Boxer, QA13-01: It has been sold since the import, so it stays. Its sales are in the books." |
| 14 | PASS | Crop and clean-up came first; the network log showed no upload until "Send and read". The original was 2,604,224 bytes; the upload was a PNG of **152,459 bytes**, 1746x2500. |
| 15 | PASS | The banner "DEMO EXTRACTION — NOT A REAL READING" was across the top. |
| 16 | PASS | QA edited a line, created it as a new part, matched another to Clutch cable, and committed. Purchase entries went from 1 to 2 (exactly one receipt), and only the 2 accepted lines got stock movements. Average cost computed by hand with StockService's method: (5×500 + 6×380 after a reset) / 10 = 478.00. The app showed KES 478.00. |
| 17 | PASS | "The invoice says 30,840 and the lines add up to 30,240", with a red "600 apart". It updates live as lines change. |
| 18 | PASS | `dotnet test`: 224 passed, 0 failed, 0 skipped. I re-ran it myself afterwards and got the same. There are build warnings about mismatched EF Core versions (10.0.4 vs 10.0.11); they are not failures. |

Extra checks:
- **Browser:** no console errors except the expected 401 before sign-in, and no sideways scroll at 1024.
- **PDF:** a PDF invoice uploads with no crop step and opens the demo review.
- **Quick-add messages:** "cost without quantity" and "quantity without cost" are both stopped in the browser with plain messages.
- **Wording:** "product" and "item" appear nowhere on the new screens for a Parts shop.

---

## Where the OCR provider plugs in

- **Interface:** `Salio.Infrastructure/Invoices/IInvoiceReader.cs`:
  `Task<InvoiceExtraction> ReadAsync(Stream image, string contentType, CancellationToken cancellationToken)`.
  The extraction holds supplier name, invoice number, invoice date, total (minor units), `IsDemo`, and lines of raw text, proposed name, quantity and unit cost.
- **Config keys:**
  - `InvoiceReader:Provider` and `InvoiceReader:ApiKey`
  - `InvoiceStorage:Path`, the folder where invoice files are kept
- **Today:**
  - With no key, the demo reader runs and every screen says "demo extraction — not a real reading".
  - With a key set, uploads are **refused with a 503** and a plain message, and nothing is kept. That happens because no real reader exists yet, and Salio never pretends to have read an invoice.

**To switch on a real provider:**
1. **Write the reader.** Create a class in `Salio.Infrastructure/Invoices/`, for example `XyzInvoiceReader : IInvoiceReader`. It calls the provider with the key and returns an extraction with `IsDemo = false`. On failure it must throw, never fall back to demo lines.
2. **Add it to the switch.** Add a case for it in the switch in `ConfiguredInvoiceReader.cs`, at the comment `// ---- A REAL PROVIDER PLUGS IN HERE. ----`.
3. **Development:** run
   `dotnet user-secrets set "InvoiceReader:Provider" "xyz" --project Salio.Api`
   `dotnet user-secrets set "InvoiceReader:ApiKey" "<key>" --project Salio.Api`
4. **Production:** set the environment variables `InvoiceReader__Provider=xyz`, `InvoiceReader__ApiKey=<key>` and **`InvoiceStorage__Path=<a folder outside the app and outside wwwroot>`**. Without that storage path, uploads in production return 503.

No key is in the repository. The agents chose no provider and signed up for nothing.

---

## Decisions I made that you had not made

| | Decision | Why |
|---|---|---|
| D1 | Products gained Category, Barcode and ReorderLevel, in the one migration. | Your import format names them; they didn't exist. |
| D2 | Added SourceType `Reversal`. A reversing entry points at the entry it reverses through SourceId. | Undo must leave a visible, linked trail, with no new column needed. |
| D3 | The opening-stock and stock-receipt code was **moved out of the two controllers** into `OpeningStockWriter` and `StockReceiptWriter`, unchanged, and the controllers now call them. | "Call the existing service" was impossible while that logic lived inside controllers. A copy would be exactly the duplicate maths you forbade. |
| D4 | Import posts opening stock **one entry per new part**. Each entry points at that part's ImportBatchChange. | So undo can reverse one part at a time by mirroring one whole entry, with no figures recalculated. |
| D5 | Reversals are dated **today**. | A period with a filed tax return is closed, and today is always open. |
| D6 | "Remove the product" on undo means **switch it off and free its SKU** (renamed `<sku>~undone-<batch>`). It is not deleted. | Its stock movements are append-only and point at it, so deleting it would break your append-only rule. |
| D7 | Importing a SKU that belongs to a switched-off part is a blocking error. | Safer than quietly switching it back on. |
| D8 | Quick-add makes the SKU itself ("Q-" + 6 characters). Quantity without cost, and cost without quantity, are each stopped with a plain line. The part with neither still saves and sells. | Your rule says stock may not be accepted at no cost, and a cost has nowhere to live without stock. The till still never refuses a sale. |
| — | **Opening-stock "no sales yet" rule narrowed for import and quick-add.** The Opening stock screen still refuses once the shop has made any sale. Import and quick-add refuse only if *those parts* have sold, which a just-created part never has. | Otherwise quick-add and import with quantities would fail in every shop already trading. The rule's purpose, not changing the cost of past sales, still holds. To undo this, pass `NoSaleInShop` at the two call sites. |
| D9 | "Parts needing a cost" = active parts with average cost 0 that have sold, or have stock. Parts that are sold out *and* have no uncosted sales are left off. | Otherwise every sold-out part would show, because StockService gives an empty shelf an average of 0. |
| — | A cashier can quick-add, but cost and quantity are hidden and ignored. | Cost and stock value are Owner-only under rule 13; selling is not. |
| D10 | Invoice files are kept on disk under `InvoiceStorage:Path`, never in wwwroot. Development uses `Salio.Api/App_Data/invoices`, which git ignores. | Keeps invoices out of the database and out of public reach. |
| D11 | **Path 2 (you importing fast movers) is not a separate tool.** You build the CSV and run the import on the client's device, signed in as or beside the owner. | Your last run's rule says the admin area may not write inside a client's shop. |
| D12 | `deploy/salio.caddy` gained an 11 MB upload limit for `/api/imports*` and `/api/supplier-invoices*`. Everything else keeps 1 MB. | A 5 MB CSV or 10 MB invoice would otherwise be refused in production. Caddy was not run to test it. |
| D13 | Import quantities must be whole numbers. Invoice and quick-add quantities allow up to 3 decimals, like stock movements. | Your import rule says whole numbers; the rest follow the stock tables. |
| D14 | The export leaves UnitCost and Quantity **empty** for existing parts. | Re-importing them would only raise "ignored" warnings, and import may never set cost on an existing part. |
| D15 | **Guard test exemption:** `StockGuardTests.A_product_stores_money_but_never_a_quantity` now lets `ReorderLevel` through, by name, with its reason written on it. | The guard forbids any number on Product except money, to stop a stored stock-on-hand figure. ReorderLevel is a threshold the owner types, not stock. The only alternative was dropping a column your brief asks for. **Please confirm or reverse.** |
| — | Invoice matches are **never pre-selected**. The owner picks a match, or "create new", on each line. | "Suggest, never decide." |
| — | A provider key set without a working reader is **refused (503)** and never falls back to the demo. | So nothing ever passes off a demo reading as real. |
| — | New parts made from an invoice get no opening stock; the receipt brings their stock in. | One receipt, as you asked, and no Owner's Equity entry for goods that were bought. |
| — | Excel formula protection: exported cells starting with `= + - @` are written with a leading `'`, which the import strips again. | Stops a part name running as a formula in Excel. |

## Told to do and could not, fully

- **Four-corner (perspective) correction** of invoice photos was not built. You get an upright crop box, 90° turns and a ±15° straighten slider. Your brief asks for crop and rotate; true deskew of a photo taken at an angle is not there.
- **No list of pending invoices.** A half-reviewed invoice can't be reopened later from a screen, because the contract had no list endpoint. Its rows are kept.
- **Not tested:** a real OCR provider (by design), a real phone camera, HEIC photos, and the Caddy upload limit under Caddy itself.

## Problems noticed and not fixed

1. **The invoice receipt date defaults to the invoice's own date.** QA's receipt journal entry was dated 07 Nov while its stock movements were dated 09 Nov. If an old invoice falls in a filed tax period, the commit is refused (JournalPoster's existing check). It also posts in the past. Decide whether the default should be today.
2. **Small text can break up** in the cleaned invoice image at the default ink setting. A real reader may struggle with it. The PNG is saved in colour (RGBA); grey or 1-bit would be smaller still.
3. **Switched-off "~undone" parts** (22 in QA's shop) still appear in the Parts list.
4. **"1 rows already the same"** on the import preview: a plural error.
5. **Average cost when a part sells before any stock existed.** That unit is absorbed, so Clutch cable showed 625.00 before its receipt. This comes from StockService, which is protected, so I'm noting it only.
6. **A page reload empties the cart**, because it is held in memory. This was already the case before this run.
7. **The test count before this run was 125, not the 121 from the last run.** The four extra tests came from other uncommitted work in the tree, not from this run.
8. **EF Core version mismatch warnings**, and the one old lint error in `App.tsx` (now around line 384), are still there.
9. **Protected files:** none needed to change, and none were edited.

## Left in the dev database

- **Test shops:**
  - "QA2 Spares" (qa2-spares@example.com): parts, 2 sales, import batches (two undone), one committed and one discarded invoice.
  - "Phase Two Test Garage": the agents' API tests (import batches, quick-adds, a committed invoice, receipts).
- **Invoice files:** these test invoices are on disk in `Salio.Api/App_Data/invoices/` (ignored by git).
- **Untouched:** "Jovis Motorcycle Spares" and your account.
- **Admin flag:** no account has platform admin (checked).
- **Processes:** the API is stopped, and the Vite dev server on 5173 is still running as it was.
