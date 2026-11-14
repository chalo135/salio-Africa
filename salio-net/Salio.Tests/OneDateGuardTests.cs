using System.Text.RegularExpressions;

namespace Salio.Tests
{
    // One transaction, one date. Every writer that posts a journal entry and
    // its stock movements together must stamp the movements from the
    // entry's own date, through StockMovementDates. QA once saw an invoice
    // entry dated 07 Nov with its movements dated 09 Nov: the movements were
    // stamped "now". These tests read the source files; no database is used.
    //
    // Covered: StockReceiptWriter (Stock in, invoice commit, import of
    // bought stock), OpeningStockWriter (Opening stock screen, quick-add,
    // import of opening stock) and JournalReverser (every undo).
    public class OneDateGuardTests
    {
        private static readonly string[] Writers =
        {
            "Salio.Infrastructure/StockReceiptWriter.cs",
            "Salio.Infrastructure/OpeningStockWriter.cs",
            "Salio.Infrastructure/JournalReverser.cs"
        };

        [Theory]
        [InlineData("Salio.Infrastructure/StockReceiptWriter.cs")]
        [InlineData("Salio.Infrastructure/OpeningStockWriter.cs")]
        [InlineData("Salio.Infrastructure/JournalReverser.cs")]
        public void Every_movement_takes_the_date_of_its_journal_entry(string path)
        {
            SourceCode.SourceFile file = SourceCode.ProductionFile(path);

            // The dates the journal entries in this file carry.
            List<string> entryDates = Regex.Matches(file.Code, @"\bEntryDate\s*=\s*(?<v>\w+)\s*[,;}]")
                .Select(m => m.Groups["v"].Value)
                .ToList();
            Assert.NotEmpty(entryDates);

            // Every movement is stamped with occurredAt, nothing else.
            List<string> stamps = Regex.Matches(file.Code, @"\bOccurredAt\s*=\s*(?<v>[^,;}\r\n]+)")
                .Select(m => m.Groups["v"].Value.Trim())
                .ToList();
            Assert.NotEmpty(stamps);
            Assert.All(stamps, s => Assert.Equal("occurredAt", s));

            // And occurredAt comes from StockMovementDates, given an entry's date.
            List<string> fromDates = Regex.Matches(
                    file.Code, @"\boccurredAt\s*=\s*StockMovementDates\s*\.\s*OccurredAtFor\s*\(\s*(?<v>\w+)\s*,")
                .Select(m => m.Groups["v"].Value)
                .ToList();
            Assert.NotEmpty(fromDates);
            Assert.All(fromDates, d => Assert.Contains(d, entryDates));

            // No other way of setting occurredAt.
            int assignments = Regex.Matches(file.Code, @"\boccurredAt\s*=(?!=)").Count;
            Assert.Equal(fromDates.Count, assignments);
        }

        [Fact]
        public void The_list_of_writers_is_still_complete()
        {
            // Any file that builds a StockMovement next to a JournalEntry is a
            // writer and must be in the list above. Sales and stock counts
            // build movements in their controllers today; they are known and
            // listed here so a new one cannot slip in unseen.
            string[] known =
            {
                "Salio.Api/Controllers/SalesController.cs",
                "Salio.Api/Controllers/StockCountsController.cs",
                "Salio.Infrastructure/DemoProductSeeder.cs"
            };

            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                bool buildsMovement = Regex.IsMatch(file.Code, @"\bnew\s+StockMovement\b");
                bool buildsEntry = Regex.IsMatch(file.Code, @"\bnew\s+JournalEntry\b");

                if (buildsMovement && buildsEntry)
                {
                    Assert.True(Writers.Contains(file.RelativePath) || known.Contains(file.RelativePath),
                        file.RelativePath + " posts stock and a journal entry together. "
                        + "Stamp its movements with StockMovementDates.OccurredAtFor(the entry's date, now) "
                        + "and add it to OneDateGuardTests.");
                }
            }
        }

        [Fact]
        public void An_import_undo_dates_its_reversing_entries_and_movements_the_same_day()
        {
            SourceCode.SourceFile service = SourceCode.ProductionFile("Salio.Infrastructure/ProductImportService.cs");

            List<Match> calls = Regex.Matches(service.Code, @"_reverser\s*\.\s*Reverse(Stock)?Async\s*\(").ToList();
            Assert.NotEmpty(calls);

            foreach (Match call in calls)
            {
                (int start, int end) = SourceCode.StatementAround(service.Code, call.Index);
                string statement = service.Code.Substring(start, end - start);

                Assert.Matches(@"\btoday\b", statement);
            }
        }
    }
}
