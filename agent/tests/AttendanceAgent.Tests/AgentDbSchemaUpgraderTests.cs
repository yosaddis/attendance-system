using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceAgent.Tests;

public class AgentDbSchemaUpgraderTests
{
    [Fact]
    public void EnsureCachedEmployeeShiftColumns_AddsMissingColumnsToAnExistingDatabase()
    {
        using var db = TestDb.CreateInMemory();

        // Simulate an "old" agent.db from before this feature: recreate CachedEmployees without
        // the 4 shift-time columns, then seed a row the way the old app would have.
        db.Database.ExecuteSqlRaw("DROP TABLE CachedEmployees;");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE CachedEmployees (
            EmployeeId TEXT NOT NULL CONSTRAINT PK_CachedEmployees PRIMARY KEY,
            EmployeeCode TEXT NOT NULL,
            Name TEXT NOT NULL,
            CachedAt TEXT NOT NULL);");
        db.Database.ExecuteSqlRaw(
            "INSERT INTO CachedEmployees (EmployeeId, EmployeeCode, Name, CachedAt) VALUES ('11111111-1111-1111-1111-111111111111', 'E001', 'Jane Doe', '2026-01-01T00:00:00Z');");

        AgentDbSchemaUpgrader.EnsureCachedEmployeeShiftColumns(db);

        var cached = db.CachedEmployees.Single();
        Assert.Equal("Jane Doe", cached.Name);
        Assert.Null(cached.ShiftStartTime);

        cached.ShiftStartTime = new TimeOnly(9, 0);
        db.SaveChanges();

        Assert.Equal(new TimeOnly(9, 0), db.CachedEmployees.Single().ShiftStartTime);
    }

    [Fact]
    public void EnsureCachedEmployeeShiftColumns_IsANoOpWhenColumnsAlreadyExist()
    {
        // A freshly-created database (via EnsureCreated) already has the columns from the current
        // model — running the upgrader against it must not throw (e.g. from a duplicate-column
        // ALTER TABLE) and must leave existing data intact.
        using var db = TestDb.CreateInMemory();
        db.CachedEmployees.Add(new CachedEmployee
        {
            EmployeeId = Guid.NewGuid(), EmployeeCode = "E001", Name = "Jane Doe",
            ShiftStartTime = new TimeOnly(9, 0), CachedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();

        AgentDbSchemaUpgrader.EnsureCachedEmployeeShiftColumns(db);

        Assert.Equal(new TimeOnly(9, 0), db.CachedEmployees.Single().ShiftStartTime);
    }
}
