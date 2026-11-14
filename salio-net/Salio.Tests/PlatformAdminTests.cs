using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Metadata;
using Salio.Api.Security;
using Salio.Domain.Entities;

namespace Salio.Tests
{
    // The PM's rule: no endpoint, anywhere, may set, grant or change
    // AppUser.IsPlatformAdmin. It is set by hand in psql only.
    // No database is used.
    public class PlatformAdminTests
    {
        private const string Column = "IsPlatformAdmin";

        // The only files allowed to name the column, and why:
        private static readonly string[] AllowedFiles =
        {
            "Salio.Domain/Entities/AppUser.cs",                         // the property itself
            "Salio.Infrastructure/Configurations/AppUserConfiguration.cs", // tells EF never to write it
            "Salio.Api/Security/PlatformAdminFilter.cs"                  // reads it, nothing more
        };

        private const string FilterFile = "Salio.Api/Security/PlatformAdminFilter.cs";

        [Fact]
        public void No_request_can_change_IsPlatformAdmin()
        {
            List<string> problems = new();
            problems.AddRange(EfWouldWriteTheColumn());
            problems.AddRange(RequestShapesCarryingTheColumn());
            problems.AddRange(SourceTouchingTheColumn());

            Assert.True(problems.Count == 0,
                "Something could change IsPlatformAdmin. It must only ever be set by hand in psql:\n"
                + string.Join("\n", problems));
        }

        // a) EF itself: SaveChanges must leave the column out of every
        // INSERT and every UPDATE.
        private static List<string> EfWouldWriteTheColumn()
        {
            List<string> problems = new();
            using var db = SalioModel.NewContext();
            IProperty? property = db.Model.FindEntityType(typeof(AppUser))?.FindProperty(Column);

            if (property is null)
            {
                problems.Add("EF model: AppUser.IsPlatformAdmin was not found. The test is broken, or the column moved.");
                return problems;
            }

            if (property.GetBeforeSaveBehavior() != PropertySaveBehavior.Ignore)
            {
                problems.Add("EF model: IsPlatformAdmin is written on INSERT (BeforeSaveBehavior must be Ignore).");
            }

            if (property.GetAfterSaveBehavior() != PropertySaveBehavior.Ignore)
            {
                problems.Add("EF model: IsPlatformAdmin is written on UPDATE (AfterSaveBehavior must be Ignore).");
            }

            return problems;
        }

        // b) Model binding: nothing a request can send has a property of
        // that name, so no JSON body or query string can carry it.
        private static List<string> RequestShapesCarryingTheColumn()
        {
            List<string> problems = new();
            Assembly api = typeof(PlatformAdminFilter).Assembly;
            HashSet<Type> seen = new();

            List<Type> controllers = api.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();

            if (controllers.Count < 10)
            {
                problems.Add("Found only " + controllers.Count + " controllers in Salio.Api. The search is broken.");
            }

            foreach (Type controller in controllers)
            {
                foreach (MethodInfo action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    foreach (ParameterInfo parameter in action.GetParameters())
                    {
                        if (string.Equals(parameter.Name, Column, StringComparison.OrdinalIgnoreCase))
                        {
                            problems.Add(controller.Name + "." + action.Name + ": a parameter named " + parameter.Name);
                        }

                        CheckType(parameter.ParameterType, controller.Name + "." + action.Name, seen, problems);
                    }
                }
            }

            foreach (Type dto in api.GetTypes().Where(t => t.Namespace == "Salio.Api.Dtos"))
            {
                CheckType(dto, "Salio.Api.Dtos", seen, problems);
            }

            return problems;
        }

        // The type, its properties' types, and the types inside lists and
        // other generics, all the way down.
        private static void CheckType(Type type, string where, HashSet<Type> seen, List<string> problems)
        {
            if (type.IsArray)
            {
                CheckType(type.GetElementType()!, where, seen, problems);
                return;
            }

            foreach (Type argument in type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes)
            {
                CheckType(argument, where, seen, problems);
            }

            // .NET's own types (string, Guid, CancellationToken, List<>) hold
            // nothing of ours.
            if (type.IsPrimitive || type.Namespace is null || type.Namespace.StartsWith("System") || type.Namespace.StartsWith("Microsoft"))
            {
                return;
            }

            if (!seen.Add(type))
            {
                return;
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (string.Equals(property.Name, Column, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(where + ": " + type.FullName + " has a property " + property.Name
                        + ", so a request could carry it.");
                }

                CheckType(property.PropertyType, where, seen, problems);
            }
        }

        // c) The source: only the three allowed files may name the column,
        // the filter may only read it, and no string (raw SQL included)
        // anywhere may name it. Migrations are left out: they made the
        // column and are history.
        private static List<string> SourceTouchingTheColumn()
        {
            List<string> problems = new();
            Regex anyMention = new(Column, RegexOptions.IgnoreCase);

            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                if (file.RelativePath.StartsWith("Salio.Infrastructure/Migrations/"))
                {
                    continue;
                }

                // Code has string contents blanked; CodeWithStrings keeps
                // them. Comments are blanked in both.
                int inCode = anyMention.Matches(file.Code).Count;
                int inCodeAndStrings = anyMention.Matches(file.CodeWithStrings).Count;

                if (inCodeAndStrings > inCode)
                {
                    problems.Add(file.RelativePath + ": names IsPlatformAdmin inside a string (raw SQL or a JSON name?).");
                }

                if (inCode > 0 && !AllowedFiles.Contains(file.RelativePath))
                {
                    problems.Add(file.RelativePath + ": names IsPlatformAdmin. Only "
                        + string.Join(", ", AllowedFiles) + " may.");
                }
            }

            SourceCode.SourceFile entity = SourceCode.ProductionFile("Salio.Domain/Entities/AppUser.cs");
            if (!Regex.IsMatch(entity.Code, @"public\s+bool\s+IsPlatformAdmin\s*\{\s*get;\s*set;\s*\}(?!\s*=)"))
            {
                problems.Add("Salio.Domain/Entities/AppUser.cs: IsPlatformAdmin must be a plain bool with no starting value.");
            }

            SourceCode.SourceFile filter = SourceCode.ProductionFile(FilterFile);
            if (Regex.IsMatch(filter.Code, @"IsPlatformAdmin\s*=(?!=)") || filter.Code.Contains("SetProperty"))
            {
                problems.Add(FilterFile + ": may only read IsPlatformAdmin, never set it.");
            }

            if (anyMention.Matches(filter.Code).Count == 0)
            {
                problems.Add(FilterFile + ": no longer reads IsPlatformAdmin. Is the admin area unlocked?");
            }

            return problems;
        }

        // The filter's rule, without a database.

        [Fact]
        public void The_filter_lets_in_only_an_active_platform_admin()
        {
            Assert.False(PlatformAdminFilter.Allows(null));
            Assert.False(PlatformAdminFilter.Allows(new AppUser { IsActive = true, IsPlatformAdmin = false }));
            Assert.False(PlatformAdminFilter.Allows(new AppUser { IsActive = false, IsPlatformAdmin = true }));
            Assert.False(PlatformAdminFilter.Allows(new AppUser { IsActive = true, IsPlatformAdmin = true, MustChangePassword = true }));
            Assert.True(PlatformAdminFilter.Allows(new AppUser { IsActive = true, IsPlatformAdmin = true }));
        }

        [Fact]
        public async Task A_stranger_gets_a_bare_404_without_a_database()
        {
            // The context points at a host that does not exist, so this also
            // proves a stranger never causes a query.
            using var db = SalioModel.NewContext();
            var filter = new PlatformAdminFilter(db);

            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var context = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

            await filter.OnAuthorizationAsync(context);

            Assert.IsType<BareNotFoundResult>(context.Result);
        }

        [Fact]
        public async Task The_bare_404_has_no_body_and_no_content_type()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();

            await new BareNotFoundResult().ExecuteResultAsync(
                new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));

            Assert.Equal(404, httpContext.Response.StatusCode);
            Assert.Null(httpContext.Response.ContentType);
            Assert.Equal(0, httpContext.Response.Body.Length);
        }
    }
}
