using Salio.Domain.Services;

namespace Salio.Tests
{
    public class PinUnlockRulesTests
    {
        private const string Stamp = "11111111-1111-1111-1111-111111111111";
        private const string NewStamp = "22222222-2222-2222-2222-222222222222";

        [Fact]
        public void A_revoked_device_is_not_trusted_even_with_the_right_pin()
        {
            var result = PinUnlockRules.Decide(
                deviceRevoked: true, userIsActive: true, deviceStamp: Stamp, userStamp: Stamp,
                failedAttemptsSoFar: 0, pinMatches: true);

            Assert.Equal(PinUnlockOutcome.DeviceNotTrusted, result.Outcome);
        }

        [Fact]
        public void An_inactive_user_is_not_trusted_even_with_the_right_pin()
        {
            var result = PinUnlockRules.Decide(
                deviceRevoked: false, userIsActive: false, deviceStamp: Stamp, userStamp: Stamp,
                failedAttemptsSoFar: 0, pinMatches: true);

            Assert.Equal(PinUnlockOutcome.DeviceNotTrusted, result.Outcome);
        }

        [Fact]
        public void A_changed_stamp_is_not_trusted_even_with_the_right_pin()
        {
            // "Sign out every device" gave the user a new stamp after this
            // device was trusted.
            var result = PinUnlockRules.Decide(
                deviceRevoked: false, userIsActive: true, deviceStamp: Stamp, userStamp: NewStamp,
                failedAttemptsSoFar: 0, pinMatches: true);

            Assert.Equal(PinUnlockOutcome.DeviceNotTrusted, result.Outcome);
        }

        [Fact]
        public void Five_failures_already_is_locked_out_even_with_the_right_pin()
        {
            var result = PinUnlockRules.Decide(
                deviceRevoked: false, userIsActive: true, deviceStamp: Stamp, userStamp: Stamp,
                failedAttemptsSoFar: PinUnlockRules.MaxAttempts, pinMatches: true);

            Assert.Equal(PinUnlockOutcome.LockedOut, result.Outcome);
        }

        [Fact]
        public void The_right_pin_unlocks_and_clears_the_failures()
        {
            var result = PinUnlockRules.Decide(
                deviceRevoked: false, userIsActive: true, deviceStamp: Stamp, userStamp: Stamp,
                failedAttemptsSoFar: 3, pinMatches: true);

            Assert.Equal(PinUnlockOutcome.Unlocked, result.Outcome);
            Assert.Equal(0, result.FailedAttempts);
        }

        [Fact]
        public void A_first_wrong_pin_counts_one_failure()
        {
            var result = PinUnlockRules.Decide(
                deviceRevoked: false, userIsActive: true, deviceStamp: Stamp, userStamp: Stamp,
                failedAttemptsSoFar: 0, pinMatches: false);

            Assert.Equal(PinUnlockOutcome.WrongPin, result.Outcome);
            Assert.Equal(1, result.FailedAttempts);
        }

        [Fact]
        public void A_fifth_wrong_pin_locks_the_device_out()
        {
            var result = PinUnlockRules.Decide(
                deviceRevoked: false, userIsActive: true, deviceStamp: Stamp, userStamp: Stamp,
                failedAttemptsSoFar: 4, pinMatches: false);

            Assert.Equal(PinUnlockOutcome.LockedOut, result.Outcome);
            Assert.Equal(5, result.FailedAttempts);
        }
    }
}
