using Microsoft.EntityFrameworkCore;
using Upsanctionscreener.Models; // Points to your entity classes

namespace Upsanctionscreener.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<SanctionScanUser> SanctionScanUsers { get; set; }
        public DbSet<UpSanctionSetting> UpSanctionSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Fluent API configuration for uniqueness
            modelBuilder.Entity<SanctionScanUser>()
                .HasIndex(u => u.Email)
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }
    }
}