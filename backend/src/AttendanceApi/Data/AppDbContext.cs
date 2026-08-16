using AttendanceApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(t => t.Status).HasConversion<string>();
            e.Property(t => t.DeviceVendor).HasConversion<string>();
        });

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Role).HasConversion<string>();
            e.HasIndex(u => u.Email).IsUnique();
        });
    }
}
