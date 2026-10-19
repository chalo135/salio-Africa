namespace Salio.Domain.Enums
{
    // Two roles to start (locked rule 13). A Cashier may sell and see their
    // own sales. An Owner may do everything a Cashier can, plus see money,
    // profit, stock value and tax. A new role needs a decision, not a guess.
    public enum MembershipRole
    {
        Owner = 0,
        Cashier = 1
    }
}
