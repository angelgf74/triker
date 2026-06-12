using Microsoft.EntityFrameworkCore;

namespace Triker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<GameSnapshot> GameSnapshots => Set<GameSnapshot>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<GameSnapshot>(e =>
        {
            e.ToTable("game_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.StateJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
        });
    }
}
