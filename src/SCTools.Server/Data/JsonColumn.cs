using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SCTools.Server.Data;

/// <summary>
/// A column that holds a list or a dictionary as JSON (the same JSON as in the data file). Two values are the same when their JSON is the same, so a
/// session whose details did not change is not written again.
/// </summary>
public static class JsonColumn
{
    static readonly JsonSerializerOptions Options = new();

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string json) => string.IsNullOrWhiteSpace(json) ? Empty<T>() : JsonSerializer.Deserialize<T>(json, Options)!;

    /// <summary>
    /// A column added by a migration to a row that is there already holds an empty string, not JSON (30 Oct 2026: the site crashed at startup, and the deploy
    /// put the old release back, when CombinedAccounts came to the Settings row): that is an empty list or dictionary.
    /// </summary>
    static T Empty<T>() => (T)Activator.CreateInstance(typeof(T))!;

    /// <summary>Keeps this property as JSON.</summary>
    public static PropertyBuilder<T> AsJson<T>(this PropertyBuilder<T> property) where T : class?
    {
        var converter = new ValueConverter<T, string>(v => Write(v), s => Read<T>(s));
        var comparer = new ValueComparer<T>(
            (a, b) => Write(a) == Write(b),
            v => Write(v).GetHashCode(),
            v => Read<T>(Write(v)));
        property.HasConversion(converter, comparer);
        return property;
    }
}
