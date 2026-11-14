namespace Salio.Domain.Services;

public enum PinUnlockOutcome
{
    Unlocked,
    WrongPin,
    LockedOut,
    DeviceNotTrusted
}

public class PinUnlockResult
{
    public PinUnlockOutcome Outcome { get; set; }

    // What FailedPinAttempts on the device should be after this try.
    public int FailedAttempts { get; set; }
}

// Whether a PIN typed on a trusted phone unlocks it. Plain facts in, a
// decision out: no database, so it is tested without Postgres.
public static class PinUnlockRules
{
    public const int MaxAttempts = 5;

    public static PinUnlockResult Decide(
        bool deviceRevoked,
        bool userIsActive,
        string deviceStamp,
        string userStamp,
        int failedAttemptsSoFar,
        bool pinMatches)
    {
        // The ORDER is the security. Every refusal below sits above the PIN
        // check on purpose: a stolen tablet whose PIN the thief already knows
        // must not get in just because the four digits are right.

        // The owner switched this tablet off.
        if (deviceRevoked)
        {
            return new PinUnlockResult
            {
                Outcome = PinUnlockOutcome.DeviceNotTrusted,
                FailedAttempts = failedAttemptsSoFar
            };
        }

        // The person no longer works here.
        if (!userIsActive)
        {
            return new PinUnlockResult
            {
                Outcome = PinUnlockOutcome.DeviceNotTrusted,
                FailedAttempts = failedAttemptsSoFar
            };
        }

        // The two stamps matched when this tablet was trusted. They differ now,
        // so the password changed or every device was signed out since then.
        if (deviceStamp != userStamp)
        {
            return new PinUnlockResult
            {
                Outcome = PinUnlockOutcome.DeviceNotTrusted,
                FailedAttempts = failedAttemptsSoFar
            };
        }

        // Already out of tries. Right PIN or not, this tablet is finished until
        // someone signs in with email and password.
        if (failedAttemptsSoFar >= MaxAttempts)
        {
            return new PinUnlockResult
            {
                Outcome = PinUnlockOutcome.LockedOut,
                FailedAttempts = failedAttemptsSoFar
            };
        }

        // Right PIN. Let them in and wipe the slate.
        if (pinMatches)
        {
            return new PinUnlockResult
            {
                Outcome = PinUnlockOutcome.Unlocked,
                FailedAttempts = 0
            };
        }

        // Wrong PIN. This attempt counts. If it uses up the last one, the
        // tablet locks now rather than offering a try that does not exist.
        int attempts = failedAttemptsSoFar + 1;

        if (attempts >= MaxAttempts)
        {
            return new PinUnlockResult
            {
                Outcome = PinUnlockOutcome.LockedOut,
                FailedAttempts = attempts
            };
        }

        return new PinUnlockResult
        {
            Outcome = PinUnlockOutcome.WrongPin,
            FailedAttempts = attempts
        };
    }
}