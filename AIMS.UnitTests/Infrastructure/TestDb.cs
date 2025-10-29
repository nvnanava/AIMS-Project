using System;
using AIMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AIMS.UnitTests.Infrastructure
{
    /// <summary>
    /// Factory helpers for one-db-per-test using EFCore InMemory.
    /// </summary>
    public static class TestDb
    {
        public static AimsDbContext Create(string? name = null)
        {
            var dbName = name ?? $"aims_tests_{Guid.NewGuid():N}";
            var opts = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(dbName)
                .EnableSensitiveDataLogging()
                .Options;
            var ctx = new AimsDbContext(opts);
            ctx.Database.EnsureCreated();
            return ctx;
        }

        public static IMemoryCache CreateCache() =>
            new MemoryCache(Options.Create(new MemoryCacheOptions()));
    }
}
