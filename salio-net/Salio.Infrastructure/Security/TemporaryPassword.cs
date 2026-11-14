using System.Security.Cryptography;

namespace Salio.Infrastructure.Security;

/// <summary>
/// A password an Owner reads out to a new person, who must replace it at
/// their first sign-in. Locked rule 11: nothing here is cryptography written
/// in this repo. The randomness is .NET's own secure generator; this class
/// only chooses which letters it may pick from.
/// </summary>
public static class TemporaryPassword
{
    // Lower case letters and digits, leaving out the ones that are confused
    // when read aloud or written by hand: 0 and o, 1 and l and i.
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    // 10 characters from 31: well past the 8-character minimum, and short
    // enough to read out and type on a phone.
    private const int Length = 10;

    public static string Generate()
    {
        return RandomNumberGenerator.GetString(Alphabet, Length);
    }
}
