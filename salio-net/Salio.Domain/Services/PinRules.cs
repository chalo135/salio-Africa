namespace Salio.Domain.Services;

// Which PINs a person may choose. Four digits is only 10,000 choices, so the
// handful everyone guesses first (0000, 1234, 9876...) are refused.
public static class PinRules
{
    public static bool IsAcceptable(string pin)
    {
        if (pin.Length != 4)
        {
            return false;
        }

        foreach (char c in pin)
        {
            // Only 0-9. char.IsDigit would also let in digits from other
            // alphabets, which no keypad here can type.
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        bool allSame = true;
        bool runUp = true;
        bool runDown = true;

        for (int i = 1; i < 4; i++)
        {
            int difference = pin[i] - pin[i - 1];

            if (difference != 0)
            {
                allSame = false;
            }
            if (difference != 1)
            {
                runUp = false;
            }
            if (difference != -1)
            {
                runDown = false;
            }
        }

        return !allSame && !runUp && !runDown;
    }
}
