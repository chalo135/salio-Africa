using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Salio.Domain.Entities;
using Salio.Infrastructure;

namespace Salio.Tests
{
    // Guard for locked rule 2: every table has an OrganizationId (Guid).
    // Five exemptions, each for the reason written on the entity itself
    // (the first four decided by the owner on 2026-11-07):
    //   AppUser       - a person, who may work in several shops (rule 10)
    //   TrustedDevice - a person's phone, not a shop's
    //   SetupCode     - exists before its shop does (platform-level)
    //   AdminAuditLog - the platform admin's record, across shops (platform-level)
    //   Organization  - the shop itself; its own Id is the organisation id
    // Any other entity without the column fails, and the decision is the
    // owner's.
    // No database is used: the EF model is built without connecting.
    public class OrganizationIdGuardTests
    {
        private const string EntitiesNamespace = "Salio.Domain.Entities";
        private static readonly string[] Exemptions =
        {
            "AppUser",
            "TrustedDevice",
            "SetupCode",
            "AdminAuditLog",
            "Organization"
        };

        // Every class in Salio.Domain.Entities, plus every table in the EF
        // model, so neither a forgotten class nor a forgotten table escapes.
        public static IEnumerable<object[]> EveryEntity()
        {
            SortedSet<string> names = new();

            foreach (Type type in typeof(AppUser).Assembly.GetTypes())
            {
                if (type.IsClass && type.IsPublic && type.Namespace == EntitiesNamespace)
                {
                    names.Add(type.Name);
                }
            }

            using (SalioDbContext db = SalioModel.NewContext())
            {
                foreach (IEntityType entity in db.Model.GetEntityTypes())
                {
                    names.Add(NameOf(entity));
                }
            }

            foreach (string exempt in Exemptions)
            {
                names.Remove(exempt);
            }

            return names.Select(name => new object[] { name });
        }

        [Theory]
        [MemberData(nameof(EveryEntity))]
        public void Every_entity_but_AppUser_has_an_OrganizationId(string entityName)
        {
            Type? domainType = typeof(AppUser).Assembly.GetType(EntitiesNamespace + "." + entityName);
            if (domainType != null)
            {
                PropertyInfo? property = domainType.GetProperty("OrganizationId");
                Assert.True(property != null,
                    entityName + " has no OrganizationId property (locked rule 2).");
                Assert.True(property!.PropertyType == typeof(Guid),
                    entityName + ".OrganizationId is " + property.PropertyType.Name + ", not Guid.");
            }

            using SalioDbContext db = SalioModel.NewContext();
            IEntityType? entity = db.Model.GetEntityTypes().FirstOrDefault(e => NameOf(e) == entityName);
            if (entity != null)
            {
                IProperty? column = entity.FindProperty("OrganizationId");
                Assert.True(column != null,
                    "The " + entityName + " table has no OrganizationId column (locked rule 2).");
                Assert.True(column!.ClrType == typeof(Guid) && !column.IsNullable,
                    "The " + entityName + " table's OrganizationId must be a Guid that is never null.");
            }
        }

        [Fact]
        public void AppUser_has_no_OrganizationId()
        {
            // The exemption is real: if AppUser ever gains the column, the
            // exemption above should be removed rather than left lying.
            Assert.Null(typeof(AppUser).GetProperty("OrganizationId"));
        }

        private static string NameOf(IEntityType entity)
        {
            return entity.HasSharedClrType ? entity.Name : entity.ClrType.Name;
        }
    }
}
