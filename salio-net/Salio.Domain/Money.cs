namespace Salio.Domain;

/// <summary>
/// An amount of money, stored in minor units. KES 45.50 is Minor = 4550.
/// </summary>
public readonly record struct Money(long Minor, string Currency)
{
    public static Money Kes(long minor) => new(minor, "KES");

    public static Money operator +(Money left, Money right)
    {
        RequireSameCurrency(left, right);
        return new Money(left.Minor + right.Minor, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        RequireSameCurrency(left, right);
        return new Money(left.Minor - right.Minor, left.Currency);
    }

    private static void RequireSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot combine {left.Currency} and {right.Currency}.");
        }
    }

    public override string ToString()
    {
        string sign = Minor < 0 ? "-" : "";
        long units = Math.Abs(Minor) / 100;
        long cents = Math.Abs(Minor) % 100;
        return $"{Currency} {sign}{units}.{cents:00}";
    }
}
