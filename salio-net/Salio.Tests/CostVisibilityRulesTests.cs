using Salio.Domain.Enums;
using Salio.Domain.Services;
using Xunit;

namespace Salio.Tests;

// Who may see the shop's buying price. CanSeeCost is a placeholder that says
// no to everyone, so the three "sees cost" tests below fail until Chalo
// writes the real rule. That is the expected state.
public class CostVisibilityRulesTests
{
    private static CostVisibilityContext Context(
        MembershipRole role,
        bool cashierSeesCost,
        bool active = true,
        bool platformAdmin = false)
    {
        return new CostVisibilityContext(role, cashierSeesCost, active, platformAdmin);
    }

    [Fact]
    public void Owner_sees_cost_whether_the_shop_setting_is_on_or_off()
    {
        Assert.True(CostVisibilityRules.CanSeeCost(Context(MembershipRole.Owner, cashierSeesCost: false)));
        Assert.True(CostVisibilityRules.CanSeeCost(Context(MembershipRole.Owner, cashierSeesCost: true)));
    }

    [Fact]
    public void Platform_admin_sees_cost()
    {
        Assert.True(CostVisibilityRules.CanSeeCost(
            Context(MembershipRole.Cashier, cashierSeesCost: false, platformAdmin: true)));
    }

    [Fact]
    public void Cashier_does_not_see_cost_when_the_shop_setting_is_off()
    {
        Assert.False(CostVisibilityRules.CanSeeCost(Context(MembershipRole.Cashier, cashierSeesCost: false)));
    }

    [Fact]
    public void Cashier_sees_cost_when_the_shop_setting_is_on()
    {
        Assert.True(CostVisibilityRules.CanSeeCost(Context(MembershipRole.Cashier, cashierSeesCost: true)));
    }

    [Theory]
    [InlineData(MembershipRole.Owner, false, false)]
    [InlineData(MembershipRole.Owner, true, false)]
    [InlineData(MembershipRole.Cashier, false, false)]
    [InlineData(MembershipRole.Cashier, true, false)]
    [InlineData(MembershipRole.Owner, true, true)]
    [InlineData(MembershipRole.Cashier, true, true)]
    public void An_inactive_member_never_sees_cost(MembershipRole role, bool cashierSeesCost, bool platformAdmin)
    {
        Assert.False(CostVisibilityRules.CanSeeCost(
            Context(role, cashierSeesCost, active: false, platformAdmin: platformAdmin)));
    }
}
