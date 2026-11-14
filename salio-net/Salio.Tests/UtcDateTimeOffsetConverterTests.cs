using System.Text.Json;
using Salio.Infrastructure.Json;

namespace Salio.Tests
{
    public class UtcDateTimeOffsetConverterTests
    {
        private class Holder
        {
            public DateTimeOffset At { get; set; }
            public DateTimeOffset? Maybe { get; set; }
        }

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new UtcDateTimeOffsetConverter() }
        };

        [Fact]
        public void A_Nairobi_time_is_read_as_the_same_moment_in_UTC()
        {
            Holder? holder = JsonSerializer.Deserialize<Holder>(
                "{\"at\":\"2026-11-08T15:30:00+03:00\"}", Options);

            Assert.NotNull(holder);
            Assert.Equal(TimeSpan.Zero, holder.At.Offset);
            Assert.Equal(new DateTimeOffset(2026, 11, 8, 12, 30, 0, TimeSpan.Zero), holder.At);
        }

        [Fact]
        public void A_nullable_time_is_made_UTC_and_a_null_stays_null()
        {
            Holder? withValue = JsonSerializer.Deserialize<Holder>(
                "{\"at\":\"2026-11-08T00:00:00Z\",\"maybe\":\"2026-11-08T03:00:00+03:00\"}", Options);

            Assert.NotNull(withValue);
            Assert.NotNull(withValue.Maybe);
            Assert.Equal(TimeSpan.Zero, withValue.Maybe.Value.Offset);
            Assert.Equal(new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero), withValue.Maybe.Value);

            Holder? withNull = JsonSerializer.Deserialize<Holder>(
                "{\"at\":\"2026-11-08T00:00:00Z\",\"maybe\":null}", Options);

            Assert.NotNull(withNull);
            Assert.Null(withNull.Maybe);
        }
    }
}
