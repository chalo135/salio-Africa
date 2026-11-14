using System.Text;
using System.Text.RegularExpressions;

namespace Salio.Tests
{
    // Reads the production C# files (Salio.Api, Salio.Infrastructure,
    // Salio.Domain, migrations included) so the guard tests can look for
    // forbidden calls. No database is involved: this is plain text reading.
    //
    // Comments are blanked out before anything is searched, so a comment
    // that says "JournalLines.Add" never counts. String contents are blanked
    // too for the code search; they are kept in a second copy only to read
    // raw SQL.
    internal static class SourceCode
    {
        public sealed record SourceFile(string RelativePath, string Code, string CodeWithStrings);

        public sealed record Write(SourceFile File, int Index, string Method, string Where);

        // Every EF Core method that puts a row into the database, changes one,
        // or deletes one.
        public static readonly string[] AddMethods =
        {
            "Add", "AddRange", "AddAsync", "AddRangeAsync", "Attach", "AttachRange"
        };

        public static readonly string[] ChangeMethods =
        {
            "Update", "UpdateRange", "Remove", "RemoveRange",
            "ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync"
        };

        private static readonly Regex WriteCall = new(
            @"\.\s*(?<method>" + string.Join("|", AddMethods.Concat(ChangeMethods)) + @")\s*[(<]");

        private static readonly Regex BulkCall = new(
            @"\.\s*(?<method>ExecuteUpdate|ExecuteUpdateAsync|ExecuteDelete|ExecuteDeleteAsync)\s*[(<]");

        private static readonly Regex RawSqlCall = new(
            @"(ExecuteSql\w*|SqlQuery\w*|FromSql\w*|\.\s*Sql)\s*[(<]");

        private static readonly Lazy<List<SourceFile>> Files = new(LoadFiles);

        public static List<SourceFile> ProductionFiles() => Files.Value;

        public static SourceFile ProductionFile(string relativePath)
        {
            SourceFile? file = Files.Value.FirstOrDefault(f => f.RelativePath == relativePath);
            if (file == null)
            {
                throw new InvalidOperationException(
                    "The guard test expected to find " + relativePath + " and did not. "
                    + "If it was moved or renamed, update the guard test to match.");
            }
            return file;
        }

        // Every call of an EF write method whose own statement names the
        // table (before the call) — for example _db.JournalLines.Add(...) or
        // _db.StockMovements.Where(...).ExecuteDeleteAsync().
        public static List<Write> WritesTo(SourceFile file, Regex table)
        {
            List<Write> writes = new();
            foreach (Match call in WriteCall.Matches(file.Code))
            {
                (int start, int end) = StatementAround(file.Code, call.Index);
                string before = file.Code.Substring(start, call.Index - start);
                if (table.IsMatch(before))
                {
                    writes.Add(new Write(file, call.Index, call.Groups["method"].Value, Describe(file, start, end)));
                }
            }
            return writes;
        }

        // The table's DbSet handed around under another name, which would
        // let a write escape the search above. Only SalioDbContext.cs may
        // declare the DbSet.
        public static List<string> AliasesOf(SourceFile file, string entityName, string dbSetName)
        {
            List<string> problems = new();
            if (file.RelativePath == "Salio.Infrastructure/SalioDbContext.cs")
            {
                return problems;
            }

            Regex declared = new(@"\bDbSet\s*<\s*(Salio\.Domain\.Entities\.)?" + entityName + @"\s*>");
            Regex copied = new(@"=\s*[\w.]*\b" + dbSetName + @"\s*;");
            foreach (Regex pattern in new[] { declared, copied })
            {
                foreach (Match match in pattern.Matches(file.Code))
                {
                    (int start, int end) = StatementAround(file.Code, match.Index);
                    problems.Add(Describe(file, start, end) + "   (the " + dbSetName + " set under another name)");
                }
            }
            return problems;
        }

        // Raw SQL (ExecuteSql..., FromSql..., migrationBuilder.Sql) whose text
        // names one of the tables and uses one of the verbs.
        public static List<string> RawSqlWrites(SourceFile file, string[] tables, string[] verbs)
        {
            List<string> problems = new();
            Regex tablePattern = new(string.Join("|", tables), RegexOptions.IgnoreCase);
            Regex verbPattern = new(@"\b(" + string.Join("|", verbs) + @")\b", RegexOptions.IgnoreCase);

            foreach (Match call in RawSqlCall.Matches(file.Code))
            {
                (int start, int end) = StatementAround(file.Code, call.Index);
                string sql = file.CodeWithStrings.Substring(start, end - start);
                if (tablePattern.IsMatch(sql) && verbPattern.IsMatch(sql))
                {
                    problems.Add(Describe(file, start, end) + "   (raw SQL)");
                }
            }
            return problems;
        }

        // Writes this kind of reading cannot pin to a table, so every one of
        // them is refused: a write straight on the context (_db.Add(x),
        // _db.Update(x), _db.Entry(x)), a hand-set EntityState, and a bulk
        // ExecuteUpdate/ExecuteDelete whose statement names no DbSet.
        public static List<string> UncheckableWrites()
        {
            List<string> problems = new();
            List<string> contextNames = ContextVariableNames();
            List<string> dbSetNames = DbSetNames();

            Regex onContext = new(
                @"(?<![\w.])(" + string.Join("|", contextNames.Select(Regex.Escape)) + @")\s*\.\s*(?<method>"
                + string.Join("|", AddMethods.Concat(ChangeMethods)) + @"|Entry)\s*[(<]");
            Regex stateSet = new(@"\bEntityState\s*\.\s*(Added|Modified|Deleted)\b");
            Regex anyDbSet = new(@"\b(" + string.Join("|", dbSetNames) + @")\b");

            foreach (SourceFile file in ProductionFiles())
            {
                foreach (Regex pattern in new[] { onContext, stateSet })
                {
                    foreach (Match match in pattern.Matches(file.Code))
                    {
                        (int start, int end) = StatementAround(file.Code, match.Index);
                        problems.Add(Describe(file, start, end)
                            + "   (a write the test cannot pin to a table: use the DbSet, e.g. _db.Products.Add)");
                    }
                }

                foreach (Match match in BulkCall.Matches(file.Code))
                {
                    (int start, int end) = StatementAround(file.Code, match.Index);
                    string before = file.Code.Substring(start, match.Index - start);
                    if (!anyDbSet.IsMatch(before))
                    {
                        problems.Add(Describe(file, start, end)
                            + "   (a bulk update or delete that names no DbSet in the same statement)");
                    }
                }
            }
            return problems;
        }

        public static string Describe(SourceFile file, int start, int end)
        {
            int line = 1;
            for (int i = 0; i < start; i++)
            {
                if (file.Code[i] == '\n') line++;
            }

            string text = Regex.Replace(file.CodeWithStrings.Substring(start, end - start), @"\s+", " ").Trim();
            if (text.Length > 160) text = text.Substring(0, 160) + "...";
            return file.RelativePath + " line " + line + ": " + text;
        }

        // From the end of the previous statement (or the brace before it)
        // up to the next semicolon.
        public static (int start, int end) StatementAround(string code, int index)
        {
            int start = code.LastIndexOfAny(new[] { ';', '{', '}' }, index) + 1;
            int end = code.IndexOf(';', index);
            if (end < 0) end = code.Length;
            return (start, end);
        }

        private static List<string> ContextVariableNames()
        {
            HashSet<string> names = new();
            Regex[] patterns =
            {
                new(@"\bSalioDbContext\s+(?<name>\w+)"),
                new(@"\bDbContext\s+(?<name>\w+)"),
                new(@"\bvar\s+(?<name>\w+)\s*=[^;]*<\s*SalioDbContext\s*>"),
                new(@"(?<name>\w+)\s*=\s*new\s+SalioDbContext\b")
            };
            foreach (SourceFile file in ProductionFiles())
            {
                foreach (Regex pattern in patterns)
                {
                    foreach (Match match in pattern.Matches(file.Code))
                    {
                        names.Add(match.Groups["name"].Value);
                    }
                }
            }
            if (names.Count == 0)
            {
                throw new InvalidOperationException(
                    "The guard test found no variable holding a SalioDbContext. The search is broken, not the code.");
            }
            return names.ToList();
        }

        private static List<string> DbSetNames()
        {
            SourceFile context = ProductionFile("Salio.Infrastructure/SalioDbContext.cs");
            List<string> names = Regex.Matches(context.Code, @"\bDbSet\s*<\s*\w+\s*>\s+(?<name>\w+)")
                .Select(m => m.Groups["name"].Value)
                .ToList();
            if (names.Count == 0)
            {
                throw new InvalidOperationException(
                    "The guard test found no DbSet in SalioDbContext.cs. The search is broken, not the code.");
            }
            return names;
        }

        private static List<SourceFile> LoadFiles()
        {
            string root = RepoRoot();
            List<SourceFile> files = new();
            foreach (string project in new[] { "Salio.Api", "Salio.Infrastructure", "Salio.Domain" })
            {
                foreach (string path in Directory.GetFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                    if (relative.Contains("/bin/") || relative.Contains("/obj/"))
                    {
                        continue;
                    }
                    string text = File.ReadAllText(path);
                    files.Add(new SourceFile(relative, Blank(text, keepStrings: false), Blank(text, keepStrings: true)));
                }
            }

            // Fail loudly rather than pass on an empty search.
            string[] mustExist =
            {
                "Salio.Infrastructure/JournalPoster.cs",
                "Salio.Infrastructure/StockPoster.cs",
                "Salio.Infrastructure/SalioDbContext.cs"
            };
            foreach (string required in mustExist)
            {
                if (!files.Any(f => f.RelativePath == required))
                {
                    throw new InvalidOperationException(
                        "The guard test read the source under " + root + " but did not find " + required + ".");
                }
            }
            if (files.Count < 30)
            {
                throw new InvalidOperationException(
                    "The guard test found only " + files.Count + " source files under " + root + ". Something is wrong with the search.");
            }
            return files;
        }

        // Walks up from where the test is running (Salio.Tests/bin/Debug/...)
        // until it finds the folder holding Salio.Infrastructure and Salio.Api.
        private static string RepoRoot()
        {
            DirectoryInfo? folder = new(AppContext.BaseDirectory);
            while (folder != null)
            {
                bool hasInfrastructure = File.Exists(Path.Combine(folder.FullName, "Salio.Infrastructure", "JournalPoster.cs"));
                bool hasApi = Directory.Exists(Path.Combine(folder.FullName, "Salio.Api"));
                if (hasInfrastructure && hasApi)
                {
                    return folder.FullName;
                }
                folder = folder.Parent;
            }
            throw new InvalidOperationException(
                "The guard test could not find the Salio source folders above " + AppContext.BaseDirectory
                + ". It reads the .cs files of the repo, so it must run from inside the repo.");
        }

        // Replaces comments with spaces (and string contents too, unless
        // keepStrings). Line breaks are kept so line numbers stay right.
        public static string Blank(string text, bool keepStrings)
        {
            StringBuilder result = new(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (c == '/' && next == '/')
                {
                    int stop = text.IndexOf('\n', i);
                    if (stop < 0) stop = text.Length;
                    AppendBlank(result, text, i, stop);
                    i = stop;
                }
                else if (c == '/' && next == '*')
                {
                    int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = close < 0 ? text.Length : close + 2;
                    AppendBlank(result, text, i, stop);
                    i = stop;
                }
                else if (c == '"' || c == '\'')
                {
                    int stop = c == '"' ? EndOfString(text, i) : EndOfChar(text, i);
                    if (keepStrings || stop - i < 2)
                    {
                        result.Append(text, i, stop - i);
                    }
                    else
                    {
                        result.Append(text[i]);
                        AppendBlank(result, text, i + 1, stop - 1);
                        result.Append(text[stop - 1]);
                    }
                    i = stop;
                }
                else
                {
                    result.Append(c);
                    i++;
                }
            }
            return result.ToString();
        }

        private static void AppendBlank(StringBuilder result, string text, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                result.Append(text[i] == '\n' || text[i] == '\r' ? text[i] : ' ');
            }
        }

        private static int EndOfChar(string text, int i)
        {
            int j = i + 1;
            while (j < text.Length && text[j] != '\'' && text[j] != '\n')
            {
                j += text[j] == '\\' ? 2 : 1;
            }
            return Math.Min(j + 1, text.Length);
        }

        // i is on a double quote. Handles "regular", @"verbatim", $"interpolated"
        // (holes may hold their own strings) and """raw""" strings.
        private static int EndOfString(string text, int i)
        {
            int quotes = 0;
            while (i + quotes < text.Length && text[i + quotes] == '"') quotes++;
            if (quotes >= 3)
            {
                int close = text.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
                return close < 0 ? text.Length : close + quotes;
            }

            bool verbatim = (i >= 1 && text[i - 1] == '@') || (i >= 2 && text[i - 1] == '$' && text[i - 2] == '@');
            bool interpolated = (i >= 1 && text[i - 1] == '$') || (i >= 2 && text[i - 1] == '@' && text[i - 2] == '$');

            int j = i + 1;
            while (j < text.Length)
            {
                char c = text[j];
                if (verbatim && c == '"')
                {
                    if (j + 1 < text.Length && text[j + 1] == '"') { j += 2; continue; }
                    return j + 1;
                }
                if (!verbatim && c == '\\') { j += 2; continue; }
                if (!verbatim && c == '"') return j + 1;
                if (!verbatim && c == '\n') return j;
                if (interpolated && c == '{')
                {
                    if (j + 1 < text.Length && text[j + 1] == '{') { j += 2; continue; }
                    j = EndOfHole(text, j);
                    continue;
                }
                j++;
            }
            return text.Length;
        }

        private static int EndOfHole(string text, int open)
        {
            int depth = 0;
            int j = open;
            while (j < text.Length)
            {
                char c = text[j];
                if (c == '"') { j = EndOfString(text, j); continue; }
                if (c == '\'') { j = EndOfChar(text, j); continue; }
                if (c == '{') depth++;
                if (c == '}')
                {
                    depth--;
                    if (depth == 0) return j + 1;
                }
                j++;
            }
            return text.Length;
        }
    }
}
