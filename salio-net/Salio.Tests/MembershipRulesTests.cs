using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests
{
    public class MembershipRulesTests
    {
        private static readonly Guid ShopA = Guid.NewGuid();
        private static readonly Guid ShopB = Guid.NewGuid();
        private static readonly Guid ShopNobodyWorksAt = Guid.NewGuid();
        private static readonly Guid UserId = Guid.NewGuid();

        [Fact]
        public void A_member_naming_their_own_shop_is_allowed()
        {
            MembershipRules rules = new();

            var result = rules.Resolve(
                new List<Membership> { NewMembership(ShopA, MembershipRole.Cashier) },
                ShopA);

            Assert.Equal(MembershipAccessOutcome.Allowed, result.Outcome);
            Assert.Equal(ShopA, result.OrganizationId);
            Assert.Equal(MembershipRole.Cashier, result.Role);
            Assert.False(result.IsOwner);
        }

        [Fact]
        public void Naming_a_shop_you_do_not_belong_to_is_refused()
        {
            MembershipRules rules = new();

            var result = rules.Resolve(
                new List<Membership> { NewMembership(ShopA, MembershipRole.Owner) },
                ShopNobodyWorksAt);

            Assert.Equal(MembershipAccessOutcome.NotAMember, result.Outcome);
            Assert.False(result.IsAllowed);
        }

        [Fact]
        public void Naming_another_persons_shop_does_not_leak_it()
        {
            MembershipRules rules = new();

            // The user works at A. They ask for B, which exists and belongs
            // to somebody else. Rule 9: the id counts for nothing on its own.
            var result = rules.Resolve(
                new List<Membership> { NewMembership(ShopA, MembershipRole.Owner) },
                ShopB);

            Assert.Equal(MembershipAccessOutcome.NotAMember, result.Outcome);
            Assert.NotEqual(ShopB, result.OrganizationId);
        }

        [Fact]
        public void With_one_shop_and_nothing_named_the_server_picks_it()
        {
            MembershipRules rules = new();

            var result = rules.Resolve(
                new List<Membership> { NewMembership(ShopA, MembershipRole.Owner) },
                requestedOrganizationId: null);

            Assert.Equal(MembershipAccessOutcome.Allowed, result.Outcome);
            Assert.Equal(ShopA, result.OrganizationId);
            Assert.True(result.IsOwner);
        }

        [Fact]
        public void With_two_shops_and_nothing_named_the_caller_must_choose()
        {
            MembershipRules rules = new();

            var result = rules.Resolve(
                new List<Membership>
                {
                    NewMembership(ShopA, MembershipRole.Owner),
                    NewMembership(ShopB, MembershipRole.Cashier)
                },
                requestedOrganizationId: null);

            Assert.Equal(MembershipAccessOutcome.MustChooseOrganization, result.Outcome);
            Assert.False(result.IsAllowed);
        }

        [Fact]
        public void The_role_comes_from_the_shop_that_was_named()
        {
            MembershipRules rules = new();

            // Owner in one shop, Cashier in another. Naming the second must
            // not carry the first shop's powers across.
            var memberships = new List<Membership>
            {
                NewMembership(ShopA, MembershipRole.Owner),
                NewMembership(ShopB, MembershipRole.Cashier)
            };

            Assert.True(rules.Resolve(memberships, ShopA).IsOwner);
            Assert.False(rules.Resolve(memberships, ShopB).IsOwner);
        }

        [Fact]
        public void A_user_with_no_memberships_gets_nothing()
        {
            MembershipRules rules = new();

            var withName = rules.Resolve(new List<Membership>(), ShopA);
            var withoutName = rules.Resolve(new List<Membership>(), null);

            Assert.Equal(MembershipAccessOutcome.NoMemberships, withName.Outcome);
            Assert.Equal(MembershipAccessOutcome.NoMemberships, withoutName.Outcome);
        }

        private static Membership NewMembership(Guid organizationId, MembershipRole role) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AppUserId = UserId,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
