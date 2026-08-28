using AttendanceAgent.Devices.SecuGen;
using Xunit;

namespace AttendanceAgent.Tests;

public class SecuGenFirTextEncodingTests
{
    [Fact]
    public void ToBytes_ThenToFirText_RoundTripsExactly()
    {
        // A representative sample matching the shape of a real captured
        // FIRTextData value (confirmed non-base64 alphabet in real hardware
        // testing — this fixture deliberately includes characters outside
        // the base64 alphabet to prove the round-trip doesn't silently rely
        // on base64-safe input).
        const string sample = "AwAAABQAAABkAgAAAQASAAMAZAAAAAAAXQIAAEa8OT3MSx5vqMPZmIwp5ioh~!@#$%^&*()";

        var bytes = SecuGenFirTextEncoding.ToBytes(sample);
        var roundTripped = SecuGenFirTextEncoding.ToFirText(bytes);

        Assert.Equal(sample, roundTripped);
    }
}
