using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
namespace Venly.Database.Helper;
/// <summary>
/// Stores an enum as lower snake_case TEXT ("PhoneVerification" becomes "phone_verification"), for services whose
/// design calls for a text column with a CHECK list rather than a native PostgreSQL enum (Eventspaces DBD 1.2).
/// Adding a state is then a migration that edits a constraint, not one that has to ALTER TYPE.
/// The wire carries the name and the database carries the name; neither depends on declaration order.
/// Services that want native enums use <see cref="VenlyEnumNameTranslator"/> instead.
/// </summary>
public sealed partial class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => ToText(value), text => FromText(text)) where TEnum : struct, Enum
{
    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex Boundary();
    public static string ToText(TEnum value) => Boundary().Replace(value.ToString(), "_$1").ToLowerInvariant();
    public static TEnum FromText(string text) => Enum.GetValues<TEnum>().First(v => ToText(v) == text);
    /// <summary>Every stored value, for building the CHECK list.</summary>
    public static IEnumerable<string> AllTexts() => Enum.GetValues<TEnum>().Select(ToText);
}
