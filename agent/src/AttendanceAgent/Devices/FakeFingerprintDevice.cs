namespace AttendanceAgent.Devices;

public class FakeFingerprintDevice : IFingerprintDevice
{
    public bool IsAcquired { get; private set; }
    public byte[] NextCapture { get; set; } = Array.Empty<byte>();
    public bool ThrowOnCapture { get; set; }
    public List<string> CallLog { get; } = new();

    public void Acquire()
    {
        IsAcquired = true;
        CallLog.Add("Acquire");
    }

    public byte[] Capture()
    {
        CallLog.Add("Capture");
        if (ThrowOnCapture) throw new InvalidOperationException("No finger detected");
        return NextCapture;
    }

    public void Release()
    {
        IsAcquired = false;
        CallLog.Add("Release");
    }
}
