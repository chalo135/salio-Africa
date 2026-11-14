using Salio.Domain.Services;

namespace Salio.Tests
{
    // One transaction, one date: a stock movement must tell the same day as
    // the journal entry it belongs to.
    public class StockMovementDatesTests
    {
        // 09 Nov 2026, 10:00 in Kenya (07:00 UTC).
        private static readonly DateTimeOffset MorningOfThe9th =
            new(2026, 11, 9, 7, 0, 0, TimeSpan.Zero);

        [Fact]
        public void An_entry_dated_today_keeps_the_real_moment()
        {
            DateTimeOffset occurredAt = StockMovementDates.OccurredAtFor(new DateOnly(2026, 11, 9), MorningOfThe9th);

            Assert.Equal(MorningOfThe9th, occurredAt);
        }

        [Fact]
        public void An_invoice_dated_07_Nov_committed_on_09_Nov_puts_its_stock_on_07_Nov()
        {
            var invoiceDate = new DateOnly(2026, 11, 7);

            DateTimeOffset occurredAt = StockMovementDates.OccurredAtFor(invoiceDate, MorningOfThe9th);

            Assert.Equal(invoiceDate, StockMovementDates.KenyaDateOf(occurredAt));
            // And in UTC too, which is how the stock report cuts days.
            Assert.Equal(invoiceDate, DateOnly.FromDateTime(occurredAt.UtcDateTime));
        }

        [Fact]
        public void Just_after_midnight_in_Kenya_today_is_Kenyas_today_not_UTCs()
        {
            // 21:30 UTC on the 8th is 00:30 on the 9th in Kenya.
            var justAfterMidnight = new DateTimeOffset(2026, 11, 8, 21, 30, 0, TimeSpan.Zero);

            DateTimeOffset occurredAt = StockMovementDates.OccurredAtFor(new DateOnly(2026, 11, 9), justAfterMidnight);

            Assert.Equal(justAfterMidnight, occurredAt);
            Assert.Equal(new DateOnly(2026, 11, 9), StockMovementDates.KenyaDateOf(occurredAt));
        }

        [Fact]
        public void The_moment_is_always_in_UTC_for_the_database()
        {
            var kenyaClock = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(3));

            Assert.Equal(TimeSpan.Zero, StockMovementDates.OccurredAtFor(new DateOnly(2026, 11, 9), kenyaClock).Offset);
            Assert.Equal(TimeSpan.Zero, StockMovementDates.OccurredAtFor(new DateOnly(2026, 10, 31), kenyaClock).Offset);
        }

        [Fact]
        public void Any_entry_date_gives_a_movement_on_that_same_day()
        {
            var start = new DateOnly(2025, 12, 25);

            for (int day = 0; day < 400; day++)
            {
                DateOnly entryDate = start.AddDays(day);
                DateTimeOffset occurredAt = StockMovementDates.OccurredAtFor(entryDate, MorningOfThe9th);

                Assert.Equal(entryDate, StockMovementDates.KenyaDateOf(occurredAt));
            }
        }
    }
}
