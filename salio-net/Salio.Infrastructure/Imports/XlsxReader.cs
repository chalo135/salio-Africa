using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Salio.Infrastructure.Imports;

// An Excel .xlsx file is a zip of XML files. This reads the first worksheet
// with nothing but .NET's own zip and XML readers (no package): text cells
// from the shared strings list or written inline, and numbers as they are
// stored. Formulas give their last calculated value.
public static class XlsxReader
{
    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // A 5 MB zip can unpack to far more. Reading stops past this, so a
    // crafted file cannot fill the server's memory.
    private const long MaxUnpackedBytes = 200L * 1024 * 1024;

    private const string NotAnExcelFile =
        "This does not look like an Excel .xlsx file. Open it in Excel, save it as .xlsx or .csv, and try again.";

    public static List<ImportFileRow> Read(byte[] content)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);

            List<string> sharedStrings = ReadSharedStrings(zip);
            string sheetPath = FirstSheetPath(zip);

            ZipArchiveEntry? sheet = zip.GetEntry(sheetPath);

            if (sheet is null)
            {
                throw new ImportFileException(NotAnExcelFile);
            }

            return ReadSheet(sheet, sharedStrings);
        }
        catch (InvalidDataException)
        {
            throw new ImportFileException(NotAnExcelFile);
        }
        catch (XmlException)
        {
            throw new ImportFileException(NotAnExcelFile);
        }
    }

    // "B" -> 1, "AA" -> 26. Cell references like "B3" are read up to the
    // first digit.
    public static int ColumnIndex(string cellReference)
    {
        int index = 0;

        foreach (char c in cellReference.ToUpperInvariant())
        {
            if (c < 'A' || c > 'Z')
            {
                break;
            }

            index = index * 26 + (c - 'A' + 1);
        }

        return index - 1;
    }

    private static XmlReader OpenXml(ZipArchiveEntry entry)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true
        };

        return XmlReader.Create(new LimitedStream(entry.Open(), MaxUnpackedBytes), settings);
    }

    // The workbook lists its sheets in order; the first one's file is found
    // through the workbook's relationships. Falls back to the usual name.
    private static string FirstSheetPath(ZipArchive zip)
    {
        const string fallback = "xl/worksheets/sheet1.xml";

        ZipArchiveEntry? workbook = zip.GetEntry("xl/workbook.xml");
        ZipArchiveEntry? rels = zip.GetEntry("xl/_rels/workbook.xml.rels");

        if (workbook is null || rels is null)
        {
            return fallback;
        }

        string? relationshipId = null;

        using (XmlReader reader = OpenXml(workbook))
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                {
                    relationshipId = reader.GetAttribute("id", RelationshipsNamespace);
                    break;
                }
            }
        }

        if (relationshipId is null)
        {
            return fallback;
        }

        using (XmlReader reader = OpenXml(rels))
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element
                    && reader.LocalName == "Relationship"
                    && reader.GetAttribute("Id") == relationshipId)
                {
                    string? target = reader.GetAttribute("Target");

                    if (string.IsNullOrEmpty(target))
                    {
                        return fallback;
                    }

                    // "/xl/worksheets/sheet1.xml" is from the zip's root;
                    // "worksheets/sheet1.xml" is from the xl folder.
                    return target.StartsWith("/") ? target.Substring(1) : "xl/" + target;
                }
            }
        }

        return fallback;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var strings = new List<string>();
        ZipArchiveEntry? entry = zip.GetEntry("xl/sharedStrings.xml");

        if (entry is null)
        {
            return strings;
        }

        using XmlReader reader = OpenXml(entry);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
            {
                strings.Add(ReadText(reader));
            }
        }

        return strings;
    }

    // All the <t> text inside the element the reader is on (a shared string
    // or an inline string), joined: rich text is stored as several runs.
    // Phonetic hints (<rPh>) are not part of the text.
    private static string ReadText(XmlReader reader)
    {
        var text = new StringBuilder();

        if (reader.IsEmptyElement)
        {
            return "";
        }

        using XmlReader inner = reader.ReadSubtree();
        inner.Read();

        while (!inner.EOF)
        {
            if (inner.NodeType == XmlNodeType.Element && inner.LocalName == "rPh")
            {
                // Step over the whole hint, its own <t> included.
                inner.Skip();
                continue;
            }

            if (inner.NodeType == XmlNodeType.Element && inner.LocalName == "t")
            {
                // Reads the text and moves on to the next node by itself.
                text.Append(inner.ReadElementContentAsString());
                continue;
            }

            inner.Read();
        }

        return text.ToString();
    }

    private static List<ImportFileRow> ReadSheet(ZipArchiveEntry sheet, List<string> sharedStrings)
    {
        var rows = new List<ImportFileRow>();
        using XmlReader reader = OpenXml(sheet);

        int lastRowNumber = 0;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row")
            {
                continue;
            }

            int rowNumber = lastRowNumber + 1;
            string? r = reader.GetAttribute("r");

            if (r is not null && int.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out int stated))
            {
                rowNumber = stated;
            }

            lastRowNumber = rowNumber;

            if (reader.IsEmptyElement)
            {
                continue;
            }

            var cells = new List<string>();

            using (XmlReader rowReader = reader.ReadSubtree())
            {
                int nextColumn = 0;

                while (rowReader.Read())
                {
                    if (rowReader.NodeType != XmlNodeType.Element || rowReader.LocalName != "c")
                    {
                        continue;
                    }

                    string? reference = rowReader.GetAttribute("r");
                    int column = reference is null ? nextColumn : ColumnIndex(reference);

                    if (column < 0 || column > 16383)
                    {
                        column = nextColumn;
                    }

                    nextColumn = column + 1;

                    string value = ReadCell(rowReader, sharedStrings);

                    while (cells.Count <= column)
                    {
                        cells.Add("");
                    }

                    cells[column] = value;
                }
            }

            if (ImportFile.IsEmpty(cells))
            {
                continue;
            }

            rows.Add(new ImportFileRow { RowNumber = rowNumber, Cells = cells });

            if (rows.Count - 1 > ImportFile.MaxDataRows)
            {
                throw new ImportFileException(ImportFile.TooManyRowsMessage);
            }
        }

        return rows;
    }

    // The reader is on a <c> element.
    private static string ReadCell(XmlReader reader, List<string> sharedStrings)
    {
        string type = reader.GetAttribute("t") ?? "n";

        if (reader.IsEmptyElement)
        {
            return "";
        }

        string? value = null;
        string? inline = null;

        using (XmlReader cell = reader.ReadSubtree())
        {
            cell.Read();

            while (!cell.EOF)
            {
                if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "v")
                {
                    // Reads the value and moves on to the next node by itself.
                    value = cell.ReadElementContentAsString();
                    continue;
                }

                if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "is")
                {
                    inline = ReadText(cell);
                }

                cell.Read();
            }
        }

        if (type == "inlineStr")
        {
            return inline ?? "";
        }

        if (value is null)
        {
            return "";
        }

        if (type == "s")
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index >= 0 && index < sharedStrings.Count)
            {
                return sharedStrings[index];
            }

            return "";
        }

        if (type == "n")
        {
            // Excel stores 12.3 as a binary number, which can come back as
            // 12.300000000000001. Fifteen significant digits is what Excel
            // itself shows, and gives back 12.3.
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                return number.ToString("G15", CultureInfo.InvariantCulture);
            }

            return value;
        }

        // "str" (a formula's text), "b" (TRUE/FALSE as 1/0), "e" (an error
        // such as #N/A): the stored text as it is.
        return value;
    }

    // Counts what is read from a zip entry and stops past a limit.
    private sealed class LimitedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _read;

        public LimitedStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count);
            _read += n;

            if (_read > _limit)
            {
                throw new ImportFileException(
                    "This Excel file holds far more than a parts list should. Save it as .csv and try again.");
            }

            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
