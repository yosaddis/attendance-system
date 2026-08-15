using AttendanceApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(t => t.Status).HasConversion<string>();
            e.Property(t => t.DeviceVendor).HasConversion<string>();
        });
    }
}
