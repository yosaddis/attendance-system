using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Data;

public class AgentDbContext : DbContext
{
    public AgentDbContext(DbContextOptions<AgentDbContext> options) : base(options) { }

    public DbSet<AgentSettings> Settings => Set<AgentSettings>();
    public DbSet<CachedEmployee> CachedEmployees => Set<CachedEmployee>();
    public DbSet<CachedTemplate> CachedTemplates => Set<CachedTemplate>();
    public DbSet<QueuedPunch> QueuedPunches => Set<QueuedPunch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentSettings>().HasKey(s => s.Id);
        modelBuilder.Entity<CachedEmployee>(e =>
        {
            e.HasKey(c => c.EmployeeId);
            e.HasIndex(c => c.EmployeeCode).IsUnique();
        });
        modelBuilder.Entity<CachedTemplate>().HasKey(t => t.EmployeeId);
        modelBuilder.Entity<QueuedPunch>().HasKey(p => p.Id);
    }
}
