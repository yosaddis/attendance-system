namespace AttendanceAgent.Devices;

public interface IFingerprintDevice
{
    void Acquire();
    byte[] Capture();
    void Release();
}
