namespace AttendanceAgent.Devices;

public static class DeviceCapture
{
    public static byte[] CaptureOnce(IFingerprintDevice device)
    {
        device.Acquire();
        try
        {
            return device.Capture();
        }
        finally
        {
            device.Release();
        }
    }
}
