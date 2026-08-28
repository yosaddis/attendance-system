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
        ConfigureKioskCaptureWindow();

        var err = _secuBsp.Capture(FIRPurpose.VERIFY);
        if (err != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Fingerprint capture failed (Capture: {err}).");

        return SecuGenFirTextEncoding.ToBytes(_secuBsp.FIRTextData);
    }

    public byte[] CaptureForEnrollment()
    {
        ConfigureKioskCaptureWindow();

        // Enroll() is a genuinely different vendor call from Capture(FIRPurpose.VERIFY) —
        // confirmed by reading the vendor's own mainform.cs demo, which uses Enroll() only for
        // its enrollment flow and Capture(FIRPurpose.VERIFY) only for its verify flow. The
        // empty string is the payload parameter (SecuGen's Enroll/CreateTemplate can embed an
        // arbitrary string into the resulting FIR) — this system tracks employee identity
        // entirely in its own backend, so no vendor-side payload is used.
        var err = _secuBsp.Enroll("");
        if (err != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Fingerprint enrollment capture failed (Enroll: {err}).");

        return SecuGenFirTextEncoding.ToBytes(_secuBsp.FIRTextData);
    }

    public void Release()
    {
        _secuBsp.CloseDevice();
    }

    // Kiosk mode: no popup window, no on-screen fingerprint preview — this station has no
    // operator watching a capture dialog. Shared by both Capture() and CaptureForEnrollment(),
    // which both need the exact same window suppression.
    private void ConfigureKioskCaptureWindow()
    {
        _secuBsp.CaptureWindowOption.WindowStyle = (int)WindowStyle.INVISIBLE;
        _secuBsp.CaptureWindowOption.ShowFPImage = false;
        _secuBsp.CaptureWindowOption.FingerWindow = IntPtr.Zero;
    }
}
