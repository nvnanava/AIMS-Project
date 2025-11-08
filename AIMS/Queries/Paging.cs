using AIMS.Dtos.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AIMS.Queries;

public static class Paging
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(2);

    // Exact totals (COUNT + page slice), cached by a stable key.
    public static async Task<PagedResult<T>> PageExactCachedAsync<T>(
        IMemoryCache cache,
        string cacheKeyBase,                 // stable fingerprint WITHOUT page/size for the COUNT
        IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken ct,
        TimeSpan? ttl = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 25, 200);
        var skip = (page - 1) * pageSize;

        // 1) cache the COUNT by the base key (independent of page/size)
        var totalKey = $"{cacheKeyBase}:total";
        var total = await cache.GetOrCreateAsync(totalKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl;
            return await query.CountAsync(ct);
        });

        // 2) cache the page slice by page+size
        var pageKey = $"{cacheKeyBase}:page={page}:size={pageSize}";
        var items = await cache.GetOrCreateAsync(pageKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl;
            return await query.Skip(skip).Take(pageSize).ToListAsync(ct);
        });

        return new PagedResult<T>
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items ?? new List<T>()
        };
    }

    // ----------------------------------------------------------------------
    // Look-ahead totals (no COUNT), cached by page+size.
    // Total = -1 when there is at least one more page; exact on last page.
    // ----------------------------------------------------------------------
    public static async Task<PagedResult<T>> PageLookAheadCachedAsync<T>(
        IMemoryCache cache,
        string cacheKeyBase,                 // include page/size in the final key
        IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken ct,
        TimeSpan? ttl = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 25, 200);
        var skip = (page - 1) * pageSize;

        var key = $"{cacheKeyBase}:page={page}:size={pageSize}";
        var slice = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl;
            return await query.Skip(skip).Take(pageSize + 1).ToListAsync(ct);
        });

        slice ??= new List<T>();

        var hasMore = slice.Count > pageSize;
        var items = hasMore ? slice.Take(pageSize).ToArray() : slice.ToArray();
        var total = hasMore ? -1 : ((page - 1) * pageSize) + items.Length;

        return new PagedResult<T>
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        };
    }
}
