using Salio.Domain.Services;

namespace Salio.Tests
{
    public class PinRulesTests
    {
        [Theory]
        [InlineData("1357")]
        [InlineData("2580")]
        [InlineData("0102")]
        [InlineData("1233")]
        [InlineData("9870")]
        public void An_ordinary_pin_is_accepted(string pin)
        {
            Assert.True(PinRules.IsAcceptable(pin));
        }

        [Theory]
        [InlineData("")]
        [InlineData("135")]
        [InlineData("13579")]
        public void Only_exactly_four_characters_are_accepted(string pin)
        {
            Assert.False(PinRules.IsAcceptable(pin));
        }

        [Theory]
        [InlineData("12a4")]
        [InlineData("13 7")]
        [InlineData("-135")]
        [InlineData("١٣٥٧")] // Arabic-Indic digits: digits, but not 0-9
        public void Anything_but_the_digits_0_to_9_is_refused(string pin)
        {
            Assert.False(PinRules.IsAcceptable(pin));
        }

        [Theory]
        [InlineData("0000")]
        [InlineData("1111")]
        [InlineData("9999")]
        public void All_the_same_digit_is_refused(string pin)
        {
            Assert.False(PinRules.IsAcceptable(pin));
        }

        [Theory]
        [InlineData("0123")]
        [InlineData("1234")]
        [InlineData("6789")]
        public void A_run_upwards_is_refused(string pin)
        {
            Assert.False(PinRules.IsAcceptable(pin));
        }

        [Theory]
        [InlineData("9876")]
        [InlineData("4321")]
        [InlineData("3210")]
        public void A_run_downwards_is_refused(string pin)
        {
            Assert.False(PinRules.IsAcceptable(pin));
        }
    }
}
