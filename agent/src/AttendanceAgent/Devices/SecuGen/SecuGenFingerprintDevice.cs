using SecuGen.SecuBSPPro.Windows;

namespace AttendanceAgent.Devices.SecuGen;

public class SecuGenFingerprintDevice : IFingerprintDevice
{
    private readonly SecuBSPMx _secuBsp = new();

    public void Acquire()
    {
        var enumErr = _secuBsp.EnumerateDevice();
        if (enumErr != BSPError.ERROR_NONE || _secuBsp.DeviceNum == 0)
            throw new InvalidOperationException($"No SecuGen device found (EnumerateDevice: {enumErr}).");

        _secuBsp.DeviceID = _secuBsp.GetDeviceID(0);

        var openErr = _secuBsp.OpenDevice();
        if (openErr != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Failed to open SecuGen device (OpenDevice: {openErr}).");

        // CONFIRMED against real hardware: the SDK's default DefaultTimeout
        // (10000ms) is too short for reliable capture in practice — a
        // Capture(FIRPurpose.VERIFY) call failed twice with
        // ERROR_CAPTURE_TIMEOUT even with a finger presented promptly.
        // Bumping to 15000ms made capture succeed reliably. This is a soft,
        // best-effort tuning — don't fail Acquire() if SetInitInfo itself
        // returns a non-ERROR_NONE code.
        var initInfo = new BSPInitInfo();
        _secuBsp.GetInitInfo(initInfo);
        initInfo.DefaultTimeout = 15000;
        _secuBsp.SetInitInfo(initInfo);
    }

    public byte[] Capture()
    {
        // Kiosk mode: no popup window, no on-screen fingerprint preview —
        // this station has no operator watching a capture dialog.
        _secuBsp.CaptureWindowOption.WindowStyle = (int)WindowStyle.INVISIBLE;
        _secuBsp.CaptureWindowOption.ShowFPImage = false;
        _secuBsp.CaptureWindowOption.FingerWindow = IntPtr.Zero;

        var err = _secuBsp.Capture(FIRPurpose.VERIFY);
        if (err != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Fingerprint capture failed (Capture: {err}).");

        return SecuGenFirTextEncoding.ToBytes(_secuBsp.FIRTextData);
    }

    public void Release()
    {
        _secuBsp.CloseDevice();
    }
}
