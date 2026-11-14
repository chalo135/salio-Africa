namespace Salio.Infrastructure.Imports;

// One non-empty row of an uploaded file. RowNumber is the row as the
// spreadsheet shows it (the first row is 1), so a message can point at it.
public class ImportFileRow
{
    public int RowNumber { get; set; }
    public List<string> Cells { get; set; } = new();
}

// A file that cannot be read at all. The message is shown as it is.
public class ImportFileException : Exception
{
    public ImportFileException(string message) : base(message)
    {
    }
}

// The limits every product file is held to, and the choice of reader.
public static class ImportFile
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxDataRows = 10_000;

    public const string TooBigMessage =
        "The file is larger than 5 MB. Split it into smaller files and import them one after another.";

    public const string TooManyRowsMessage =
        "The file has more than 10,000 rows of parts. Split it into smaller files and import them one after another.";

    // Reads a .csv or .xlsx into rows, skipping empty ones. Throws
    // ImportFileException with a sentence for anything it cannot read.
    public static List<ImportFileRow> Read(string fileName, byte[] content)
    {
        if (content.LongLength > MaxBytes)
        {
            throw new ImportFileException(TooBigMessage);
        }

        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        List<ImportFileRow> rows;

        if (extension == ".csv")
        {
            rows = CsvReader.Read(content);
        }
        else if (extension == ".xlsx")
        {
            rows = XlsxReader.Read(content);
        }
        else
        {
            throw new ImportFileException(
                "Salio reads .csv and .xlsx files. Save the sheet as one of those (in Excel: File, Save As) and try again.");
        }

        // The header row is not a part.
        if (rows.Count - 1 > MaxDataRows)
        {
            throw new ImportFileException(TooManyRowsMessage);
        }

        return rows;
    }

    public static bool IsEmpty(List<string> cells)
    {
        foreach (string cell in cells)
        {
            if (cell.Trim().Length > 0)
            {
                return false;
            }
        }

        return true;
    }
}
