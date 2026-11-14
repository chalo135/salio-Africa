using System.Text.RegularExpressions;

namespace Salio.Tests
{
    // Guard for the supplier invoice reader: it may create parts and ask
    // StockReceiptWriter for ONE stock receipt, but it never composes a
    // debit, a credit, a journal entry or a stock movement of its own. These
    // tests read the source files; no database is used.
    public class InvoiceGuardTests
    {
        private static readonly Regex JournalLineWork = new(
            @"\bJournalLine\b|\bDebitMinor\b|\bCreditMinor\b");

        private static readonly Regex OwnPosting = new(
            @"\bJournalEntry\b|\bJournalEntries\b|\bJournalPoster\b|\bStockMovement\b|\bStockMovements\b|\bStockPoster\b|\bBuildJournalLines\b");

        private static List<SourceCode.SourceFile> InvoiceFiles()
        {
            List<SourceCode.SourceFile> files = SourceCode.ProductionFiles()
                .Where(f => f.RelativePath.StartsWith("Salio.Infrastructure/Invoices/")
                    || f.RelativePath == "Salio.Api/Controllers/SupplierInvoicesController.cs"
                    || f.RelativePath == "Salio.Domain/Services/InvoiceMatchRules.cs"
                    || f.RelativePath == "Salio.Domain/Services/InvoiceTotals.cs")
                .ToList();

            // Fail loudly rather than pass on an empty search.
            Assert.Contains(files, f => f.RelativePath == "Salio.Infrastructure/Invoices/SupplierInvoiceService.cs");
            Assert.Contains(files, f => f.RelativePath == "Salio.Api/Controllers/SupplierInvoicesController.cs");
            Assert.Contains(files, f => f.RelativePath == "Salio.Domain/Services/InvoiceMatchRules.cs");
            Assert.Contains(files, f => f.RelativePath == "Salio.Domain/Services/InvoiceTotals.cs");

            return files;
        }

        [Fact]
        public void Invoice_code_never_builds_journal_lines_or_sets_a_debit_or_credit()
        {
            List<string> problems = new();

            foreach (SourceCode.SourceFile file in InvoiceFiles())
            {
                foreach (Match match in JournalLineWork.Matches(file.Code))
                {
                    (int start, int end) = SourceCode.StatementAround(file.Code, match.Index);
                    problems.Add(SourceCode.Describe(file, start, end));
                }
            }

            Assert.True(problems.Count == 0,
                "Invoice code must not build journal lines or set a debit or credit. "
                + "A commit goes through StockReceiptWriter. Found:\n"
                + string.Join("\n", problems));
        }

        [Fact]
        public void Invoice_code_never_posts_entries_or_stock_itself()
        {
            List<string> problems = new();

            foreach (SourceCode.SourceFile file in InvoiceFiles())
            {
                foreach (Match match in OwnPosting.Matches(file.Code))
                {
                    (int start, int end) = SourceCode.StatementAround(file.Code, match.Index);
                    problems.Add(SourceCode.Describe(file, start, end));
                }
            }

            Assert.True(problems.Count == 0,
                "Invoice code must not touch journal entries or stock movements directly; "
                + "StockReceiptWriter does that. Found:\n"
                + string.Join("\n", problems));
        }

        [Fact]
        public void The_commit_goes_through_StockReceiptWriter_once()
        {
            SourceCode.SourceFile service = SourceCode.ProductionFile(
                "Salio.Infrastructure/Invoices/SupplierInvoiceService.cs");

            Assert.Single(Regex.Matches(service.Code, @"_receiptWriter\s*\.\s*WriteAsync\s*\("));
        }
    }
}
