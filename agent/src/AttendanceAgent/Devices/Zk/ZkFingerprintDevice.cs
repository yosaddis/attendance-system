using libzkfpcsharp;

namespace AttendanceAgent.Devices.Zk;

public class ZkFingerprintDevice : IFingerprintDevice
{
    private const int ParamImageWidth = 1;
    private const int ParamImageHeight = 2;
    private const int TemplateBufferSize = 2048;
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);

    private IntPtr _devHandle = IntPtr.Zero;
    private byte[] _imageBuffer = Array.Empty<byte>();

    public void Acquire()
    {
        var initErr = zkfp2.Init();
        if (initErr != zkfperrdef.ZKFP_ERR_OK && initErr != zkfperrdef.ZKFP_ERR_ALREADY_INIT)
            throw new InvalidOperationException($"ZKFinger algorithm init failed (Init: {initErr}).");

        // Everything from here on runs inside try/catch because it happens AFTER
        // zkfp2.Init() succeeded and, below, after OpenDevice() handed us a live
        // device handle. DeviceCapture.CaptureOnce deliberately calls Acquire()
        // OUTSIDE its try/finally (Release() must not run if Acquire() never
        // succeeded), so if Acquire() throws part-way through, nothing else will
        // ever close the handle or terminate the algorithm library — this method
        // has to unwind its own partial state before letting the exception out.
        try
        {
            if (zkfp2.GetDeviceCount() == 0)
                throw new InvalidOperationException("No ZKFinger device found.");

            _devHandle = zkfp2.OpenDevice(0);
            if (_devHandle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to open ZKFinger device (OpenDevice).");

            // GetParameters' result is overloaded: it reports failure through the
            // return code for SOME parameter codes and through the returned VALUE
            // for others. Measured on a real ZK4500 (VID_1B55&PID_0840): codes
            // 7 and 101-105 all return ret=0 (success) with value=-5
            // (ZKFP_ERR_INVALID_PARAM), while codes 999/12345 return ret=-5 with
            // value=0. So neither channel alone is trustworthy — the only safe
            // check is on the dimensions themselves. Width/height on this device
            // read 288 x 365 (= 105120 bytes, matching parameter code 106, the
            // SDK's own image-size parameter).
            //
            // This previously read `new byte[Math.Max(1, width * height)]`, which
            // turned both failure shapes into a plausible-looking small positive
            // buffer (0 -> 1 byte; -5 x -5 -> 25 bytes) for a sensor that produces
            // 105120 bytes — i.e. a native buffer overwrite if the driver does not
            // respect the buffer's declared length. Failing loudly instead.
            var width = ReadIntParameter(_devHandle, ParamImageWidth);
            var height = ReadIntParameter(_devHandle, ParamImageHeight);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException(
                    $"ZKFinger device returned invalid image dimensions (width={width}, height={height}).");

            // Reuse the existing buffer when the size is unchanged. At 288x365 =
            // 105120 bytes this array is over the 85,000-byte Large Object Heap
            // threshold, and Acquire() runs once per punch on a kiosk process
            // meant to stay up for weeks — reallocating every time churned the LOH
            // for no reason. ZkFingerprintDevice is a DI singleton (see
            // HostComposition.ConfigureServices), so the cached buffer genuinely
            // survives between punches.
            var requiredSize = width * height;
            if (_imageBuffer.Length != requiredSize)
                _imageBuffer = new byte[requiredSize];
        }
        catch
        {
            if (_devHandle != IntPtr.Zero)
            {
                zkfp2.CloseDevice(_devHandle);
                _devHandle = IntPtr.Zero;
            }
            zkfp2.Terminate();
            throw;
        }
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

            // ZKFP_ERR_CAPTURE (-8) is the SDK's own "nothing to capture yet"
            // code — confirmed by reflecting over zkfperrdef, and observed as the
            // steady-state result of a no-finger AcquireFingerprint call on the
            // real ZK4500. It is the ONLY result worth polling on.
            //
            // Every other non-OK code is a real failure: -7 invalid device
            // handle, -23 not opened, -24 not initialized, -11 out of memory,
            // -12 busy, and so on. Retrying those for the full 15 seconds just
            // delayed an inevitable failure by 15s per punch and buried the
            // actual error code behind a generic timeout message. Fail
            // immediately with the code, matching SecuGenFingerprintDevice.
            if (lastErr != zkfperrdef.ZKFP_ERR_CAPTURE)
                throw new InvalidOperationException(
                    $"Fingerprint capture failed (AcquireFingerprint: {lastErr}).");

            // No sleep between polls: measured on a real ZK4500, a no-finger
            // AcquireFingerprint call blocks ~290-320ms on its own, so the native
            // call already paces this loop. The 200ms Thread.Sleep that used to
            // sit here roughly doubled the polling interval, which only added
            // latency between a finger touching the sensor and the punch being
            // recognized.
        } while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException($"Fingerprint capture timed out (last AcquireFingerprint result: {lastErr}).");
    }

    // Placeholder to satisfy IFingerprintDevice until the real ZK4500 enrollment path
    // (repeated AcquireFingerprint + zkfp2's DBMerge to fold 3 samples into one template) is
    // implemented and validated against real hardware. Deliberately not guessed at here: the
    // ZKFinger SDK's DBMatch/DBMerge calls carry a documented, unmitigated
    // AccessViolationException risk (see the "docs: document unmitigated AccessViolationException
    // risk in ZKFinger DBMatch" commit), so this needs real-hardware testing rather than an
    // untested implementation slipped in as a side effect of an interface change.
    public byte[] CaptureForEnrollment() =>
        throw new NotImplementedException("ZK4500 enrollment capture is not yet implemented.");

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
