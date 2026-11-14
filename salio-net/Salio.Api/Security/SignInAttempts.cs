namespace Salio.Api.Security;

/// <summary>
/// Counts wrong passwords per account, whatever address they come from.
/// After MaxFailures wrong passwords inside Window, that account's password
/// is not even checked until the window ends. A right password wipes the
/// count.
///
/// Why per account as well as per address (the "login" limit in
/// Program.cs): a whole shop, or several shops, can sit behind one address
/// on a mobile network, so the address limit has to be loose. This one stays
/// tight, and it also holds against a guesser who keeps changing address.
///
/// Kept in memory only: no table, and a restart forgets the counts. One
/// instance for the whole API (registered as a singleton in Program.cs).
/// </summary>
public class SignInAttempts
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    // Past this many accounts being counted, finished windows are thrown
    // away, so a flood of made-up emails cannot use up the memory.
    private const int TidyAbove = 10_000;

    private class Count
    {
        public int Failures;
        public DateTimeOffset WindowEnds;
    }

    private readonly Dictionary<string, Count> _counts = new();
    private readonly object _lock = new();

    // How long this account must wait, or null when it may try now.
    public TimeSpan? WaitFor(string account, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_counts.TryGetValue(Key(account), out Count? count)
                && now < count.WindowEnds
                && count.Failures >= MaxFailures)
            {
                return count.WindowEnds - now;
            }

            return null;
        }
    }

    public void RecordFailure(string account, DateTimeOffset now)
    {
        lock (_lock)
        {
            string key = Key(account);

            if (_counts.TryGetValue(key, out Count? count) && now < count.WindowEnds)
            {
                count.Failures++;
                return;
            }

            if (_counts.Count >= TidyAbove)
            {
                Tidy(now);
            }

            _counts[key] = new Count { Failures = 1, WindowEnds = now + Window };
        }
    }

    public void RecordSuccess(string account)
    {
        lock (_lock)
        {
            _counts.Remove(Key(account));
        }
    }

    // The same account however it was typed, and never longer than an
    // email address can be.
    private static string Key(string account)
    {
        string key = account.Trim().ToLowerInvariant();
        return key.Length <= 320 ? key : key[..320];
    }

    private void Tidy(DateTimeOffset now)
    {
        List<string> finished = _counts
            .Where(pair => now >= pair.Value.WindowEnds)
            .Select(pair => pair.Key)
            .ToList();

        foreach (string key in finished)
        {
            _counts.Remove(key);
        }

        // Still full: every one of them is inside its window. Start again
        // rather than grow without limit. The address limit still applies.
        if (_counts.Count >= TidyAbove)
        {
            _counts.Clear();
        }
    }
}
