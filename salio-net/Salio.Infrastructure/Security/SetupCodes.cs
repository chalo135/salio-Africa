using System.Security.Cryptography;

namespace Salio.Infrastructure.Security;

/// <summary>
/// The shape of a setup code: making one, tidying what someone typed, and
/// showing it. Used only through SetupCodeService and its tests.
///
/// Locked rule 11: no cryptography is written here. The randomness is
/// .NET's own secure generator; this class only picks the letters and the
/// layout.
/// </summary>
public static class SetupCodes
{
    // No I, L, O, U, 0 or 1: a code is read out over the phone or copied by
    // hand, and those are the letters people mix up.
    public const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    public const int Length = 6;

    // Six characters, stored and compared like this: "K7MP29".
    public static string Generate()
    {
        return RandomNumberGenerator.GetString(Alphabet, Length);
    }

    // What is stored and compared: upper case, no dash, no spaces. So
    // "k7m-p29", "K7M P29" and "K7MP29" are one code.
    public static string Normalise(string? typed)
    {
        if (typed is null)
        {
            return string.Empty;
        }

        var kept = new System.Text.StringBuilder();

        foreach (char c in typed.Trim().ToUpperInvariant())
        {
            if (c != '-' && c != ' ')
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
    }

    // True only for exactly six characters, all from the alphabet. Expects a
    // code that has been through Normalise.
    public static bool IsWellFormed(string normalised)
    {
        if (normalised.Length != Length)
        {
            return false;
        }

        foreach (char c in normalised)
        {
            if (!Alphabet.Contains(c))
            {
                return false;
            }
        }

        return true;
    }

    // How a code is shown and sent: two groups of three, "K7M-P29". The dash
    // is only for reading; it is never stored.
    public static string Format(string code)
    {
        string normalised = Normalise(code);

        if (normalised.Length != Length)
        {
            return normalised;
        }

        return normalised[..3] + "-" + normalised[3..];
    }
}
