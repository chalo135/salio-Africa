using System.IO.Compression;
using System.Text;
using Salio.Infrastructure.Imports;

namespace Salio.Tests
{
    public class ImportFileReaderTests
    {
        // ---- CSV ----

        [Fact]
        public void Csv_reads_quoted_commas_quotes_and_line_breaks()
        {
            string text = "SKU,Name,SellingPrice\r\n"
                + "A1,\"Pad, front\",\"1,250.00\"\r\n"
                + "A2,\"The \"\"good\"\" one\",10\n"
                + "A3,\"Two\nlines\",5";

            var rows = CsvReader.Read(Encoding.UTF8.GetBytes(text));

            Assert.Equal(4, rows.Count);
            Assert.Equal(new[] { "A1", "Pad, front", "1,250.00" }, rows[1].Cells);
            Assert.Equal("The \"good\" one", rows[2].Cells[1]);
            Assert.Equal("Two\nlines", rows[3].Cells[1]);
            Assert.Equal(4, rows[3].RowNumber);
        }

        [Fact]
        public void Csv_skips_empty_rows_but_keeps_the_spreadsheet_row_numbers()
        {
            string text = "SKU,Name\n\n,,\nA1,Pad\n";

            var rows = CsvReader.Read(Encoding.UTF8.GetBytes(text));

            Assert.Equal(2, rows.Count);
            Assert.Equal(1, rows[0].RowNumber);
            Assert.Equal(4, rows[1].RowNumber);
        }

        [Fact]
        public void Csv_reads_utf8_with_and_without_the_byte_order_mark()
        {
            byte[] plain = Encoding.UTF8.GetBytes("SKU,Name\nA1,Bremsbelag ü\n");
            byte[] withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(plain).ToArray();

            var a = CsvReader.Read(plain);
            var b = CsvReader.Read(withBom);

            Assert.Equal("SKU", b[0].Cells[0]);
            Assert.Equal(a[1].Cells, b[1].Cells);
            Assert.Equal("Bremsbelag ü", b[1].Cells[1]);
        }

        [Fact]
        public void Csv_handles_a_bare_carriage_return_as_a_line_end()
        {
            var rows = CsvReader.Read(Encoding.UTF8.GetBytes("SKU,Name\rA1,Pad\r"));

            Assert.Equal(2, rows.Count);
            Assert.Equal("Pad", rows[1].Cells[1]);
        }

        // ---- Limits and file types ----

        [Fact]
        public void A_file_over_5_MB_is_refused_with_one_clear_message()
        {
            byte[] big = new byte[ImportFile.MaxBytes + 1];

            var ex = Assert.Throws<ImportFileException>(() => ImportFile.Read("big.csv", big));

            Assert.Equal(ImportFile.TooBigMessage, ex.Message);
        }

        [Fact]
        public void More_than_10000_rows_is_refused_with_one_clear_message()
        {
            var text = new StringBuilder("SKU,Name,SellingPrice\n");

            for (int i = 0; i <= ImportFile.MaxDataRows; i++)
            {
                text.Append("S").Append(i).Append(",N,1\n");
            }

            var ex = Assert.Throws<ImportFileException>(
                () => ImportFile.Read("many.csv", Encoding.UTF8.GetBytes(text.ToString())));

            Assert.Equal(ImportFile.TooManyRowsMessage, ex.Message);
        }

        [Fact]
        public void Exactly_10000_rows_is_allowed()
        {
            var text = new StringBuilder("SKU,Name,SellingPrice\n");

            for (int i = 0; i < ImportFile.MaxDataRows; i++)
            {
                text.Append("S").Append(i).Append(",N,1\n");
            }

            var rows = ImportFile.Read("many.csv", Encoding.UTF8.GetBytes(text.ToString()));

            Assert.Equal(ImportFile.MaxDataRows + 1, rows.Count);
        }

        [Fact]
        public void Another_file_type_is_refused()
        {
            Assert.Throws<ImportFileException>(() => ImportFile.Read("parts.xls", new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public void A_broken_xlsx_is_refused_in_words()
        {
            var ex = Assert.Throws<ImportFileException>(
                () => ImportFile.Read("parts.xlsx", Encoding.UTF8.GetBytes("not a zip")));

            Assert.Contains(".xlsx", ex.Message);
        }

        // ---- XLSX ----

        [Theory]
        [InlineData("A1", 0)]
        [InlineData("B7", 1)]
        [InlineData("Z3", 25)]
        [InlineData("AA10", 26)]
        [InlineData("AB1", 27)]
        public void Column_letters_become_positions(string reference, int index)
        {
            Assert.Equal(index, XlsxReader.ColumnIndex(reference));
        }

        [Fact]
        public void Xlsx_reads_shared_strings_inline_strings_and_numbers_from_the_first_sheet()
        {
            byte[] file = BuildXlsx(
                sharedStrings: "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                    + "<si><t>SKU</t></si>"
                    + "<si><t>Name</t></si>"
                    + "<si><t>Selling Price</t></si>"
                    + "<si><r><t>Brake </t></r><r><t>pad</t></r></si>"
                    + "</sst>",
                sheet: "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
                    + "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"s\"><v>2</v></c></row>"
                    + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>brk-01</t></is></c><c r=\"B2\" t=\"s\"><v>3</v></c><c r=\"C2\"><v>12.300000000000001</v></c></row>"
                    + "<row r=\"3\"></row>"
                    + "<row r=\"5\"><c r=\"A5\" t=\"inlineStr\"><is><t>X9</t></is></c><c r=\"C5\"><v>450</v></c></row>"
                    + "</sheetData></worksheet>");

            var rows = ImportFile.Read("parts.xlsx", file);

            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { "SKU", "Name", "Selling Price" }, rows[0].Cells);
            Assert.Equal(new[] { "brk-01", "Brake pad", "12.3" }, rows[1].Cells);

            // The empty row 3 (and the missing row 4) are skipped; row 5 keeps its number.
            Assert.Equal(5, rows[2].RowNumber);
            Assert.Equal(new[] { "X9", "", "450" }, rows[2].Cells);
        }

        [Fact]
        public void Xlsx_without_shared_strings_still_reads()
        {
            byte[] file = BuildXlsx(
                sharedStrings: null,
                sheet: "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
                    + "<row r=\"1\"><c r=\"B1\" t=\"inlineStr\"><is><t>Name</t></is></c></row>"
                    + "</sheetData></worksheet>");

            var rows = XlsxReader.Read(file);

            Assert.Equal(new[] { "", "Name" }, Assert.Single(rows).Cells);
        }

        // A minimal .xlsx: the workbook, its relationships, one sheet and,
        // if given, the shared strings. The sheet is deliberately not called
        // sheet1.xml, so the test proves the workbook's link is followed.
        private static byte[] BuildXlsx(string? sharedStrings, string sheet)
        {
            using var buffer = new MemoryStream();

            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(zip, "xl/workbook.xml",
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                    + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                    + "<sheets><sheet name=\"Parts\" sheetId=\"1\" r:id=\"rId7\"/></sheets></workbook>");

                Add(zip, "xl/_rels/workbook.xml.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId7\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/parts.xml\"/>"
                    + "</Relationships>");

                Add(zip, "xl/worksheets/parts.xml", sheet);

                if (sharedStrings is not null)
                {
                    Add(zip, "xl/sharedStrings.xml", sharedStrings);
                }
            }

            return buffer.ToArray();
        }

        private static void Add(ZipArchive zip, string path, string xml)
        {
            ZipArchiveEntry entry = zip.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
    }
}
