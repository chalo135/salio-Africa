using Salio.Infrastructure.Security;

namespace Salio.Tests
{
    public class SetupCodesTests
    {
        [Fact]
        public void A_new_code_is_six_characters_from_the_alphabet_only()
        {
            for (int i = 0; i < 200; i++)
            {
                string code = SetupCodes.Generate();

                Assert.Equal(6, code.Length);
                Assert.Matches("^[ABCDEFGHJKMNPQRSTVWXYZ23456789]{6}$", code);
            }
        }

        [Fact]
        public void The_alphabet_has_no_lookalike_characters()
        {
            foreach (char c in "ILOU01")
            {
                Assert.DoesNotContain(c, SetupCodes.Alphabet);
            }
        }

        [Fact]
        public void A_code_is_shown_as_two_groups_of_three()
        {
            Assert.Equal("K7M-P29", SetupCodes.Format("K7MP29"));
            Assert.Matches("^[A-Z2-9]{3}-[A-Z2-9]{3}$", SetupCodes.Format(SetupCodes.Generate()));
        }

        [Theory]
        [InlineData("K7M-P29")]
        [InlineData("k7m-p29")]
        [InlineData("k7mp29")]
        [InlineData("  K7M P29 ")]
        public void A_code_matches_however_it_is_typed(string typed)
        {
            Assert.Equal("K7MP29", SetupCodes.Normalise(typed));
            Assert.True(SetupCodes.IsWellFormed(SetupCodes.Normalise(typed)));
        }

        [Theory]
        [InlineData("K7MP2I")]
        [InlineData("K7MP2L")]
        [InlineData("K7MP2O")]
        [InlineData("K7MP2U")]
        [InlineData("K7MP20")]
        [InlineData("K7MP21")]
        [InlineData("K7MP2")]
        [InlineData("K7MP299")]
        [InlineData("")]
        [InlineData("ABCDEFGHJKMN")]
        public void Lookalike_characters_and_wrong_lengths_are_not_well_formed(string code)
        {
            Assert.False(SetupCodes.IsWellFormed(code));
        }

        [Fact]
        public void Two_new_codes_differ()
        {
            Assert.NotEqual(SetupCodes.Generate(), SetupCodes.Generate());
        }
    }
}
