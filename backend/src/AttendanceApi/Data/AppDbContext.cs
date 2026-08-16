using AttendanceApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<FingerprintTemplate> FingerprintTemplates => Set<FingerprintTemplate>();
    public DbSet<Punch> Punches => Set<Punch>();

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

        modelBuilder.Entity<Station>(e =>
        {
            e.Property(s => s.DeviceVendor).HasConversion<string>();
            e.HasIndex(s => s.ApiKeyHash).IsUnique();
        });

        modelBuilder.Entity<Employee>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.EmployeeCode }).IsUnique();
        });

        modelBuilder.Entity<Shift>(e =>
        {
            e.Property(s => s.PunchMode).HasConversion<string>();
        });

        modelBuilder.Entity<FingerprintTemplate>(e =>
        {
            e.Property(t => t.Vendor).HasConversion<string>();
            e.HasIndex(t => t.EmployeeId).IsUnique();
        });

        modelBuilder.Entity<Punch>(e =>
        {
            e.Property(p => p.PunchType).HasConversion<string>();
            e.Property(p => p.Timestamp).HasConversion(
                v => v.UtcDateTime,
                v => new DateTimeOffset(v, TimeSpan.Zero));
            e.HasIndex(p => new { p.TenantId, p.Timestamp });
        });
    }
}
