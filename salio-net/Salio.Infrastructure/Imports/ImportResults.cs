using Salio.Domain.Services;

namespace Salio.Infrastructure.Imports;

// What POST /api/imports/preview returns. Nothing has been written.
public class ImportPreviewResult
{
    // Not used: the commit re-sends and re-checks the file itself.
    public string? PreviewToken { get; set; }

    public string FileName { get; set; } = string.Empty;
    public int RowsToCreate { get; set; }
    public int RowsToUpdate { get; set; }

    // Rows for parts that already exist where nothing would change.
    public int RowsUnchanged { get; set; }

    // True once the shop has recorded any sale (not a draft). Then the
    // quantities can no longer be opening stock; the screen pre-selects
    // "Stock I bought".
    public bool ShopHasSales { get; set; }

    // True when any valid NEW part carries a quantity, which is the only
    // case where an import posts stock. Then the commit needs a stockBasis.
    // (A quantity on a part that already exists is ignored, with a warning.)
    public bool HasQuantities { get; set; }

    public List<ImportIssue> Errors { get; set; } = new();
    public List<ImportIssue> Warnings { get; set; } = new();
}

// What POST /api/imports/commit returns when it worked.
public class ImportCommitResult
{
    public Guid BatchId { get; set; }
    public int RowsCreated { get; set; }
    public int RowsUpdated { get; set; }
}

// One line of the import history.
public class ImportBatchSummary
{
    public Guid BatchId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int RowsCreated { get; set; }
    public int RowsUpdated { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }
    public string? UndoneBy { get; set; }
}

public class ImportKeptProduct
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

// What POST /api/imports/{batchId}/undo returns.
public class ImportUndoResult
{
    public int UndoneCreated { get; set; }
    public int UndoneUpdated { get; set; }
    public List<ImportKeptProduct> Kept { get; set; } = new();
}

// The product's values before an import changed them, stored as JSON on
// ImportBatchChange.PreviousValuesJson so undo can put them back.
public class PreviousProductValues
{
    public string Name { get; set; } = string.Empty;
    public long SellPriceMinor { get; set; }
    public string? Category { get; set; }
    public string? Barcode { get; set; }
    public int? ReorderLevel { get; set; }
}
