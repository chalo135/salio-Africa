namespace Salio.Domain.Services;

using System.Globalization;
using System.Text.RegularExpressions;

// One problem or note about a cell of an import file. Row is the spreadsheet
// row number (the header is row 1). Column is Salio's name for the column.
public class ImportIssue
{
    public int? Row { get; set; }
    public string? Column { get; set; }
    public string? Value { get; set; }
    public string Message { get; set; } = string.Empty;
}

// A row that passed every check, in the shape the import writes.
public class ImportRow
{
    public int RowNumber { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SellPriceMinor { get; set; }

    // Null when the column is not in the file at all: leave the part as it
    // is. A column that is there but empty clears the value.
    public bool HasCategory { get; set; }
    public string? Category { get; set; }
    public bool HasBarcode { get; set; }
    public string? Barcode { get; set; }
    public bool HasReorderLevel { get; set; }
    public int? ReorderLevel { get; set; }

    // Used only for a new part. Ignored for one that already exists.
    public long? UnitCostMinor { get; set; }
    public int? Quantity { get; set; }
}

// What the import needs to know about the shop for one row. The caller
// looks these up; the rules only decide.
public class ImportRowContext
{
    // SKU (as NormaliseSku gives it) -> the row it was first seen on.
    public Dictionary<string, int> SkusSeenEarlier { get; set; } = new();

    // An active part in this shop already has this SKU.
    public bool SkuAlreadyExists { get; set; }

    // A switched-off part has this SKU (decision D7).
    public bool SkuIsSwitchedOff { get; set; }
}

public class ImportRowResult
{
    public List<ImportIssue> Errors { get; set; } = new();
    public List<ImportIssue> Warnings { get; set; } = new();

    // Set only when there are no errors.
    public ImportRow? Row { get; set; }
}

public class ImportHeaderResult
{
    // Column position in the file -> Salio's column name.
    public Dictionary<int, string> Columns { get; set; } = new();
    public List<ImportIssue> Errors { get; set; } = new();
    public List<ImportIssue> Warnings { get; set; } = new();
}

// The product import's rules, in one place and with no database in sight,
// so they are tested without Postgres. Plain facts in, decisions out, every
// message a sentence a shopkeeper understands.
public static class ImportRowRules
{
    public const string Sku = "SKU";
    public const string Name = "Name";
    public const string SellingPrice = "SellingPrice";
    public const string Category = "Category";
    public const string UnitCost = "UnitCost";
    public const string Quantity = "Quantity";
    public const string Barcode = "Barcode";
    public const string ReorderLevel = "ReorderLevel";

    public const int MaxSkuLength = 40;
    public const int MaxNameLength = 64;
    public const int MaxCategoryLength = 64;
    public const int MaxBarcodeLength = 64;

    // In the order the export writes them.
    public static readonly string[] KnownColumns =
    {
        Sku, Name, SellingPrice, Category, UnitCost, Quantity, Barcode, ReorderLevel
    };

    public static readonly string[] RequiredColumns = { Sku, Name, SellingPrice };

    // ---- Headers ----

    // "Selling Price", "sellingprice" and "SELLING_PRICE" are one column:
    // lower case, with spaces, underscores and hyphens taken out.
    public static string NormaliseHeader(string header)
    {
        string result = "";

        foreach (char c in header.Trim().ToLowerInvariant())
        {
            if (c == ' ' || c == '_' || c == '-' || c == '\t')
            {
                continue;
            }

            result = result + c;
        }

        return result;
    }

    // Which of Salio's columns a header names, or null if none.
    public static string? MatchHeader(string header)
    {
        string normalised = NormaliseHeader(header);

        foreach (string known in KnownColumns)
        {
            if (NormaliseHeader(known) == normalised)
            {
                return known;
            }
        }

        return null;
    }

    public static ImportHeaderResult MapHeaders(List<string> headers, int headerRowNumber)
    {
        var result = new ImportHeaderResult();

        for (int i = 0; i < headers.Count; i++)
        {
            string header = headers[i].Trim();

            // A blank heading over an empty column is common in Excel files.
            if (header.Length == 0)
            {
                continue;
            }

            string? known = MatchHeader(header);

            if (known is null)
            {
                result.Warnings.Add(new ImportIssue
                {
                    Row = headerRowNumber,
                    Column = header,
                    Value = header,
                    Message = "The column \"" + header + "\" is not one Salio knows, so it will be ignored."
                });
                continue;
            }

            if (result.Columns.ContainsValue(known))
            {
                result.Errors.Add(new ImportIssue
                {
                    Row = headerRowNumber,
                    Column = known,
                    Value = header,
                    Message = "The column " + known + " is in the file twice. Keep one and delete the other."
                });
                continue;
            }

            result.Columns[i] = known;
        }

        foreach (string required in RequiredColumns)
        {
            if (!result.Columns.ContainsValue(required))
            {
                result.Errors.Add(new ImportIssue
                {
                    Row = headerRowNumber,
                    Column = required,
                    Message = "The file has no " + required + " column. The first row must name the columns: "
                        + "SKU, Name and SellingPrice are needed."
                });
            }
        }

        return result;
    }

    // ---- Cells ----

    // Trimmed, with the lower-case letters made capitals, so "abc-1 " and
    // "ABC-1" are one SKU.
    public static string NormaliseSku(string? raw)
    {
        return CleanText(raw).ToUpperInvariant();
    }

    // Trims the cell. Salio's own export puts a ' in front of a cell that
    // starts with = + - or @, so Excel shows it as text instead of running
    // it as a formula; that ' is taken off again here.
    public static string CleanText(string? raw)
    {
        string text = (raw ?? "").Trim();

        if (text.Length >= 2 && text[0] == '\''
            && (text[1] == '=' || text[1] == '+' || text[1] == '-' || text[1] == '@'))
        {
            text = text.Substring(1);
        }

        return text;
    }

    public enum NumberProblem
    {
        None,
        NotANumber,
        Negative,
        TooManyDecimals,
        TooLarge
    }

    private static readonly Regex Digits = new(@"^(\d+(\.\d*)?|\.\d+)$");

    // Digits and one full stop only. Spaces and thousands commas are taken
    // out first, so "1,250.50" and "1 250.50" both read as 1250.50.
    private static string StripThousands(string raw)
    {
        return raw.Replace(" ", "").Replace(",", "").Replace(" ", "").Trim();
    }

    // Shillings with up to two decimals, returned in cents (minor units).
    public static NumberProblem TryParseMoney(string raw, out long minor)
    {
        minor = 0;
        string text = StripThousands(raw);

        if (text.StartsWith("-") && Digits.IsMatch(text.Substring(1)))
        {
            return NumberProblem.Negative;
        }

        if (!Digits.IsMatch(text))
        {
            return NumberProblem.NotANumber;
        }

        string[] parts = text.Split('.');
        string whole = parts[0].Length == 0 ? "0" : parts[0];
        string fraction = parts.Length > 1 ? parts[1] : "";

        if (fraction.Length > 2)
        {
            return NumberProblem.TooManyDecimals;
        }

        whole = whole.TrimStart('0');

        if (whole.Length == 0)
        {
            whole = "0";
        }

        // A trillion shillings is far beyond any price, and keeps the cents
        // well inside a long.
        if (whole.Length > 12)
        {
            return NumberProblem.TooLarge;
        }

        long shillings = long.Parse(whole, CultureInfo.InvariantCulture);
        long cents = long.Parse(fraction.PadRight(2, '0'), CultureInfo.InvariantCulture);

        minor = shillings * 100 + cents;
        return NumberProblem.None;
    }

    // A count of things: no decimals ("5.0" is allowed, it is still five).
    public static NumberProblem TryParseWholeNumber(string raw, out int value)
    {
        value = 0;
        string text = StripThousands(raw);

        if (text.StartsWith("-") && Digits.IsMatch(text.Substring(1)))
        {
            return NumberProblem.Negative;
        }

        if (!Digits.IsMatch(text))
        {
            return NumberProblem.NotANumber;
        }

        string[] parts = text.Split('.');
        string whole = parts[0].Length == 0 ? "0" : parts[0].TrimStart('0');
        string fraction = parts.Length > 1 ? parts[1] : "";

        if (fraction.Trim('0').Length > 0)
        {
            return NumberProblem.TooManyDecimals;
        }

        if (whole.Length == 0)
        {
            whole = "0";
        }

        if (whole.Length > 9)
        {
            return NumberProblem.TooLarge;
        }

        value = int.Parse(whole, CultureInfo.InvariantCulture);
        return NumberProblem.None;
    }

    // ---- One row ----

    // cells: Salio's column name -> the raw text of that cell. A column that
    // is not in the file is simply not in the dictionary.
    public static ImportRowResult Validate(
        int rowNumber,
        Dictionary<string, string> cells,
        ImportRowContext context)
    {
        var result = new ImportRowResult();
        string at = "Row " + rowNumber + ": ";

        void Error(string column, string value, string message)
        {
            result.Errors.Add(new ImportIssue { Row = rowNumber, Column = column, Value = value, Message = at + message });
        }

        void Warning(string column, string value, string message)
        {
            result.Warnings.Add(new ImportIssue { Row = rowNumber, Column = column, Value = value, Message = at + message });
        }

        string Raw(string column) => cells.TryGetValue(column, out string? value) ? value ?? "" : "";

        // ---- SKU ----
        string rawSku = Raw(Sku);
        string sku = NormaliseSku(rawSku);

        if (sku.Length == 0)
        {
            Error(Sku, rawSku, "there is no SKU. Every part needs its own code so Salio can tell it apart. Add one.");
        }
        else if (sku.Length > MaxSkuLength)
        {
            Error(Sku, rawSku, "the SKU is " + sku.Length + " characters long. Keep it to "
                + MaxSkuLength + " or fewer.");
        }
        else if (context.SkusSeenEarlier.TryGetValue(sku, out int firstRow))
        {
            Error(Sku, rawSku, "SKU " + sku + " is already used on row " + firstRow
                + ". Each part goes on one row only. Delete one of them, or give this one its own SKU.");
        }
        else if (context.SkuIsSwitchedOff)
        {
            Error(Sku, rawSku, "this SKU belongs to a part that was switched off. "
                + "Switch it back on, or use a different SKU.");
        }

        bool existing = context.SkuAlreadyExists && !context.SkuIsSwitchedOff;

        // ---- Name ----
        string rawName = Raw(Name);
        string name = CleanText(rawName);

        if (name.Length == 0)
        {
            Error(Name, rawName, "there is no name. Add the name your customers and staff know the part by.");
        }
        else if (name.Length > MaxNameLength)
        {
            Error(Name, rawName, "the name is " + name.Length + " characters long. Shorten it to "
                + MaxNameLength + " or fewer.");
        }

        // ---- Selling price ----
        string rawPrice = Raw(SellingPrice);
        long price = 0;
        bool priceOk = false;

        if (CleanText(rawPrice).Length == 0)
        {
            Error(SellingPrice, rawPrice, "there is no selling price. Add what the part sells for, in shillings, like 1250 or 1250.50.");
        }
        else
        {
            NumberProblem problem = TryParseMoney(rawPrice, out price);
            priceOk = problem == NumberProblem.None;

            if (!priceOk)
            {
                Error(SellingPrice, rawPrice, MoneyMessage("selling price", rawPrice, problem));
            }
        }

        // ---- Unit cost ----
        string rawCost = Raw(UnitCost);
        long? cost = null;

        if (CleanText(rawCost).Length > 0)
        {
            NumberProblem problem = TryParseMoney(rawCost, out long parsed);

            if (problem == NumberProblem.None)
            {
                cost = parsed;
            }
            else
            {
                Error(UnitCost, rawCost, MoneyMessage("unit cost", rawCost, problem));
            }
        }

        // ---- Quantity ----
        string rawQuantity = Raw(Quantity);
        int? quantity = null;

        if (CleanText(rawQuantity).Length > 0)
        {
            NumberProblem problem = TryParseWholeNumber(rawQuantity, out int parsed);

            if (problem == NumberProblem.None)
            {
                quantity = parsed;
            }
            else if (problem == NumberProblem.Negative)
            {
                Error(Quantity, rawQuantity, "the quantity (" + CleanText(rawQuantity)
                    + ") is below zero. Put how many are on the shelf, or leave it empty.");
            }
            else if (problem == NumberProblem.TooManyDecimals)
            {
                Error(Quantity, rawQuantity, "the quantity (" + CleanText(rawQuantity)
                    + ") is not a whole number. Count whole parts, like 5.");
            }
            else if (problem == NumberProblem.TooLarge)
            {
                Error(Quantity, rawQuantity, "the quantity (" + CleanText(rawQuantity)
                    + ") is far too large. Check it.");
            }
            else
            {
                Error(Quantity, rawQuantity, "the quantity (" + CleanText(rawQuantity)
                    + ") is not a number. Use digits only, like 5.");
            }
        }

        if (existing)
        {
            // A part already in Salio gets its stock and its cost only from a
            // stock receipt, never from an import.
            if (quantity is not null && quantity.Value != 0)
            {
                Warning(Quantity, rawQuantity, "SKU " + sku + " is already in Salio, so the quantity ("
                    + CleanText(rawQuantity) + ") will be ignored. "
                    + "Please use a stock receipt to add stock to a part you already have.");
            }

            if (cost is not null)
            {
                Warning(UnitCost, rawCost, "SKU " + sku + " is already in Salio, so the unit cost ("
                    + CleanText(rawCost) + ") will be ignored. A part's cost only changes through a stock receipt.");
            }
        }
        else
        {
            // Stock with no cost would make every sale of it look like pure
            // profit. The accounting rule: quantity needs a cost above zero.
            if (quantity is not null && quantity.Value > 0 && (cost is null || cost.Value == 0))
            {
                string costPart = cost is null ? "no unit cost" : "a unit cost of zero";
                Error(UnitCost, rawCost, "there is a quantity (" + quantity.Value + ") but " + costPart
                    + ". Salio needs to know what the stock cost you, or your profit will look bigger than it is. "
                    + "Add the unit cost, or clear the quantity.");
            }

            if (priceOk && cost is not null && cost.Value > 0 && price < cost.Value)
            {
                Warning(SellingPrice, rawPrice, "the selling price (" + CleanText(rawPrice)
                    + ") is below the unit cost (" + CleanText(rawCost)
                    + "). Each sale will lose money. Fine for a clearance; otherwise check the price.");
            }
        }

        // ---- Category, barcode, reorder level ----
        string rawCategory = Raw(Category);
        string category = CleanText(rawCategory);

        if (category.Length > MaxCategoryLength)
        {
            Error(Category, rawCategory, "the category is " + category.Length + " characters long. Shorten it to "
                + MaxCategoryLength + " or fewer.");
        }

        string rawBarcode = Raw(Barcode);
        string barcode = CleanText(rawBarcode);

        if (barcode.Length > MaxBarcodeLength)
        {
            Error(Barcode, rawBarcode, "the barcode is " + barcode.Length + " characters long. Keep it to "
                + MaxBarcodeLength + " or fewer.");
        }

        string rawReorder = Raw(ReorderLevel);
        int? reorderLevel = null;

        if (CleanText(rawReorder).Length > 0)
        {
            NumberProblem problem = TryParseWholeNumber(rawReorder, out int parsed);

            if (problem == NumberProblem.None)
            {
                reorderLevel = parsed;
            }
            else
            {
                Error(ReorderLevel, rawReorder, "the reorder level (" + CleanText(rawReorder)
                    + ") must be a whole number of zero or more, like 3. Or leave it empty.");
            }
        }

        if (result.Errors.Count == 0)
        {
            result.Row = new ImportRow
            {
                RowNumber = rowNumber,
                Sku = sku,
                Name = name,
                SellPriceMinor = price,
                HasCategory = cells.ContainsKey(Category),
                Category = category.Length == 0 ? null : category,
                HasBarcode = cells.ContainsKey(Barcode),
                Barcode = barcode.Length == 0 ? null : barcode,
                HasReorderLevel = cells.ContainsKey(ReorderLevel),
                ReorderLevel = reorderLevel,
                UnitCostMinor = existing ? null : cost,
                Quantity = existing ? null : quantity
            };
        }

        return result;
    }

    private static string MoneyMessage(string what, string raw, NumberProblem problem)
    {
        string shown = CleanText(raw);

        if (problem == NumberProblem.Negative)
        {
            return "the " + what + " (" + shown + ") is below zero. It must be zero or more.";
        }

        if (problem == NumberProblem.TooManyDecimals)
        {
            return "the " + what + " (" + shown + ") has more than two decimal places. Shillings and cents only, like 1250.50.";
        }

        if (problem == NumberProblem.TooLarge)
        {
            return "the " + what + " (" + shown + ") is far too large. Check it.";
        }

        return "the " + what + " (" + shown + ") is not a number. Use digits and a full stop only, like 1250.50.";
    }
}
