using AIMS.Data;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Tests.Integration;

public static class MigrateDb
{
    public static AimsDbContext CreateContext(string cs) =>
        new(new DbContextOptionsBuilder<AimsDbContext>()
            .UseSqlServer(cs)
            .Options);
}