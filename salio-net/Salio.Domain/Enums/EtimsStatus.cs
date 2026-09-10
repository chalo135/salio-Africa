namespace Salio.Domain.Enums
{
    // Where a sale stands with KRA. NotRequired is the default and covers
    // everything until eTIMS integration is actually switched on.
    public enum EtimsStatus
    {
        NotRequired,
        Queued,
        Transmitting,
        Transmitted,
        Rejected
    }
}
