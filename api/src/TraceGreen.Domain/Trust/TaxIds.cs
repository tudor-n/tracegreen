namespace TraceGreen.Domain.Trust;

/// <summary>Valid, invalid, or a country whose tax ids this code does not check.</summary>
public enum TaxIdCheck { Valid, Invalid, Unchecked }

/// <summary>
/// Check digits of the seller tax ids TraceGreen meets (F7): Romania's CUI (control key 753217532), China's unified social
/// credit code (GB 32100-2015: 18 characters, a mod-31 check character; characters 3–8 are the registration region) and
/// Taiwan's unified business number (weights 1,2,1,2,1,2,4,1, digit sums divisible by 5). A failed check is a typo or a
/// made-up number, and it splits one supplier in two on the supplier page, which groups deliveries by tax id.
/// </summary>
public static class TaxIds
{
    private const string UsccAlphabet = "0123456789ABCDEFGHJKLMNPQRTUWXY";

    private static readonly int[] UsccWeights = [1, 3, 9, 27, 19, 26, 16, 17, 20, 29, 25, 13, 8, 24, 10, 30, 28];

    private static readonly int[] CuiKey = [7, 5, 3, 2, 1, 7, 5, 3, 2];

    private static readonly int[] UbnWeights = [1, 2, 1, 2, 1, 2, 4, 1];

    /// <summary>
    /// The tax id within its country: whitespace removed, uppercase, and a leading prefix equal to the country dropped, so
    /// "RO99999984" and "99999984" from a Romanian seller are the same supplier.
    /// </summary>
    public static string Normalize(string taxId, string country)
    {
        var id = string.Concat(taxId.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        var prefix = country.Trim().ToUpperInvariant();
        return prefix.Length > 0 && id.Length > prefix.Length && id.StartsWith(prefix, StringComparison.Ordinal) ? id[prefix.Length..] : id;
    }

    public static TaxIdCheck Check(string taxId, string country)
    {
        var code = country.Trim().ToUpperInvariant();
        var id = Normalize(taxId, code);
        return code switch
        {
            "RO" => Verdict(IsValidCui(id)),
            "CN" => Verdict(IsValidUscc(id)),
            "TW" => Verdict(IsValidUbn(id)),
            _ => TaxIdCheck.Unchecked,
        };
    }

    public static bool IsValidCui(string digits)
    {
        if (digits.Length is < 2 or > 10 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }
        var body = digits[..^1].PadLeft(9, '0');
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * CuiKey[i];
        }
        var check = sum * 10 % 11 % 10;
        return check == digits[^1] - '0';
    }

    public static bool IsValidUscc(string code)
    {
        if (code.Length != 18 || code.Any(c => UsccAlphabet.IndexOf(c, StringComparison.Ordinal) < 0))
        {
            return false;
        }
        var sum = 0;
        for (var i = 0; i < 17; i++)
        {
            sum += UsccAlphabet.IndexOf(code[i], StringComparison.Ordinal) * UsccWeights[i];
        }
        var check = (31 - sum % 31) % 31;
        return UsccAlphabet[check] == code[17];
    }

    public static bool IsValidUbn(string digits)
    {
        if (digits.Length != 8 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }
        var sum = 0;
        for (var i = 0; i < 8; i++)
        {
            var product = (digits[i] - '0') * UbnWeights[i];
            sum += product / 10 + product % 10;
        }
        // A 7 in the seventh place gives 28, whose digit sum 10 may count as 1 or 0.
        return sum % 5 == 0 || (digits[6] == '7' && (sum + 1) % 5 == 0);
    }

    private static TaxIdCheck Verdict(bool valid) => valid ? TaxIdCheck.Valid : TaxIdCheck.Invalid;
}
