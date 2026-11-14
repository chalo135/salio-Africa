using System.Text.RegularExpressions;

namespace Salio.Tests
{
    // Guard for locked rule 3: only JournalPoster.PostAsync writes to
    // JournalEntries and JournalLines, and it calls LedgerService.PostJournal
    // first. These tests read the source files; no database is used.
    public class JournalGuardTests
    {
        private const string JournalPosterFile = "Salio.Infrastructure/JournalPoster.cs";

        private static readonly Regex JournalTables = new(
            @"\bJournalLines\b|\bJournalEntries\b|\bSet\s*<\s*(Salio\.Domain\.Entities\.)?Journal(Line|Entry)\s*>");

        [Fact]
        public void Only_JournalPoster_writes_journal_entries_and_lines()
        {
            List<string> problems = new();

            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                if (file.RelativePath == JournalPosterFile)
                {
                    continue;
                }

                foreach (SourceCode.Write write in SourceCode.WritesTo(file, JournalTables))
                {
                    problems.Add(write.Where);
                }
                problems.AddRange(SourceCode.AliasesOf(file, "JournalLine", "JournalLines"));
                problems.AddRange(SourceCode.AliasesOf(file, "JournalEntry", "JournalEntries"));
                problems.AddRange(SourceCode.RawSqlWrites(
                    file,
                    new[] { "JournalLines", "JournalEntries" },
                    new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE", "MERGE" }));
            }

            problems.AddRange(SourceCode.UncheckableWrites());

            Assert.True(problems.Count == 0,
                "Locked rule 3: only JournalPoster.PostAsync may write journal rows. Found:\n"
                + string.Join("\n", problems));
        }

        [Fact]
        public void JournalPoster_checks_with_PostJournal_before_it_writes()
        {
            SourceCode.SourceFile poster = SourceCode.ProductionFile(JournalPosterFile);

            Match postAsync = Regex.Match(poster.Code, @"\bPostAsync\s*\(");
            Assert.True(postAsync.Success, "JournalPoster has no PostAsync method.");

            Assert.True(Regex.IsMatch(poster.Code, @"\bLedgerService\b"),
                "JournalPoster does not use LedgerService.");

            Match check = Regex.Match(poster.Code.Substring(postAsync.Index), @"\.\s*PostJournal\s*\(");
            Assert.True(check.Success, "JournalPoster.PostAsync never calls PostJournal.");
            int checkIndex = postAsync.Index + check.Index;

            List<SourceCode.Write> writes = SourceCode.WritesTo(poster, JournalTables);
            Assert.True(writes.Count > 0,
                "JournalPoster no longer writes journal rows, so this guard checks nothing. Update the guard.");

            foreach (SourceCode.Write write in writes)
            {
                Assert.True(SourceCode.AddMethods.Contains(write.Method),
                    "JournalPoster may only add journal rows, never change or delete them: " + write.Where);
                Assert.True(write.Index > checkIndex,
                    "JournalPoster writes before PostJournal has checked the entry: " + write.Where);
            }
        }
    }
}
