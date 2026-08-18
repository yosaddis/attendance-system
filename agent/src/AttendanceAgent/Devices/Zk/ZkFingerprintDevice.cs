using libzkfpcsharp;

namespace AttendanceAgent.Devices.Zk;

public class ZkFingerprintDevice : IFingerprintDevice
{
    private const int ParamImageWidth = 1;
    private const int ParamImageHeight = 2;
    private const int TemplateBufferSize = 2048;
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private IntPtr _devHandle = IntPtr.Zero;
    private byte[] _imageBuffer = Array.Empty<byte>();

    public void Acquire()
    {
        var initErr = zkfp2.Init();
        if (initErr != zkfperrdef.ZKFP_ERR_OK && initErr != zkfperrdef.ZKFP_ERR_ALREADY_INIT)
            throw new InvalidOperationException($"ZKFinger algorithm init failed (Init: {initErr}).");

        if (zkfp2.GetDeviceCount() == 0)
            throw new InvalidOperationException("No ZKFinger device found.");

        _devHandle = zkfp2.OpenDevice(0);
        if (_devHandle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to open ZKFinger device (OpenDevice).");

        var width = ReadIntParameter(_devHandle, ParamImageWidth);
        var height = ReadIntParameter(_devHandle, ParamImageHeight);
        _imageBuffer = new byte[Math.Max(1, width * height)];
    }

    public byte[] Capture()
    {
        var template = new byte[TemplateBufferSize];
        var deadline = DateTime.UtcNow + CaptureTimeout;
        int lastErr;
        do
        {
            var size = TemplateBufferSize;
            lastErr = zkfp2.AcquireFingerprint(_devHandle, _imageBuffer, template, ref size);
            if (lastErr == zkfperrdef.ZKFP_ERR_OK)
            {
                var result = new byte[size];
                Array.Copy(template, result, size);
                return result;
            }

            Thread.Sleep(PollInterval);
        } while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException($"Fingerprint capture timed out (last AcquireFingerprint result: {lastErr}).");
    }

    public void Release()
    {
        if (_devHandle != IntPtr.Zero)
        {
            zkfp2.CloseDevice(_devHandle);
            _devHandle = IntPtr.Zero;
        }
        zkfp2.Terminate();
    }

    private static int ReadIntParameter(IntPtr devHandle, int code)
    {
        var buffer = new byte[4];
        var size = buffer.Length;
        zkfp2.GetParameters(devHandle, code, buffer, ref size);
        var value = 0;
        zkfp2.ByteArray2Int(buffer, ref value);
        return value;
    }
}
