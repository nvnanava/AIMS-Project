using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AIMS.Utilities;

public static class SupervisorScopeHelper
{
    /// <summary>
    /// Returns the set of UserIDs in scope for a supervisor: { supervisorId } ∪ { direct reports }.
    /// Uses IMemoryCache when provided.
    /// </summary>
    public static async Task<List<int>> GetSupervisorScopeUserIdsAsync(
        AimsDbContext db,
        int supervisorId,
        IMemoryCache? cache = null,
        CancellationToken ct = default)
    {
        var cacheKey = $"scopeIds:supervisor:{supervisorId}";

        if (cache != null && cache.TryGetValue(cacheKey, out List<int>? cached) && cached is { Count: > 0 })
            return cached;

        var ids = await db.Users.AsNoTracking()
            .Where(u => u.UserID == supervisorId || u.SupervisorID == supervisorId)
            .Select(u => u.UserID)
            .ToListAsync(ct);

        if (cache != null)
            cache.Set(cacheKey, ids, TimeSpan.FromMinutes(5));

        return ids;
    }
}
