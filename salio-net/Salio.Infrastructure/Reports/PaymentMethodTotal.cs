using Salio.Domain.Enums;

namespace Salio.Infrastructure.Reports
{
    public class PaymentMethodTotal
    {
        public PaymentMethod Method { get; set; }
        public long AmountMinor { get; set; }
    }
}
