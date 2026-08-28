using System.Linq;
using AttendanceAgent.Data;
using Xunit;

namespace AttendanceAgent.Tests;

public class AgentDbContextTests
{
    [Fact]
    public void Settings_RoundTrips()
    {
        using var db = TestDb.CreateInMemory();

        db.Settings.Add(new AgentSettings { BackendBaseUrl = "https://api.test/", StationApiKey = "secret" });
        db.SaveChanges();

        var loaded = db.Settings.Single();
        Assert.Equal("https://api.test/", loaded.BackendBaseUrl);
    }
}
