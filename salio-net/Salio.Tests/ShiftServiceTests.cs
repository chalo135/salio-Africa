using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests
{
    public class ShiftServiceTests
    {
        private static readonly Guid OrganizationId = Guid.NewGuid();
        private static readonly Guid SaleId = Guid.NewGuid();

        [Fact]
        public void Expected_cash_is_the_float_plus_the_cash_payments()
        {
            Shift shift = NewShift(openingFloatMinor: 500000);

            List<Payment> payments = new()
            {
                NewPayment(PaymentMethod.Cash, 10000),
                NewPayment(PaymentMethod.Cash, 25000)
            };

            ShiftService service = new();

            Assert.Equal(535000, service.CalculateExpectedCash(shift, payments));
        }

        [Fact]
        public void Payments_that_are_not_cash_are_ignored()
        {
            Shift shift = NewShift(openingFloatMinor: 500000);

            List<Payment> payments = new()
            {
                NewPayment(PaymentMethod.Cash, 10000),
                NewPayment(PaymentMethod.Mpesa, 90000),
                NewPayment(PaymentMethod.Card, 70000),
                NewPayment(PaymentMethod.Credit, 40000)
            };

            ShiftService service = new();

            // Only the 10000 in cash reaches the drawer.
            Assert.Equal(510000, service.CalculateExpectedCash(shift, payments));
        }

        [Fact]
        public void With_no_payments_the_expected_cash_is_just_the_float()
        {
            Shift shift = NewShift(openingFloatMinor: 500000);

            List<Payment> payments = new();

            ShiftService service = new();

            Assert.Equal(500000, service.CalculateExpectedCash(shift, payments));
        }

        [Fact]
        public void Counting_less_than_expected_gives_a_negative_variance()
        {
            ShiftService service = new();

            // 5000 short: the drawer is missing KES 50.
            Assert.Equal(-5000, service.CalculateVariance(expected: 535000, counted: 530000));
        }

        [Fact]
        public void Counting_exactly_what_was_expected_gives_no_variance()
        {
            ShiftService service = new();

            Assert.Equal(0, service.CalculateVariance(expected: 535000, counted: 535000));
        }

        private static Shift NewShift(long openingFloatMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            OpenedBy = "Wanjiru",
            OpenedAt = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.FromHours(3)),
            OpeningFloatMinor = openingFloatMinor,
            ClosedAt = null,
            CountedCashMinor = null,
            Status = ShiftStatus.Open
        };

        private static Payment NewPayment(PaymentMethod method, long amountMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            SaleId = SaleId,
            Method = method,
            AmountMinor = amountMinor,
            Reference = method == PaymentMethod.Mpesa ? "QGR7XK2L91" : null,
            ReceivedAt = new DateTimeOffset(2026, 9, 10, 11, 30, 0, TimeSpan.FromHours(3))
        };
    }
}
