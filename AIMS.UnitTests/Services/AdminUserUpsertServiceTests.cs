using AppUser = AIMS.Models.User;
using GraphUser = Microsoft.Graph.Models.User;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Models;
using AIMS.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Graph.Models;
using Moq;
using Xunit;

public class AdminUserUpsertServiceTests
{
    private static DbContextOptions<AimsDbContext> InMemoryOptions(string dbName) =>
        new DbContextOptionsBuilder<AimsDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static Mock<IGraphUserService> MakeGraphMock(
        string graphId,
        string? displayName = "Ada Lovelace",
        string? mail = "ada@contoso.com",
        string? upn = "ada@contoso.com")
    {
        var m = new Mock<IGraphUserService>();
        m.Setup(x => x.GetUserByIdAsync(graphId, It.IsAny<CancellationToken>()))
         .ReturnsAsync(new GraphUser
         {
             Id = graphId,
             DisplayName = displayName,
             Mail = mail,
             UserPrincipalName = upn
         });
        return m;
    }

    [Fact]
    public async Task Upsert_NewUser_InsertsOneRow()
    {
        var graphId = "graph-1";
        var options = InMemoryOptions(nameof(Upsert_NewUser_InsertsOneRow));
        var graph = MakeGraphMock(graphId);

        using var db = new AimsDbContext(options);
        db.Database.EnsureCreated();

        var svc = new AdminUserUpsertService(db, graph.Object);

        var saved = await svc.UpsertAdminUserAsync(graphId, roleId: 5, supervisorId: null, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(graphId, saved.GraphObjectID);
        Assert.Equal("Ada Lovelace", saved.FullName);
        Assert.Equal("ada@contoso.com", saved.Email);
        Assert.True(saved.IsActive);
        Assert.False(saved.IsArchived);
        Assert.Equal(5, saved.RoleID);

        var count = await db.Users.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Upsert_ExistingUser_UpdatesAndDoesNotDuplicate()
    {
        var graphId = "graph-2";
        var options = InMemoryOptions(nameof(Upsert_ExistingUser_UpdatesAndDoesNotDuplicate));
        var graph = MakeGraphMock(graphId, displayName: "Updated Name", mail: "updated@contoso.com");

        using var db = new AimsDbContext(options);
        db.Database.EnsureCreated();

    db.Users.Add(new AppUser
        {
            GraphObjectID = graphId,
            FullName = "Old Name",
            Email = "old@contoso.com",
            EmployeeNumber = "abcdef01",
            IsActive = false,
            IsArchived = false,
            RoleID = 1
        });
        await db.SaveChangesAsync();

        var svc = new AdminUserUpsertService(db, graph.Object);

        var saved = await svc.UpsertAdminUserAsync(graphId, roleId: 7, supervisorId: 3, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal("Updated Name", saved.FullName);
        Assert.Equal("updated@contoso.com", saved.Email);
        Assert.True(saved.IsActive);
        Assert.False(saved.IsArchived);
        Assert.Equal(7, saved.RoleID);
        Assert.Equal(3, saved.SupervisorID);

        var count = await db.Users.CountAsync();
        Assert.Equal(1, count); // no duplicate
    }

    [Fact]
    public async Task Upsert_ArchivedExisting_UnarchivesAndUpdates()
    {
        var graphId = "graph-3";
        var options = InMemoryOptions(nameof(Upsert_ArchivedExisting_UnarchivesAndUpdates));
        var graph = MakeGraphMock(graphId, displayName: "Resurrected", mail: "back@contoso.com");

        using var db = new AimsDbContext(options);
        db.Database.EnsureCreated();

    db.Users.Add(new AppUser
        {
            GraphObjectID = graphId,
            FullName = "Archived Name",
            Email = "archived@contoso.com",
            EmployeeNumber = "aaaa0001",
            IsActive = false,
            IsArchived = true,
            RoleID = 2
        });
        await db.SaveChangesAsync();

        var svc = new AdminUserUpsertService(db, graph.Object);

        var saved = await svc.UpsertAdminUserAsync(graphId, roleId: 9, supervisorId: null, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal("Resurrected", saved.FullName);
        Assert.Equal("back@contoso.com", saved.Email);
        Assert.True(saved.IsActive);
        Assert.False(saved.IsArchived);
        Assert.Equal(9, saved.RoleID);

        var count = await db.Users.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Upsert_RaceCondition_UniqueViolation_ThrowsException()
    {
        var graphId = "graph-4";
        var options = InMemoryOptions(nameof(Upsert_RaceCondition_UniqueViolation_ThrowsException));
        var graph = MakeGraphMock(graphId, displayName: "Racer", mail: "race@contoso.com");

        // Seed an existing user (simulate another request inserted first)
        using (var seedDb = new AimsDbContext(options))
        {
            seedDb.Database.EnsureCreated();
            seedDb.Users.Add(new AppUser
            {
                GraphObjectID = graphId,
                FullName = "Already There",
                Email = "already@contoso.com",
                EmployeeNumber = "seed0001",
                IsActive = true,
                IsArchived = false,
                RoleID = 1
            });
            await seedDb.SaveChangesAsync();
        }

        // Use a DbContext that throws a one-time unique violation to hit the catch path
        using var throwingDb = new ThrowOnceOnSaveAimsDbContext(options, throwOnce: true);
        var svc = new AdminUserUpsertService(throwingDb, graph.Object);

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await svc.UpsertAdminUserAsync(graphId, roleId: 10, supervisorId: null, CancellationToken.None));
    }

    [Fact]
    public async Task Upsert_Throws_WhenGraphUserNotFound()
    {
        var graphId = "missing-graph";
        var options = InMemoryOptions(nameof(Upsert_Throws_WhenGraphUserNotFound));
        var graph = new Mock<IGraphUserService>();
        graph.Setup(x => x.GetUserByIdAsync(graphId, It.IsAny<CancellationToken>()));

        using var db = new AimsDbContext(options);
        db.Database.EnsureCreated();
        var svc = new AdminUserUpsertService(db, graph.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertAdminUserAsync(graphId, null, null, CancellationToken.None));
    }

  
    private sealed class ThrowOnceOnSaveAimsDbContext : AimsDbContext
    {
        private bool _throwOnce;

        public ThrowOnceOnSaveAimsDbContext(DbContextOptions<AimsDbContext> options, bool throwOnce)
            : base(options)
        {
            _throwOnce = throwOnce;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_throwOnce)
            {
                _throwOnce = false;
                // Simulate a unique constraint violation with a simple exception
                throw new DbUpdateException("Simulated unique violation", new Exception("Unique constraint violation"));
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }


}
