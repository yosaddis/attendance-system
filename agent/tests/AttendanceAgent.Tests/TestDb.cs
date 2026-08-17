using AttendanceAgent.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Tests;

public static class TestDb
{
    public static AgentDbContext CreateInMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AgentDbContext>().UseSqlite(connection).Options;
        var db = new AgentDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
