namespace AIMS.ViewModels;

public enum PagingTotals
{
    // Use look-ahead (pageSize + 1) and set Total = -1 when there are more pages.
    Lookahead = 0,
    // Compute exact Count(*) and set Tottal to the exact total rows.
    Exact = 1
}