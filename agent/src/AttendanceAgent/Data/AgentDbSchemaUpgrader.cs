using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Data;

// AgentDbContext uses Database.EnsureCreated() (no formal EF Core migrations), which only creates a
// brand-new agent.db — it is a no-op against an already-existing file, so a station upgraded to a
// newer build with new CachedEmployee columns would otherwise silently keep its old schema, and
// every query touching those columns would start throwing SqliteException. This adds just enough
// reconciliation to keep an existing agent.db usable after the model gains new nullable columns,
// without pulling in a full EF Core Migrations setup.
public static class AgentDbSchemaUpgrader
{
    private static readonly string[] CachedEmployeeShiftColumns =
    {
        "ShiftStartTime", "ShiftEndTime", "ShiftBreakStart", "ShiftBreakEnd",
    };

    public static void EnsureCachedEmployeeShiftColumns(AgentDbContext db)
    {
        db.Database.OpenConnection();
        var connection = db.Database.GetDbConnection();

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(CachedEmployees);";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(reader.GetOrdinal("name")));
            }
        }

        foreach (var column in CachedEmployeeShiftColumns)
        {
            if (!existingColumns.Contains(column))
            {
                db.Database.ExecuteSqlRaw($"ALTER TABLE CachedEmployees ADD COLUMN {column} TEXT NULL;");
            }
        }
    }
}
