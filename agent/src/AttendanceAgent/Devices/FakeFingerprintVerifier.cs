namespace AttendanceAgent.Devices;

public class FakeFingerprintVerifier : IFingerprintVerifier
{
    public bool AlwaysMatches { get; set; } = true;

    public bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate) => AlwaysMatches;
}
