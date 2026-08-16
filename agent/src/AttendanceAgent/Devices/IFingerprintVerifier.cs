namespace AttendanceAgent.Devices;

public interface IFingerprintVerifier
{
    bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate);
}
