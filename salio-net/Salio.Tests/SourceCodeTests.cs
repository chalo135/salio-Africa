using System.Text.RegularExpressions;

namespace Salio.Tests
{
    // Proves the source search behind the guard tests really finds a
    // forbidden write, so a passing guard means something.
    public class SourceCodeTests
    {
        private static readonly Regex JournalTables = new(@"\bJournalLines\b|\bJournalEntries\b");
        private static readonly Regex StockTable = new(@"\bStockMovements\b");

        [Fact]
        public void A_planted_journal_write_is_found()
        {
            List<SourceCode.Write> writes = Search("_db.JournalLines.AddRange(lines);", JournalTables);

            Assert.Single(writes);
            Assert.Equal("AddRange", writes[0].Method);
        }

        [Fact]
        public void A_bulk_delete_over_several_lines_is_found()
        {
            string text = "await _db.StockMovements\n    .Where(m => m.Id == id)\n    .ExecuteDeleteAsync(ct);";

            List<SourceCode.Write> writes = Search(text, StockTable);

            Assert.Single(writes);
            Assert.Equal("ExecuteDeleteAsync", writes[0].Method);
        }

        [Fact]
        public void A_write_in_a_comment_or_a_string_is_not_counted()
        {
            string text = "// _db.JournalLines.Add(line);\n"
                + "/* _db.StockMovements.Remove(m); */\n"
                + "string s = \"_db.JournalLines.Add(line);\";\n"
                + "string r = @\"_db.StockMovements.Update(m);\";";

            Assert.Empty(Search(text, JournalTables));
            Assert.Empty(Search(text, StockTable));
        }

        [Fact]
        public void Adding_to_a_plain_list_is_not_counted()
        {
            string text = "lines.Add(new JournalLine { AccountId = id });\nmovements.Add(new StockMovement());";

            Assert.Empty(Search(text, JournalTables));
            Assert.Empty(Search(text, StockTable));
        }

        private static List<SourceCode.Write> Search(string text, Regex table)
        {
            SourceCode.SourceFile file = new(
                "Planted.cs",
                SourceCode.Blank(text, keepStrings: false),
                SourceCode.Blank(text, keepStrings: true));

            return SourceCode.WritesTo(file, table);
        }
    }
}
