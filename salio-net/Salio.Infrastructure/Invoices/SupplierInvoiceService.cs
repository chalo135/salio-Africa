using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Infrastructure.Invoices;

// A supplier invoice from upload to stock receipt. Reading and checking
// write nothing to the books. Committing creates any new parts and then ONE
// stock receipt through StockReceiptWriter, which does all the stock and
// journal work. This class never builds a journal line or a stock movement.
// The caller has already checked the shop and that the user is an Owner.
public class SupplierInvoiceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SalioDbContext _db;
    private readonly StockReceiptWriter _receiptWriter;

    public SupplierInvoiceService(SalioDbContext db, StockReceiptWriter receiptWriter)
    {
        _db = db;
        _receiptWriter = receiptWriter;
    }

    // Saves what the reader returned, as Pending. The file is already on disk.
    public async Task<InvoiceView> CreateAsync(
        Guid organizationId,
        Guid userId,
        Guid invoiceId,
        string imagePath,
        InvoiceExtraction extraction,
        CancellationToken cancellationToken)
    {
        var invoice = new SupplierInvoice
        {
            Id = invoiceId,
            OrganizationId = organizationId,
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedByUserId = userId,
            ImagePath = imagePath,
            Status = SupplierInvoiceStatus.Pending,
            SupplierName = Fit(extraction.SupplierName, 200),
            InvoiceNumber = Fit(extraction.InvoiceNumber, 100),
            InvoiceDate = extraction.InvoiceDate,
            ExtractedTotalMinor = extraction.TotalMinor,
            RawExtractionJson = JsonSerializer.Serialize(extraction, JsonOptions)
        };

        _db.SupplierInvoices.Add(invoice);

        var lines = new List<SupplierInvoiceLine>();
        int lineNumber = 0;

        foreach (var read in extraction.Lines)
        {
            lineNumber++;

            // The database keeps three decimals; a reader's nonsense below
            // zero is shown as 0 for the owner to correct.
            decimal quantity = Math.Max(0, decimal.Round(read.Quantity, 3, MidpointRounding.AwayFromZero));
            long unitCost = Math.Max(0, read.UnitCostMinor);

            lines.Add(new SupplierInvoiceLine
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                SupplierInvoiceId = invoiceId,
                LineNumber = lineNumber,
                RawText = Fit(read.RawText, 1000) ?? "",
                ProposedName = Fit(read.ProposedName, 200) ?? "",
                ProposedQuantity = quantity,
                ProposedUnitCostMinor = unitCost,

                // Never pre-set from a suggestion: the owner chooses.
                MatchedProductId = null,

                // Ticked when it could go straight into a receipt; the commit
                // still refuses a ticked line with no part chosen.
                Accepted = quantity > 0 && unitCost > 0
            });
        }

        _db.SupplierInvoiceLines.AddRange(lines);
        await _db.SaveChangesAsync(cancellationToken);

        return await ViewAsync(invoice, lines, cancellationToken);
    }

    public async Task<InvoiceView?> GetAsync(Guid organizationId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _db.SupplierInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.OrganizationId == organizationId, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var lines = await LinesOfAsync(organizationId, invoiceId, tracked: false, cancellationToken);

        return await ViewAsync(invoice, lines, cancellationToken);
    }

    // The owner's corrections. Status becomes Reviewed.
    public async Task<InvoiceOutcome<InvoiceView>> UpdateAsync(
        Guid organizationId, Guid invoiceId, UpdateSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await _db.SupplierInvoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.OrganizationId == organizationId, cancellationToken);

        if (invoice is null)
        {
            return new InvoiceOutcome<InvoiceView> { NotFound = true };
        }

        string? closed = ClosedMessage(invoice.Status);

        if (closed is not null)
        {
            return new InvoiceOutcome<InvoiceView> { Conflict = closed };
        }

        string? supplierName = Tidy(request.SupplierName);
        string? invoiceNumber = Tidy(request.InvoiceNumber);

        if (supplierName is not null && supplierName.Length > 200)
        {
            return new InvoiceOutcome<InvoiceView> { Error = "Keep the supplier name to 200 characters or fewer." };
        }

        if (invoiceNumber is not null && invoiceNumber.Length > 100)
        {
            return new InvoiceOutcome<InvoiceView> { Error = "Keep the invoice number to 100 characters or fewer." };
        }

        var lines = await LinesOfAsync(organizationId, invoiceId, tracked: true, cancellationToken);

        string? error = await ApplyLineEditsAsync(organizationId, lines, request.Lines, cancellationToken);

        if (error is not null)
        {
            return new InvoiceOutcome<InvoiceView> { Error = error };
        }

        invoice.SupplierName = supplierName;
        invoice.InvoiceNumber = invoiceNumber;
        invoice.InvoiceDate = request.InvoiceDate;
        invoice.Status = SupplierInvoiceStatus.Reviewed;

        await _db.SaveChangesAsync(cancellationToken);

        return new InvoiceOutcome<InvoiceView> { Value = await ViewAsync(invoice, lines, cancellationToken) };
    }

    // Thrown away. Nothing is deleted: the file and the rows stay.
    public async Task<InvoiceOutcome<InvoiceView>> DiscardAsync(
        Guid organizationId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _db.SupplierInvoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.OrganizationId == organizationId, cancellationToken);

        if (invoice is null)
        {
            return new InvoiceOutcome<InvoiceView> { NotFound = true };
        }

        if (invoice.Status == SupplierInvoiceStatus.Committed)
        {
            return new InvoiceOutcome<InvoiceView> { Conflict = ClosedMessage(invoice.Status) };
        }

        // Discarding twice is harmless.
        invoice.Status = SupplierInvoiceStatus.Discarded;
        await _db.SaveChangesAsync(cancellationToken);

        return new InvoiceOutcome<InvoiceView>
        {
            Value = await ViewAsync(invoice, new List<SupplierInvoiceLine>(), cancellationToken)
        };
    }

    // ONE stock receipt for the ticked lines, after creating any new parts,
    // all in one transaction: every part and the receipt land, or nothing does.
    public async Task<InvoiceOutcome<CommitSupplierInvoiceResult>> CommitAsync(
        Guid organizationId, Guid invoiceId, CommitSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await _db.SupplierInvoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.OrganizationId == organizationId, cancellationToken);

        if (invoice is null)
        {
            return new InvoiceOutcome<CommitSupplierInvoiceResult> { NotFound = true };
        }

        string? closed = ClosedMessage(invoice.Status);

        if (closed is not null)
        {
            return new InvoiceOutcome<CommitSupplierInvoiceResult> { Conflict = closed };
        }

        if (request.PaidWith is null)
        {
            return Refused("Say how the supplier was paid: Cash, Mpesa or Credit.");
        }

        // The receipt is dated when the goods and the debt arrived: the
        // invoice's own date, unless the owner sent another. Not today.
        DateOnly? receiptDate = request.ReceiptDate ?? invoice.InvoiceDate;

        if (receiptDate is null)
        {
            return Refused("Enter the date the goods arrived.");
        }

        var lines = await LinesOfAsync(organizationId, invoiceId, tracked: true, cancellationToken);

        // New parts can only be asked for here: there is no column to keep
        // "create new" between a PUT and the commit.
        var newPartPrices = new Dictionary<Guid, long?>();

        if (request.Lines is not null)
        {
            string? editError = await ApplyLineEditsAsync(organizationId, lines, request.Lines, cancellationToken);

            if (editError is not null)
            {
                return Refused(editError);
            }

            foreach (var edit in request.Lines)
            {
                if (edit.CreateNew)
                {
                    newPartPrices[edit.Id] = edit.NewSellPriceMinor;
                }
            }
        }

        // ---- Every ticked line is checked before anything is written. ----

        var accepted = lines.Where(l => l.Accepted).OrderBy(l => l.LineNumber).ToList();

        if (accepted.Count == 0)
        {
            return Refused("Tick at least one line to receive.");
        }

        var activeIds = await ActiveProductIdsAsync(organizationId, cancellationToken);

        foreach (var line in accepted)
        {
            string label = Label(line);

            if (line.ProposedQuantity <= 0)
            {
                return Refused(label + ": enter a quantity above zero, or untick the line.");
            }

            if (line.ProposedUnitCostMinor <= 0)
            {
                return Refused(label + ": enter a unit cost above zero. Stock cannot be received at no cost.");
            }

            bool createNew = newPartPrices.ContainsKey(line.Id);

            if (createNew)
            {
                long? price = newPartPrices[line.Id];

                if (price is null || price.Value <= 0)
                {
                    return Refused(label + ": enter the selling price for the new part.");
                }

                if (line.ProposedName.Trim().Length == 0)
                {
                    return Refused(label + ": give the new part a name.");
                }
            }
            else if (line.MatchedProductId is null)
            {
                return Refused(label + ": choose which part this is, or add it as a new part.");
            }
            else if (!activeIds.Contains(line.MatchedProductId.Value))
            {
                return Refused(label + ": the part chosen is switched off or not in this shop. Choose another.");
            }
        }

        // ---- Write. ----

        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Claim the invoice first. A second commit arriving at the same time
        // waits on this row and then finds it no longer open, so one invoice
        // can never become two receipts.
        int claimed = await _db.SupplierInvoices
            .Where(i => i.Id == invoiceId
                && i.OrganizationId == organizationId
                && (i.Status == SupplierInvoiceStatus.Pending || i.Status == SupplierInvoiceStatus.Reviewed))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, SupplierInvoiceStatus.Committed), cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            return new InvoiceOutcome<CommitSupplierInvoiceResult>
            {
                Conflict = "This invoice has already been received into stock."
            };
        }

        // New parts: no opening stock. The receipt below brings their stock in.
        foreach (var line in accepted)
        {
            if (!newPartPrices.ContainsKey(line.Id))
            {
                continue;
            }

            string? sku = await ProductSkus.GenerateFreeAsync(_db, organizationId, cancellationToken);

            if (sku is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                return Refused("Salio could not make a free SKU for a new part. Try again.");
            }

            var product = new Product
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Sku = sku,
                Name = line.ProposedName.Trim(),
                SellPriceMinor = newPartPrices[line.Id]!.Value,
                IsActive = true
            };

            _db.Products.Add(product);

            // Saved one at a time so the next SKU check sees this one.
            await _db.SaveChangesAsync(cancellationToken);

            line.MatchedProductId = product.Id;
        }

        var receiptLines = new List<StockLineInput>();

        foreach (var line in accepted)
        {
            receiptLines.Add(new StockLineInput
            {
                ProductId = line.MatchedProductId!.Value,
                Quantity = line.ProposedQuantity,
                UnitCostMinor = line.ProposedUnitCostMinor
            });
        }

        // All the stock and journal work: the same code as the Receive stock
        // screen, joining this transaction.
        var receipt = await _receiptWriter.WriteAsync(
            organizationId,
            receiptDate.Value,
            request.PaidWith.Value,
            invoice.SupplierName,
            receiptLines,
            cancellationToken);

        if (receipt.Error is not null)
        {
            // Everything above (the claim, any new parts) is undone with it.
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            // The books refused the date because that period's return has
            // been filed. Said in words, with the way out: post it today.
            if (receipt.ClosedPeriodDate is DateOnly closedDate)
            {
                return new InvoiceOutcome<CommitSupplierInvoiceResult>
                {
                    ClosedPeriod = true,
                    Error = ClosedPeriodMessage(closedDate, receipt.ClosedPeriodTaxType)
                };
            }

            return Refused(receipt.Error);
        }

        invoice.Status = SupplierInvoiceStatus.Committed;
        invoice.CommittedStockReceiptId = receipt.ReceiptId;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new InvoiceOutcome<CommitSupplierInvoiceResult>
        {
            Value = new CommitSupplierInvoiceResult
            {
                StockReceiptId = receipt.ReceiptId,
                JournalEntryId = receipt.JournalEntryId,
                LinesReceived = receipt.LineCount
            }
        };
    }

    // ---- Helpers ----

    // Checks every edit first, then applies them all. Returns the first
    // problem in words, or null.
    private async Task<string?> ApplyLineEditsAsync(
        Guid organizationId, List<SupplierInvoiceLine> lines, List<InvoiceLineEdit> edits, CancellationToken cancellationToken)
    {
        var activeIds = await ActiveProductIdsAsync(organizationId, cancellationToken);
        var seen = new HashSet<Guid>();

        foreach (var edit in edits)
        {
            var line = lines.FirstOrDefault(l => l.Id == edit.Id);

            if (line is null)
            {
                return "A line was sent that is not on this invoice: " + edit.Id;
            }

            if (!seen.Add(edit.Id))
            {
                return "Line " + line.LineNumber + " was sent twice.";
            }

            string label = "Line " + line.LineNumber;
            string name = (edit.ProposedName ?? "").Trim();

            if (name.Length > 200)
            {
                return label + ": keep the name to 200 characters or fewer.";
            }

            if (edit.ProposedQuantity < 0 || (edit.Accepted && edit.ProposedQuantity == 0))
            {
                return label + ": enter a quantity above zero, or untick the line.";
            }

            if (decimal.Round(edit.ProposedQuantity, 3) != edit.ProposedQuantity)
            {
                return label + ": a quantity can have at most three decimal places.";
            }

            if (edit.ProposedUnitCostMinor < 0)
            {
                return label + ": a unit cost cannot be negative.";
            }

            if (edit.CreateNew && edit.MatchedProductId is not null)
            {
                return label + ": choose an existing part or a new part, not both.";
            }

            if (edit.NewSellPriceMinor is not null && edit.NewSellPriceMinor.Value < 0)
            {
                return label + ": the selling price cannot be below zero.";
            }

            // Rule 9: a product id is only trusted once it is known to be
            // this shop's, and switched on. Unknown and foreign get one answer.
            if (edit.MatchedProductId is not null && !activeIds.Contains(edit.MatchedProductId.Value))
            {
                return label + ": the part chosen is switched off or not in this shop. Choose another.";
            }
        }

        foreach (var edit in edits)
        {
            var line = lines.First(l => l.Id == edit.Id);

            line.ProposedName = (edit.ProposedName ?? "").Trim();
            line.ProposedQuantity = edit.ProposedQuantity;
            line.ProposedUnitCostMinor = edit.ProposedUnitCostMinor;
            line.MatchedProductId = edit.CreateNew ? null : edit.MatchedProductId;
            line.Accepted = edit.Accepted;
        }

        return null;
    }

    private async Task<List<SupplierInvoiceLine>> LinesOfAsync(
        Guid organizationId, Guid invoiceId, bool tracked, CancellationToken cancellationToken)
    {
        var query = _db.SupplierInvoiceLines
            .Where(l => l.OrganizationId == organizationId && l.SupplierInvoiceId == invoiceId);

        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.OrderBy(l => l.LineNumber).ToListAsync(cancellationToken);
    }

    private async Task<HashSet<Guid>> ActiveProductIdsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var ids = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    private async Task<InvoiceView> ViewAsync(
        SupplierInvoice invoice, List<SupplierInvoiceLine> lines, CancellationToken cancellationToken)
    {
        var candidates = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == invoice.OrganizationId && p.IsActive)
            .Select(p => new InvoiceMatchCandidate { ProductId = p.Id, Name = p.Name, Sku = p.Sku })
            .ToListAsync(cancellationToken);

        var totals = InvoiceTotals.Check(
            invoice.ExtractedTotalMinor,
            lines.Select(l => new InvoiceTotalsLine
            {
                Quantity = l.ProposedQuantity,
                UnitCostMinor = l.ProposedUnitCostMinor,
                Accepted = l.Accepted
            }).ToList());

        var view = new InvoiceView
        {
            Id = invoice.Id,
            Status = invoice.Status.ToString(),
            IsDemoExtraction = ReadIsDemo(invoice.RawExtractionJson),
            SupplierName = invoice.SupplierName,
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.InvoiceDate,
            ExtractedTotalMinor = invoice.ExtractedTotalMinor,
            LinesTotalMinor = totals.LinesTotalMinor,
            TotalsAgree = totals.TotalsAgree,
            DifferenceMinor = totals.DifferenceMinor
        };

        foreach (var line in lines.OrderBy(l => l.LineNumber))
        {
            var suggestions = InvoiceMatchRules.Suggest(line.ProposedName, line.RawText, candidates);

            view.Lines.Add(new InvoiceLineView
            {
                Id = line.Id,
                LineNumber = line.LineNumber,
                RawText = line.RawText,
                ProposedName = line.ProposedName,
                ProposedQuantity = line.ProposedQuantity,
                ProposedUnitCostMinor = line.ProposedUnitCostMinor,
                MatchedProductId = line.MatchedProductId,
                Accepted = line.Accepted,
                Suggestions = suggestions.Select(s => new InvoiceSuggestionView
                {
                    ProductId = s.ProductId,
                    Name = s.Name,
                    Sku = s.Sku,
                    Score = s.Score
                }).ToList()
            });
        }

        return view;
    }

    // A reading with no record of isDemo is treated as demo: never claim a
    // real reading that cannot be shown to be one.
    private static bool ReadIsDemo(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);

            if (document.RootElement.TryGetProperty("isDemo", out JsonElement isDemo)
                && isDemo.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }
        catch (JsonException)
        {
            return true;
        }

        return true;
    }

    private static string? ClosedMessage(SupplierInvoiceStatus status)
    {
        if (status == SupplierInvoiceStatus.Committed)
        {
            return "This invoice has already been received into stock.";
        }

        if (status == SupplierInvoiceStatus.Discarded)
        {
            return "This invoice was discarded.";
        }

        return null;
    }

    private static string Label(SupplierInvoiceLine line)
    {
        string name = line.ProposedName.Trim();
        return name.Length == 0 ? "Line " + line.LineNumber : "Line " + line.LineNumber + " (" + name + ")";
    }

    private static InvoiceOutcome<CommitSupplierInvoiceResult> Refused(string error) => new() { Error = error };

    // "October 2026's VAT return has been filed, so nothing can be posted
    // into it. You can post this receipt today instead."
    public static string ClosedPeriodMessage(DateOnly date, TaxType? taxType)
    {
        string month = date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        string which = taxType switch
        {
            TaxType.Vat => "VAT return",
            TaxType.TurnoverTax => "Turnover Tax return",
            _ => "tax return"
        };

        return month + "'s " + which + " has been filed, so nothing can be posted into it. "
            + "You can post this receipt today instead.";
    }

    private static string? Tidy(string? text)
    {
        string trimmed = (text ?? "").Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? Fit(string? text, int maxLength)
    {
        string? tidy = Tidy(text);

        if (tidy is null)
        {
            return null;
        }

        return tidy.Length <= maxLength ? tidy : tidy[..maxLength];
    }
}
