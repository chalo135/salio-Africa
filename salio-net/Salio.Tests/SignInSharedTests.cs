using System.Text.RegularExpressions;

namespace Salio.Tests
{
    // A password login and finishing shop setup must sign a person in and
    // trust their phone in exactly the same way. That code lives once, in
    // SignInService. These tests read the source (no database) and fail if
    // either controller grows its own copy.
    public class SignInSharedTests
    {
        private const string Service = "Salio.Api/Security/SignInService.cs";
        private const string Auth = "Salio.Api/Controllers/AuthController.cs";
        private const string Setup = "Salio.Api/Controllers/SetupController.cs";

        [Fact]
        public void Login_and_setup_both_call_the_shared_sign_in()
        {
            Regex call = new(@"\.\s*SignInAndTrustDeviceAsync\s*\(");

            Assert.Matches(call, SourceCode.ProductionFile(Auth).Code);
            Assert.Matches(call, SourceCode.ProductionFile(Setup).Code);
        }

        [Fact]
        public void Only_the_shared_service_makes_a_trusted_device()
        {
            Regex table = new(@"\bTrustedDevices\b");

            List<string> adds = SourceCode.ProductionFiles()
                .SelectMany(file => SourceCode.WritesTo(file, table))
                .Where(write => SourceCode.AddMethods.Contains(write.Method))
                .Select(write => write.Where)
                .ToList();

            Assert.NotEmpty(adds);
            Assert.All(adds, where => Assert.StartsWith(Service + " ", where));
        }

        [Fact]
        public void Only_the_shared_service_writes_the_cookies()
        {
            // HttpContext.SignInAsync writes the session cookie, and
            // Cookies.Append writes salio_device.
            Regex sessionCookie = new(@"\.\s*SignInAsync\s*\(\s*CookieAuthenticationDefaults");
            Regex deviceCookie = new(@"\bCookies\s*\.\s*Append\s*\(");

            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                if (file.RelativePath == Service)
                {
                    Assert.Matches(sessionCookie, file.Code);
                    Assert.Matches(deviceCookie, file.Code);
                    continue;
                }

                Assert.DoesNotMatch(sessionCookie, file.Code);
                Assert.DoesNotMatch(deviceCookie, file.Code);
            }
        }
    }
}
