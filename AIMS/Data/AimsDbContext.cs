using Microsoft.EntityFrameworkCore;

namespace AIMS.Data
{
    // Minimal context so Program.cs can register it.
    // We'll add DbSets and configuration in Subtask 2.
    public class AimsDbContext : DbContext
    {
        public AimsDbContext(DbContextOptions<AimsDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }
}