using Microsoft.EntityFrameworkCore;
using YTR.Core.Models;

namespace YTR.Core.Data;

public class YtrDbContext : DbContext
{
    public DbSet<DownloadRecord> Downloads => Set<DownloadRecord>();

    public YtrDbContext(DbContextOptions<YtrDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DownloadRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Url).IsRequired();
            entity.Property(e => e.FilePath).IsRequired();
            entity.HasIndex(e => e.DownloadedAt);
            entity.HasIndex(e => e.Platform);
        });
    }
}
