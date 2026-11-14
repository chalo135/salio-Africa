using System.Text.RegularExpressions;

namespace Salio.Tests
{
    // Guard for the import: it may create products and ask the existing
    // writers for opening stock, but it never composes a debit or a credit
    // of its own. The only code allowed to build journal lines for an undo
    // is JournalReverser, and only by swapping an existing line's two
    // columns. These tests read the source files; no database is used.
    public class ImportGuardTests
    {
        private const string ReverserFile = "Salio.Infrastructure/JournalReverser.cs";

        private static readonly Regex JournalLineWork = new(
            @"\bJournalLine\b|\bDebitMinor\b|\bCreditMinor\b");

        private static List<SourceCode.SourceFile> ImportFiles()
        {
            List<SourceCode.SourceFile> files = SourceCode.ProductionFiles()
                .Where(f => f.RelativePath.StartsWith("Salio.Infrastructure/Imports/")
                    || f.RelativePath == "Salio.Infrastructure/ProductImportService.cs"
                    || f.RelativePath == "Salio.Api/Controllers/ImportsController.cs")
                .ToList();

            // Fail loudly rather than pass on an empty search.
            Assert.Contains(files, f => f.RelativePath == "Salio.Infrastructure/ProductImportService.cs");
            Assert.Contains(files, f => f.RelativePath == "Salio.Api/Controllers/ImportsController.cs");
            Assert.Contains(files, f => f.RelativePath.StartsWith("Salio.Infrastructure/Imports/"));

            return files;
        }

        [Fact]
        public void Import_code_never_builds_its_own_journal_lines()
        {
            List<string> problems = new();

            foreach (SourceCode.SourceFile file in ImportFiles())
            {
                foreach (Match match in JournalLineWork.Matches(file.Code))
                {
                    (int start, int end) = SourceCode.StatementAround(file.Code, match.Index);
                    problems.Add(SourceCode.Describe(file, start, end));
                }
            }

            Assert.True(problems.Count == 0,
                "Import code must not build journal lines or set a debit or credit. "
                + "Opening stock goes through OpeningStockWriter, undo through JournalReverser. Found:\n"
                + string.Join("\n", problems));
        }

        [Fact]
        public void JournalReverser_only_swaps_the_columns_of_an_existing_line()
        {
            SourceCode.SourceFile reverser = SourceCode.ProductionFile(ReverserFile);

            List<string> assignments = Regex.Matches(reverser.Code, @"\b(DebitMinor|CreditMinor)\s*=(?!=)\s*[^,;}]+")
                .Select(m => Regex.Replace(m.Value, @"\s+", " ").Trim())
                .ToList();

            Assert.Equal(
                new[] { "DebitMinor = line.CreditMinor", "CreditMinor = line.DebitMinor" },
                assignments);
        }
    }
}
