using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Salio.Api;
using Salio.Api.Security;
using Salio.Infrastructure.Invoices;

namespace Salio.Tests
{
    // What must hold on a real server: the developer-only parts of the API
    // stay in Development, other websites get no access, wrong passwords are
    // counted, and nothing pretends a demo is real. No database is used.
    public class ProductionHardeningTests
    {
        private const string ProgramFile = "Salio.Api/Program.cs";

        private static IConfiguration Settings(params (string Key, string? Value)[] pairs)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value))
                .Build();
        }

        // ---- Program.cs, read as text ----

        // The text between the braces of the one
        // "if (app.Environment.IsDevelopment()) { ... }" in Program.cs.
        private static string DevelopmentOnlyBlock(string code)
        {
            MatchCollection starts = Regex.Matches(code, @"if\s*\(\s*app\.Environment\.IsDevelopment\(\)\s*\)\s*\{");
            Assert.Single(starts);

            int open = starts[0].Index + starts[0].Length - 1;
            int depth = 0;

            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                if (code[i] == '}') depth--;

                if (depth == 0)
                {
                    return code.Substring(open, i - open + 1);
                }
            }

            throw new InvalidOperationException("The Development block in Program.cs never closes.");
        }

        [Theory]
        [InlineData(@"\.MapOpenApi\s*\(")]
        [InlineData(@"\.MapScalarApiReference\s*\(")]
        [InlineData(@"await\s+SetUpDevelopmentDatabaseAsync\s*\(")]
        public void Developer_only_things_are_called_once_and_only_in_Development(string call)
        {
            string code = SourceCode.ProductionFile(ProgramFile).Code;

            Assert.Single(Regex.Matches(code, call));
            Assert.Single(Regex.Matches(DevelopmentOnlyBlock(code), call));
        }

        [Fact]
        public void The_database_is_migrated_and_seeded_from_one_place_only()
        {
            // Startup migration, the demo products and the test user: each is
            // reached only through SetUpDevelopmentDatabaseAsync, which the
            // test above pins inside the Development block.
            Regex risky = new(@"\.\s*Migrate(Async)?\s*\(|\bDemoProductSeeder\s*\.|\bSeedTestUserAsync\s*\(|\.\s*EnsureCreated(Async)?\s*\(");

            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                if (file.RelativePath == ProgramFile || file.RelativePath.Contains("/Migrations/"))
                {
                    continue;
                }

                Assert.DoesNotMatch(risky, file.Code);
            }

            string program = SourceCode.ProductionFile(ProgramFile).Code;
            int setUp = program.IndexOf("static async Task SetUpDevelopmentDatabaseAsync", StringComparison.Ordinal);
            Assert.True(setUp > 0, "SetUpDevelopmentDatabaseAsync was not found in Program.cs.");

            // Nothing risky above that method: the top of Program.cs only
            // calls it, inside the Development block.
            Assert.DoesNotMatch(risky, program.Substring(0, setUp));

            // And the method refuses to run anywhere else, whoever calls it.
            Assert.Matches(
                @"SetUpDevelopmentDatabaseAsync\s*\(\s*WebApplication\s+app\s*\)\s*\{\s*if\s*\(\s*!\s*app\.Environment\.IsDevelopment\(\)\s*\)\s*\{\s*throw\b",
                program);
        }

        [Fact]
        public void No_stack_trace_page_and_no_sensitive_logging_anywhere()
        {
            Regex forbidden = new(@"UseDeveloperExceptionPage|EnableSensitiveDataLogging|EnableDetailedErrors|AddHttpLogging|UseHttpLogging|UseW3CLogging");

            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                Assert.DoesNotMatch(forbidden, file.Code);
            }

            // The plain-message handler is in the pipeline for everyone.
            string program = SourceCode.ProductionFile(ProgramFile).Code;
            Assert.Matches(@"AddExceptionHandler\s*<\s*UnhandledErrorHandler\s*>", program);
            Assert.Matches(@"app\.UseExceptionHandler\s*\(", program);
        }

        [Fact]
        public void Forwarded_headers_come_first_in_the_pipeline()
        {
            string code = SourceCode.ProductionFile(ProgramFile).Code;

            int forwarded = code.IndexOf("app.UseForwardedHeaders(", StringComparison.Ordinal);
            Assert.True(forwarded > 0, "app.UseForwardedHeaders() was not found in Program.cs.");

            foreach (Match other in Regex.Matches(code, @"\bapp\.(Use|Map)\w+\s*\("))
            {
                Assert.True(other.Index >= forwarded,
                    "Something runs before UseForwardedHeaders in Program.cs: " + other.Value);
            }
        }

        [Fact]
        public void The_unhandled_error_message_carries_the_code_and_nothing_else()
        {
            string code = UnhandledErrorHandler.NewCode();

            Assert.Matches("^[0-9A-F]{8}$", code);
            Assert.NotEqual(code, UnhandledErrorHandler.NewCode());
            Assert.Equal(
                "Salio could not do that. Nothing was saved. Try again; if it keeps happening, tell support this code: "
                    + code + ".",
                UnhandledErrorHandler.DetailFor(code));
        }

        [Fact]
        public void A_PIN_unlock_never_shares_the_password_rate_limit()
        {
            string auth = SourceCode.ProductionFile("Salio.Api/Controllers/AuthController.cs").CodeWithStrings;

            Assert.Matches(@"\[EnableRateLimiting\(""pin-unlock""\)\]\s*\[HttpPost\(""pin-unlock""\)\]", auth);
            Assert.Matches(@"\[EnableRateLimiting\(""login""\)\]\s*\[HttpPost\(""login""\)\]", auth);
        }

        // ---- CORS ----

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(";;")]
        public void No_setting_means_no_other_origin_at_all(string? setting)
        {
            Assert.Empty(CorsOrigins.Parse(setting));
        }

        [Fact]
        public void Origins_are_split_on_semicolons_and_tidied()
        {
            string[] origins = CorsOrigins.Parse(
                " http://localhost:5173/ ;https://till.example.com;;HTTP://LOCALHOST:5173");

            Assert.Equal(new[] { "http://localhost:5173", "https://till.example.com" }, origins);
        }

        [Theory]
        [InlineData("*")]
        [InlineData("https://*.example.com")]
        [InlineData("localhost:5173")]
        [InlineData("ftp://files.example.com")]
        [InlineData("not an address")]
        public void A_wildcard_or_a_non_web_address_is_never_accepted(string bad)
        {
            Assert.Empty(CorsOrigins.Parse(bad));
            Assert.Equal(new[] { "https://ok.example.com" }, CorsOrigins.Parse(bad + ";https://ok.example.com"));
        }

        [Fact]
        public void Only_Development_names_an_origin_in_the_settings_files()
        {
            string root = Path.GetDirectoryName(typeof(ProductionHardeningTests).Assembly.Location)!;

            // The API's settings files are copied next to the test assembly.
            IConfiguration production = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(root, "appsettings.json"))
                .Build();

            IConfiguration development = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(root, "appsettings.json"))
                .AddJsonFile(Path.Combine(root, "appsettings.Development.json"))
                .Build();

            Assert.Empty(CorsOrigins.Parse(production[CorsOrigins.SettingName]));
            Assert.Equal(new[] { "http://localhost:5173" }, CorsOrigins.Parse(development[CorsOrigins.SettingName]));
        }

        // ---- Wrong passwords per account ----

        [Fact]
        public void Ten_wrong_passwords_make_the_account_wait_until_the_window_ends()
        {
            var attempts = new SignInAttempts();
            DateTimeOffset start = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

            for (int i = 0; i < SignInAttempts.MaxFailures - 1; i++)
            {
                attempts.RecordFailure("owner@shop.co.ke", start.AddSeconds(i));
            }

            Assert.Null(attempts.WaitFor("owner@shop.co.ke", start.AddSeconds(30)));

            // The tenth, typed with capitals and a space: the same account.
            attempts.RecordFailure(" Owner@Shop.co.ke ", start.AddSeconds(30));

            Assert.Equal(TimeSpan.FromMinutes(9), attempts.WaitFor("owner@shop.co.ke", start.AddMinutes(1)));

            // Somebody else is not held up.
            Assert.Null(attempts.WaitFor("cashier@shop.co.ke", start.AddMinutes(1)));

            // The window is ten minutes from the FIRST wrong password.
            Assert.Null(attempts.WaitFor("owner@shop.co.ke", start + SignInAttempts.Window));
        }

        [Fact]
        public void A_right_password_wipes_the_count()
        {
            var attempts = new SignInAttempts();
            DateTimeOffset now = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

            for (int i = 0; i < SignInAttempts.MaxFailures - 1; i++)
            {
                attempts.RecordFailure("owner@shop.co.ke", now);
            }

            attempts.RecordSuccess("owner@shop.co.ke");
            attempts.RecordFailure("owner@shop.co.ke", now);

            Assert.Null(attempts.WaitFor("owner@shop.co.ke", now));
        }

        [Fact]
        public void A_flood_of_made_up_emails_does_not_grow_without_limit_or_break_counting()
        {
            var attempts = new SignInAttempts();
            DateTimeOffset now = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

            for (int i = 0; i < 25_000; i++)
            {
                attempts.RecordFailure("nobody" + i + "@example.com", now);
            }

            for (int i = 0; i < SignInAttempts.MaxFailures; i++)
            {
                attempts.RecordFailure("owner@shop.co.ke", now);
            }

            Assert.NotNull(attempts.WaitFor("owner@shop.co.ke", now));
        }

        // ---- Is a real invoice reader switched on? ----

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("someprovider", "")]
        [InlineData("", "secret-key")]
        [InlineData("someprovider", "secret-key")]
        public void The_reader_is_not_real_until_one_has_been_built(string? provider, string? apiKey)
        {
            var reader = new ConfiguredInvoiceReader(Settings(
                ("InvoiceReader:Provider", provider),
                ("InvoiceReader:ApiKey", apiKey)));

            Assert.False(reader.IsReal);
        }

        [Fact]
        public void Reader_status_and_the_upload_ask_the_same_place_which_reader()
        {
            string reader = SourceCode.ProductionFile("Salio.Infrastructure/Invoices/ConfiguredInvoiceReader.cs").Code;

            // One decision, used twice: by IsReal and by ReadAsync.
            Assert.Equal(3, Regex.Matches(reader, @"\bFindRealReader\s*\(").Count);
            Assert.Single(Regex.Matches(reader, @"\bswitch\s*\("));

            string controller = SourceCode.ProductionFile("Salio.Api/Controllers/SupplierInvoicesController.cs").Code;
            Assert.Matches(@"Real\s*=\s*_configuredReader\.IsReal", controller);

            // One object per request answers both questions.
            string program = SourceCode.ProductionFile(ProgramFile).Code;
            Assert.Matches(@"AddScoped<IInvoiceReader>\(\s*services\s*=>\s*services\.GetRequiredService<ConfiguredInvoiceReader>\(\)\)", program);
        }

        // ---- Setup links ----

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("https://salio.example.com")]
        [InlineData("https://example.com/")]
        [InlineData("till.shop.co.ke")]
        public void A_missing_or_placeholder_PublicBaseUrl_counts_as_not_set(string? value)
        {
            IConfiguration settings = Settings(("PublicBaseUrl", value));

            Assert.False(SetupLink.IsBaseUrlSet(settings));

            // The admin page then uses the address it was opened on.
            Assert.Equal(
                "https://till.shop.co.ke/setup?code=K7MP29",
                SetupLink.Build(settings, "k7m-p29", "https://till.shop.co.ke"));
        }

        [Fact]
        public void A_real_PublicBaseUrl_wins_over_the_address_of_the_request()
        {
            IConfiguration settings = Settings(("PublicBaseUrl", "https://till.shop.co.ke/"));

            Assert.True(SetupLink.IsBaseUrlSet(settings));
            Assert.Equal(
                "https://till.shop.co.ke/setup?code=K7MP29",
                SetupLink.Build(settings, "K7MP29", "http://somewhere-else.test"));
        }
    }
}
