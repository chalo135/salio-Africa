using System.Text.Json;
using System.Text.Json.Serialization;

namespace Salio.Infrastructure.Json;

/// <summary>
/// Turns every DateTimeOffset a client sends into UTC as it is read, so a
/// phone set to Nairobi time ("+03:00") never reaches the database. Npgsql
/// refuses to save a DateTimeOffset that is not UTC, which used to be a 500.
///
/// Registered once in Program.cs for all controllers. System.Text.Json uses
/// it for DateTimeOffset? too, and leaves a null as null.
/// </summary>
public class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // The same moment in time, written as UTC.
        return reader.GetDateTimeOffset().ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
