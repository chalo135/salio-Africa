using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure.Imports;

namespace Salio.Infrastructure;

public class ImportCommitOutcome
{
    public ImportCommitResult? Result { get; set; }

    // Set when nothing was written.
    public string? Detail { get; set; }
    public List<ImportIssue> Errors { get; set; } = new();
}

public class ImportUndoOutcome
{
    public ImportUndoResult? Result { get; set; }
    public bool NotFound { get; set; }
    public bool AlreadyUndone { get; set; }
    public string? Detail { get; set; }
}

// The product import: export the shop's parts as a CSV, check an uploaded
// file, write it, and undo it. The caller has already checked the person is
// an Owner of the shop.
//
// It writes no journal line of its own. Opening stock goes through
// OpeningStockWriter, bought stock through StockReceiptWriter; undo goes
// through JournalReverser, which mirrors an existing entry.
// ImportGuardTests checks this.
public class ProductImportService
{
    private const string UndoSoldReason =
        "It has been sold since the import, so it stays. Its sales are in the books.";

    private const string UndoDraftReason =
        "It is in a sale on the till that has not been finished, so it stays.";

    private const string UndoMovedReason =
        "Stock has been received, counted or adjusted for it since the import, so it stays.";

    private readonly SalioDbContext _db;
    private readonly OpeningStockWriter _openingStock;
    private readonly StockReceiptWriter _receiptWriter;
    private readonly JournalReverser _reverser;

    public ProductImportService(
        SalioDbContext db,
        OpeningStockWriter openingStock,
        StockReceiptWriter receiptWriter,
        JournalReverser reverser)
    {
        _db = db;
        _openingStock = openingStock;
        _receiptWriter = receiptWriter;
        _reverser = reverser;
    }

    // ---- Export ----

    // The shop's active parts in exactly the import format. UnitCost and
    // Quantity are left empty (decision D14): an import never sets either
    // for a part that already exists.
    public async Task<(byte[] Content, string FileName)> ExportAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.IsActive)
            .OrderBy(p => p.Sku)
            .ToListAsync(cancellationToken);

        string shopName = await _db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "shop";

        var csv = new StringBuilder();
        csv.Append(string.Join(",", ImportRowRules.KnownColumns)).Append("\r\n");

        foreach (var product in products)
        {
            var cells = new List<string>
            {
                product.Sku,
                product.Name,
                Shillings(product.SellPriceMinor),
                product.Category ?? "",
                "",
                "",
                product.Barcode ?? "",
                product.ReorderLevel?.ToString(CultureInfo.InvariantCulture) ?? ""
            };

            csv.Append(string.Join(",", cells.Select(CsvCell))).Append("\r\n");
        }

        // With the byte-order mark, so Excel reads it as UTF-8.
        byte[] content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(csv.ToString()))
            .ToArray();

        string fileName = "stock-" + FileNamePart(shopName) + "-"
            + JournalReverser.TodayInNairobi().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";

        return (content, fileName);
    }

    private static string Shillings(long minor)
    {
        return (minor / 100).ToString(CultureInfo.InvariantCulture) + "."
            + (minor % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    // Quoted when it holds a comma, quote or line break. A cell starting
    // with = + - or @ gets a ' in front so Excel shows it rather than runs
    // it as a formula; the import takes it off again.
    private static string CsvCell(string value)
    {
        if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@'))
        {
            value = "'" + value;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static string FileNamePart(string shopName)
    {
        var part = new StringBuilder();

        foreach (char c in shopName.ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                part.Append(c);
            }
            else if (part.Length > 0 && part[part.Length - 1] != '-')
            {
                part.Append('-');
            }
        }

        string result = part.ToString().Trim('-');
        return result.Length == 0 ? "shop" : result;
    }

    // ---- Check (preview, and again at commit) ----

    private class PlannedRow
    {
        public ImportRow Row { get; set; } = new();
        public Product? Existing { get; set; }
        public bool Changes { get; set; }
    }

    private class CheckResult
    {
        public ImportPreviewResult Preview { get; set; } = new();
        public List<PlannedRow> Plan { get; set; } = new();
    }

    public async Task<ImportPreviewResult> PreviewAsync(
        Guid organizationId, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var check = await CheckAsync(organizationId, fileName, content, cancellationToken);

        // The same question OpeningStockWriter asks before it posts.
        check.Preview.ShopHasSales = await _openingStock.ShopHasSoldAsync(organizationId, cancellationToken);

        return check.Preview;
    }

    private async Task<CheckResult> CheckAsync(
        Guid organizationId, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var check = new CheckResult();
        check.Preview.FileName = fileName;

        List<ImportFileRow> rows;

        try
        {
            rows = ImportFile.Read(fileName, content);
        }
        catch (ImportFileException ex)
        {
            check.Preview.Errors.Add(new ImportIssue { Message = ex.Message });
            return check;
        }

        if (rows.Count == 0)
        {
            check.Preview.Errors.Add(new ImportIssue
            {
                Message = "The file is empty. Start from Export, add your parts under the headings, and try again."
            });
            return check;
        }

        ImportFileRow header = rows[0];
        ImportHeaderResult headers = ImportRowRules.MapHeaders(header.Cells, header.RowNumber);
        check.Preview.Errors.AddRange(headers.Errors);
        check.Preview.Warnings.AddRange(headers.Warnings);

        // Without the columns there is nothing sensible to say about rows.
        if (headers.Errors.Count > 0)
        {
            return check;
        }

        if (rows.Count == 1)
        {
            check.Preview.Errors.Add(new ImportIssue
            {
                Message = "The file has headings but no parts under them. Add one part per row and try again."
            });
            return check;
        }

        // Every part in the shop, active or not, by SKU as the import reads
        // it (trimmed, capitals). An active part wins if two differ only in case.
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        var bySku = new Dictionary<string, Product>();

        foreach (var product in products)
        {
            string key = ImportRowRules.NormaliseSku(product.Sku);

            if (!bySku.TryGetValue(key, out Product? already) || (!already.IsActive && product.IsActive))
            {
                bySku[key] = product;
            }
        }

        var seen = new Dictionary<string, int>();

        for (int r = 1; r < rows.Count; r++)
        {
            ImportFileRow fileRow = rows[r];
            var cells = new Dictionary<string, string>();
            bool anything = false;

            foreach (var column in headers.Columns)
            {
                string value = column.Key < fileRow.Cells.Count ? fileRow.Cells[column.Key] : "";
                cells[column.Value] = value;

                if (value.Trim().Length > 0)
                {
                    anything = true;
                }
            }

            // Something only in columns Salio ignores: an empty row to us.
            if (!anything)
            {
                continue;
            }

            string sku = ImportRowRules.NormaliseSku(cells.GetValueOrDefault(ImportRowRules.Sku));
            bySku.TryGetValue(sku, out Product? existing);

            var context = new ImportRowContext
            {
                SkusSeenEarlier = seen,
                SkuAlreadyExists = existing is not null && existing.IsActive,
                SkuIsSwitchedOff = existing is not null && !existing.IsActive
            };

            ImportRowResult result = ImportRowRules.Validate(fileRow.RowNumber, cells, context);
            check.Preview.Errors.AddRange(result.Errors);
            check.Preview.Warnings.AddRange(result.Warnings);

            if (sku.Length > 0 && !seen.ContainsKey(sku))
            {
                seen[sku] = fileRow.RowNumber;
            }

            if (result.Row is null)
            {
                continue;
            }

            if (existing is null)
            {
                check.Plan.Add(new PlannedRow { Row = result.Row, Changes = true });
                check.Preview.RowsToCreate++;

                // Only a new part's quantity is ever posted as stock.
                if (HasStock(result.Row))
                {
                    check.Preview.HasQuantities = true;
                }
            }
            else
            {
                bool changes = WouldChange(existing, result.Row);
                check.Plan.Add(new PlannedRow { Row = result.Row, Existing = existing, Changes = changes });

                if (changes)
                {
                    check.Preview.RowsToUpdate++;
                }
                else
                {
                    check.Preview.RowsUnchanged++;
                }
            }
        }

        return check;
    }

    // A row whose stock would be posted: a quantity above zero.
    private static bool HasStock(ImportRow row)
    {
        return row.Quantity is not null && row.Quantity.Value > 0;
    }

    // Only these five may change on a part that exists. Never cost, never
    // quantity.
    private static bool WouldChange(Product product, ImportRow row)
    {
        return product.Name != row.Name
            || product.SellPriceMinor != row.SellPriceMinor
            || (row.HasCategory && product.Category != row.Category)
            || (row.HasBarcode && product.Barcode != row.Barcode)
            || (row.HasReorderLevel && product.ReorderLevel != row.ReorderLevel);
    }

    // ---- Commit ----

    public async Task<ImportCommitOutcome> CommitAsync(
        Guid organizationId,
        Guid userId,
        string fileName,
        byte[] content,
        string? stockBasis,
        string? paidWith,
        CancellationToken cancellationToken)
    {
        // The file is checked again here. An earlier preview is never trusted:
        // the shop may have changed since, or the file may not be the same.
        var check = await CheckAsync(organizationId, fileName, content, cancellationToken);

        if (check.Preview.Errors.Count > 0)
        {
            return new ImportCommitOutcome
            {
                Detail = "Nothing was imported. Fix the problems in the file and try again.",
                Errors = check.Preview.Errors
            };
        }

        // What the quantities mean: stock the owner already had (opening
        // stock, Owner's Equity) or stock the shop bought (a purchase, paid
        // in cash, M-Pesa or owed). Quantity alone cannot tell, so the owner
        // says. A file without quantities needs no answer.
        ImportStockBasisDecision basis = ImportStockBasisRules.Decide(
            check.Preview.HasQuantities, stockBasis, paidWith);

        if (basis.Error is not null)
        {
            return new ImportCommitOutcome { Detail = "Nothing was imported. " + basis.Error };
        }

        // The rules never return a purchase without a payment method; this
        // only makes certain of it before anything is written.
        if (basis.Basis == ImportStockBasis.Purchase && basis.PaidWith is null)
        {
            return new ImportCommitOutcome { Detail = "Nothing was imported. Say how this stock was paid for." };
        }

        var toWrite = check.Plan.Where(p => p.Changes).ToList();

        if (toWrite.Count == 0)
        {
            return new ImportCommitOutcome
            {
                Detail = "Nothing to import: every row matches what Salio already has."
            };
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly today = JournalReverser.TodayInNairobi();

        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FileName = Shorten(Path.GetFileName(fileName), 255),
            UploadedByUserId = userId,
            StartedAt = now,
            StockBasis = basis.Basis,
            PaidWith = basis.PaidWith
        };

        // One transaction for the whole file: every product, every change
        // row and all the stock lands, or none of it does.
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.ImportBatches.Add(batch);

        // New parts carrying a quantity, with the change row that made them.
        var stocked = new List<(ImportBatchChange Change, ImportRow Row)>();
        int created = 0;
        int updated = 0;

        foreach (var planned in toWrite)
        {
            ImportRow row = planned.Row;
            var change = new ImportBatchChange
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ImportBatchId = batch.Id
            };

            if (planned.Existing is null)
            {
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Sku = row.Sku,
                    Name = row.Name,
                    SellPriceMinor = row.SellPriceMinor,
                    Category = row.Category,
                    Barcode = row.Barcode,
                    ReorderLevel = row.ReorderLevel,
                    IsActive = true
                };

                _db.Products.Add(product);

                change.ProductId = product.Id;
                change.ChangeType = ImportChangeType.Created;
                created++;

                if (HasStock(row))
                {
                    stocked.Add((change, row));
                }
            }
            else
            {
                var product = await _db.Products.FirstAsync(
                    p => p.Id == planned.Existing.Id && p.OrganizationId == organizationId, cancellationToken);

                var previous = new PreviousProductValues
                {
                    Name = product.Name,
                    SellPriceMinor = product.SellPriceMinor,
                    Category = product.Category,
                    Barcode = product.Barcode,
                    ReorderLevel = product.ReorderLevel
                };

                product.Name = row.Name;
                product.SellPriceMinor = row.SellPriceMinor;

                if (row.HasCategory)
                {
                    product.Category = row.Category;
                }

                if (row.HasBarcode)
                {
                    product.Barcode = row.Barcode;
                }

                if (row.HasReorderLevel)
                {
                    product.ReorderLevel = row.ReorderLevel;
                }

                change.ProductId = product.Id;
                change.ChangeType = ImportChangeType.Updated;
                change.PreviousValuesJson = JsonSerializer.Serialize(previous);
                updated++;
            }

            _db.ImportBatchChanges.Add(change);
        }

        batch.RowsCreated = created;
        batch.RowsUpdated = updated;

        await _db.SaveChangesAsync(cancellationToken);

        // Everything so far is saved inside the transaction. Letting go of it
        // keeps each SaveChanges below quick on a file of thousands of rows.
        _db.ChangeTracker.Clear();

        if (basis.Basis == ImportStockBasis.OpeningStock)
        {
            // Opening stock one part at a time (decision D4): one journal
            // entry per new part, its SourceId the change row's id, so undo
            // can reverse exactly that part's entry. OpeningStockWriter's own
            // rule applies unchanged: refused once the shop has made a sale.
            foreach (var (change, row) in stocked)
            {
                var lines = new List<StockLineInput>
                {
                    new()
                    {
                        ProductId = change.ProductId,
                        Quantity = row.Quantity ?? 0,
                        UnitCostMinor = row.UnitCostMinor ?? 0
                    }
                };

                var result = await _openingStock.WriteAsync(
                    organizationId, today, lines, change.Id, cancellationToken);

                if (result.Error is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _db.ChangeTracker.Clear();

                    // "This shop has already made a sale..." is about the
                    // shop, not about one row, so it is said on its own.
                    string where = result.ShopHasTraded
                        ? ""
                        : "Row " + row.RowNumber + " (" + row.Sku + "): ";

                    return new ImportCommitOutcome
                    {
                        Detail = "Nothing was imported. " + where + result.Error
                    };
                }

                _db.ChangeTracker.Clear();
            }
        }
        else if (basis.Basis == ImportStockBasis.Purchase && basis.PaidWith is PaymentMethod purchasePaidWith)
        {
            // Stock the shop bought: ONE receipt for every new part with a
            // quantity, through StockReceiptWriter, paid as the owner said.
            // Never Owner's Equity. Dated today: the file does not say when
            // the goods arrived. Its SourceId is the batch id, so undo can
            // find exactly this receipt.
            var lines = new List<StockLineInput>();

            foreach (var (change, row) in stocked)
            {
                lines.Add(new StockLineInput
                {
                    ProductId = change.ProductId,
                    Quantity = row.Quantity ?? 0,
                    UnitCostMinor = row.UnitCostMinor ?? 0
                });
            }

            var receipt = await _receiptWriter.WriteAsync(
                organizationId,
                today,
                purchasePaidWith,
                supplierName: null,
                lines,
                cancellationToken,
                receiptId: batch.Id);

            if (receipt.Error is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();

                return new ImportCommitOutcome { Detail = "Nothing was imported. " + receipt.Error };
            }

            _db.ChangeTracker.Clear();
        }

        var savedBatch = await _db.ImportBatches.FirstAsync(
            b => b.Id == batch.Id && b.OrganizationId == organizationId, cancellationToken);
        savedBatch.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ImportCommitOutcome
        {
            Result = new ImportCommitResult
            {
                BatchId = batch.Id,
                RowsCreated = created,
                RowsUpdated = updated
            }
        };
    }

    // ---- History ----

    public async Task<List<ImportBatchSummary>> ListAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var batches = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.OrganizationId == organizationId)
            .OrderByDescending(b => b.StartedAt)
            .ToListAsync(cancellationToken);

        var userIds = batches.Select(b => b.UploadedByUserId)
            .Concat(batches.Where(b => b.UndoneByUserId != null).Select(b => b.UndoneByUserId!.Value))
            .Distinct()
            .ToList();

        var names = await _db.AppUsers.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var list = new List<ImportBatchSummary>();

        foreach (var batch in batches)
        {
            string? undoneBy = null;

            if (batch.UndoneByUserId is Guid undoneById)
            {
                undoneBy = names.GetValueOrDefault(undoneById, "");
            }

            list.Add(new ImportBatchSummary
            {
                BatchId = batch.Id,
                FileName = batch.FileName,
                UploadedBy = names.GetValueOrDefault(batch.UploadedByUserId, ""),
                StartedAt = batch.StartedAt,
                CompletedAt = batch.CompletedAt,
                RowsCreated = batch.RowsCreated,
                RowsUpdated = batch.RowsUpdated,
                UndoneAt = batch.UndoneAt,
                UndoneBy = undoneBy
            });
        }

        return list;
    }

    // ---- Undo ----

    // Decisions D5 and D6. Nothing is deleted. A created part that has not
    // been touched since gets its opening stock reversed (a mirror journal
    // entry and opposing stock movements), is switched off, and gives up its
    // SKU. An updated part gets its old values back. A part that has sold,
    // or had stock moved since, is left exactly as it is and listed.
    //
    // A Purchase batch mirrors what it posted: its one receipt is reversed
    // whole, or, if any part in it has sold or moved since, nothing is
    // undone and the owner is told why.
    public async Task<ImportUndoOutcome> UndoAsync(
        Guid organizationId, Guid userId, Guid batchId, CancellationToken cancellationToken)
    {
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Locks the batch row until the end of the transaction, so two undo
        // presses at once cannot both get past the "already undone" check.
        var locked = await _db.ImportBatches
            .FromSqlInterpolated(
                $"SELECT * FROM \"ImportBatches\" WHERE \"Id\" = {batchId} AND \"OrganizationId\" = {organizationId} FOR UPDATE")
            .ToListAsync(cancellationToken);

        if (locked.Count == 0)
        {
            return new ImportUndoOutcome { NotFound = true };
        }

        ImportBatch batch = locked[0];

        if (batch.UndoneAt is not null)
        {
            return new ImportUndoOutcome { AlreadyUndone = true };
        }

        var changes = await _db.ImportBatchChanges.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.ImportBatchId == batchId)
            .ToListAsync(cancellationToken);

        var productIds = changes.Select(c => c.ProductId).Distinct().ToList();

        var products = await _db.Products
            .Where(p => p.OrganizationId == organizationId && productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var saleStatuses = await _db.SaleLines.AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && productIds.Contains(l.ProductId))
            .Join(_db.Sales, l => l.SaleId, s => s.Id, (l, s) => new { l.ProductId, s.Status })
            .ToListAsync(cancellationToken);

        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && productIds.Contains(m.ProductId))
            .ToListAsync(cancellationToken);

        var result = new ImportUndoResult();

        // One date for every reversing entry and movement of this undo.
        DateOnly today = JournalReverser.TodayInNairobi();

        // A Purchase batch posted ONE receipt for all its new parts (its
        // SourceId is the batch id). The books can only mirror that entry
        // whole: reversing one part out of it would mean working out new
        // figures, which this code never does. So the receipt is reversed
        // only if NONE of its parts has sold or had stock moved since;
        // otherwise the whole undo is refused, in words.
        bool isPurchase = batch.StockBasis == ImportStockBasis.Purchase;
        string receiptNote = StockReceiptWriter.NoteFor(batchId, null);
        var receiptMovements = movements
            .Where(m => m.Type == StockMovementType.Receipt && m.Note == receiptNote)
            .ToList();
        var blockedByReceipt = new List<string>();

        try
        {
            foreach (var change in changes)
            {
                Product? product = products.FirstOrDefault(p => p.Id == change.ProductId);

                if (product is null)
                {
                    continue;
                }

                if (change.ChangeType == ImportChangeType.Updated)
                {
                    RestorePrevious(product, change.PreviousValuesJson);
                    result.UndoneUpdated++;
                    continue;
                }

                string? keptReason = null;

                if (saleStatuses.Any(s => s.ProductId == product.Id && s.Status != SaleStatus.Draft))
                {
                    keptReason = UndoSoldReason;
                }
                else if (saleStatuses.Any(s => s.ProductId == product.Id))
                {
                    keptReason = UndoDraftReason;
                }

                // The import's own stock is the movement it wrote: for opening
                // stock, the one with this change's id in its note
                // (OpeningStockWriter); for a purchase, the one with the
                // batch's receipt note (StockReceiptWriter). Any other
                // movement means stock was received, counted or adjusted since.
                string openingNote = "Opening stock " + change.Id.ToString()[..8];
                var productMovements = movements.Where(m => m.ProductId == product.Id).ToList();
                var openingMovements = productMovements
                    .Where(m => m.Type == StockMovementType.Adjustment && m.Note == openingNote)
                    .ToList();
                var ownMovements = isPurchase
                    ? receiptMovements.Where(m => m.ProductId == product.Id).ToList()
                    : openingMovements;

                if (keptReason is null && productMovements.Count != ownMovements.Count)
                {
                    keptReason = UndoMovedReason;
                }

                // A part inside the one receipt cannot be kept on its own.
                if (keptReason is not null && isPurchase && ownMovements.Count > 0)
                {
                    blockedByReceipt.Add(product.Name + " (" + product.Sku + ")");
                    continue;
                }

                if (keptReason is not null)
                {
                    result.Kept.Add(new ImportKeptProduct
                    {
                        ProductId = product.Id,
                        Sku = product.Sku,
                        Name = product.Name,
                        Reason = keptReason
                    });
                    continue;
                }

                // Opening stock is reversed part by part here. A purchase is
                // reversed once, whole, after this loop.
                if (!isPurchase)
                {
                    var openingEntryId = await _db.JournalEntries.AsNoTracking()
                        .Where(e => e.OrganizationId == organizationId
                            && e.SourceType == SourceType.StockAdjustment
                            && e.SourceId == change.Id)
                        .Select(e => (Guid?)e.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (openingEntryId is not null)
                    {
                        await _reverser.ReverseAsync(
                            organizationId, openingEntryId.Value, "import undone", today, cancellationToken);
                    }

                    if (openingMovements.Count > 0)
                    {
                        await _reverser.ReverseStockAsync(
                            organizationId, openingMovements, "Reversal of import", today, cancellationToken);
                    }
                }

                // It cannot be deleted: its stock movements point at it. It
                // leaves the shelf and frees its SKU for a fresh import (D6).
                product.IsActive = false;
                product.Sku = UndoneSku(product.Sku, batchId);
                result.UndoneCreated++;
            }

            if (blockedByReceipt.Count > 0)
            {
                // Nothing has been saved yet (a purchase posts nothing in the
                // loop above), and the rollback makes certain of it.
                await transaction.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();

                return new ImportUndoOutcome
                {
                    Detail = "Nothing was undone. The stock in this import was booked as one purchase, "
                        + "and a purchase can only be reversed whole. Since the import, these parts have been "
                        + "sold or had stock received, counted or adjusted: "
                        + string.Join(", ", blockedByReceipt) + ". "
                        + "Their sales are in the books. To correct the shelf, use Stock in or a stock count."
                };
            }

            if (isPurchase)
            {
                // The one receipt: its journal entry mirrored, and every one
                // of its movements opposed, all dated today.
                var receiptEntryId = await _db.JournalEntries.AsNoTracking()
                    .Where(e => e.OrganizationId == organizationId
                        && e.SourceType == SourceType.Purchase
                        && e.SourceId == batchId)
                    .Select(e => (Guid?)e.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (receiptEntryId is not null)
                {
                    await _reverser.ReverseAsync(
                        organizationId, receiptEntryId.Value, "import undone", today, cancellationToken);
                }

                if (receiptMovements.Count > 0)
                {
                    await _reverser.ReverseStockAsync(
                        organizationId, receiptMovements, "Reversal of import", today, cancellationToken);
                }
            }

            var tracked = await _db.ImportBatches.FirstAsync(
                b => b.Id == batchId && b.OrganizationId == organizationId, cancellationToken);
            tracked.UndoneAt = DateTimeOffset.UtcNow;
            tracked.UndoneByUserId = userId;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            return new ImportUndoOutcome { Detail = "Nothing was undone. " + ex.Message };
        }

        return new ImportUndoOutcome { Result = result };
    }

    private static void RestorePrevious(Product product, string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        var previous = JsonSerializer.Deserialize<PreviousProductValues>(json);

        if (previous is null)
        {
            return;
        }

        product.Name = previous.Name;
        product.SellPriceMinor = previous.SellPriceMinor;
        product.Category = previous.Category;
        product.Barcode = previous.Barcode;
        product.ReorderLevel = previous.ReorderLevel;
    }

    // "<sku>~undone-<first 6 of the batch id>", cut to fit Product.Sku's 50.
    public static string UndoneSku(string sku, Guid batchId)
    {
        string suffix = "~undone-" + batchId.ToString("N")[..6];
        return Shorten(sku, 50 - suffix.Length) + suffix;
    }

    private static string Shorten(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max);
    }
}
