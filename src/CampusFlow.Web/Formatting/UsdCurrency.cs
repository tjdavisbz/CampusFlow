namespace CampusFlow.Web.Formatting;

public static class UsdCurrency
{
    public static string Format(decimal amount) =>
        amount.ToString("$#,##0.00;($#,##0.00);$0.00");
}
