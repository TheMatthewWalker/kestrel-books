namespace KestrelBooks.Api.Services;

public static class Paging
{
    /// <summary>Clamps paging inputs (page ≥ 1, 1 ≤ pageSize ≤ 500) and returns skip/take.</summary>
    public static (int skip, int take) Normalise(ref int page, ref int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        return ((page - 1) * pageSize, pageSize);
    }
}
