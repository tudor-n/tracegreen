using System.Text.RegularExpressions;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Trust;

/// <summary>
/// Format and check-digit rules for the identifiers a supplier can give (F7). A check digit catches a typo and most made-up
/// numbers: a random container number passes ISO 6346 about one time in ten, a random air waybill one time in seven. The
/// other documents have no check digit, so only their shape is checked. <c>web/src/lib/evidence.ts</c> mirrors these rules
/// for instant feedback; the API stays the authority.
/// </summary>
public static partial class DocumentIds
{
    /// <summary>Which modes each document goes with: an air waybill means air, a bill of lading sea, a CMR road.</summary>
    public static bool Fits(ShippingDocType type, TransportMode mode) => type switch
    {
        // Containers also travel by rail and on trucks.
        ShippingDocType.ContainerNumber => mode is TransportMode.Sea or TransportMode.Rail or TransportMode.Road,
        ShippingDocType.AirWaybill => mode == TransportMode.Air,
        ShippingDocType.BillOfLading => mode == TransportMode.Sea,
        ShippingDocType.Cmr or ShippingDocType.ETransportUit => mode == TransportMode.Road,
        ShippingDocType.RailConsignment => mode == TransportMode.Rail,
        _ => false,
    };

    /// <summary>Uppercase without spaces, and without hyphens and dots for the numbers that are digits and letters only.</summary>
    public static string Normalize(ShippingDocType type, string id)
    {
        var compact = string.Concat(id.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        return type is ShippingDocType.ContainerNumber or ShippingDocType.AirWaybill
            ? compact.Replace("-", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal)
            : compact;
    }

    /// <summary>Why <paramref name="id"/> (already normalised) is not a valid <paramref name="type"/>, or null when it is.</summary>
    public static string? Problem(ShippingDocType type, string id) => type switch
    {
        ShippingDocType.ContainerNumber => !ContainerShape().IsMatch(id)
            ? "A container number is 4 letters (the 4th U, J or Z) and 7 digits."
            : IsoCheckDigit(id) != id[10] - '0' ? "The container number's check digit does not match." : null,
        ShippingDocType.AirWaybill => !AwbShape().IsMatch(id)
            ? "An air waybill number is 11 digits: the airline's 3 and a serial of 8."
            : long.Parse(id.AsSpan(3, 7), System.Globalization.CultureInfo.InvariantCulture) % 7 != id[10] - '0'
                ? "The air waybill number's check digit does not match."
                : null,
        ShippingDocType.BillOfLading => FreeShape(id, 6, 20, "A bill of lading number"),
        ShippingDocType.Cmr => FreeShape(id, 4, 20, "A CMR number"),
        ShippingDocType.ETransportUit => FreeShape(id, 8, 20, "An e-Transport UIT code"),
        ShippingDocType.RailConsignment => FreeShape(id, 4, 20, "A rail consignment note number"),
        _ => "Unknown document type.",
    };

    /// <summary>A certificate or contract id: 4 to 64 letters, digits and <c>- _ . / :</c>, kept as written but trimmed.</summary>
    public static string? CertificateProblem(string id) =>
        CertificateShape().IsMatch(id) ? null : "A certificate id is 4 to 64 letters, digits and - _ . / : characters.";

    /// <summary>The ISO 6346 check digit of the first ten characters: letters valued from 10 skipping multiples of 11, weights 2^i, sum mod 11 mod 10.</summary>
    public static int IsoCheckDigit(string id)
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var c = id[i];
            sum += (char.IsAsciiDigit(c) ? c - '0' : LetterValue(c)) << i;
        }
        return sum % 11 % 10;
    }

    private static int LetterValue(char letter)
    {
        var value = 10;
        for (var c = 'A'; c < letter; c++)
        {
            value++;
            if (value % 11 == 0)
            {
                value++;
            }
        }
        return value;
    }

    private static string? FreeShape(string id, int min, int max, string what) =>
        id.Length >= min && id.Length <= max && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '/')
            ? null
            : $"{what} is {min} to {max} letters, digits, - or /.";

    [GeneratedRegex("^[A-Z]{3}[UJZ][0-9]{7}$")]
    private static partial Regex ContainerShape();

    [GeneratedRegex("^[0-9]{11}$")]
    private static partial Regex AwbShape();

    [GeneratedRegex("^[A-Za-z0-9_./:-]{4,64}$")]
    private static partial Regex CertificateShape();
}
