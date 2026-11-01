using BreweryCrawl.Api.Features.Crawls;
using Microsoft.EntityFrameworkCore;

namespace BreweryCrawl.Api.Infrastructure.Persistence;

public sealed class CrawlDbContext(DbContextOptions<CrawlDbContext> options) : DbContext(options)
{
    public DbSet<Crawl> Crawls => Set<Crawl>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Crawl>(crawl =>
        {
            crawl.HasKey(c => c.Id);
            crawl.Property(c => c.Name).HasMaxLength(100);
            crawl.Property(c => c.City).HasMaxLength(100);
            crawl.Property(c => c.OwnerId).HasMaxLength(50);

            // EF Core adds "WHERE Version = <original>" to updates and throws DbUpdateConcurrencyException
            // if someone else changed the row in the meantime.
            crawl.Property(c => c.Version).IsConcurrencyToken();

            crawl.OwnsMany(c => c.Stops, stop =>
            {
                stop.ToTable("CrawlStops");
                stop.WithOwner().HasForeignKey("CrawlId");
                stop.HasKey("CrawlId", nameof(CrawlStop.BreweryId));
            });
        });
    }
}
