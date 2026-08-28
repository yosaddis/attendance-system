namespace AttendanceAgent.Devices;

public interface IFingerprintDevice
{
    void Acquire();
    byte[] Capture();
    byte[] CaptureForEnrollment();
    void Release();
}
