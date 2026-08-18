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

        // ASSUMPTION, NOT YET VERIFIED AGAINST REAL HARDWARE: FIRTextData
        // is the SDK's "text-encoded FIR" form (SecuAPI_FIR_FORM_TEXTENCODE),
        // which for every SecuGen SDK generation observed in the vendor
        // samples is base64. Verify this empirically during hardware
        // bring-up (Step 6 below) — if FIRTextData turns out not to be
        // valid base64, this line is the one to fix.
        return Convert.FromBase64String(_secuBsp.FIRTextData);
    }

    public void Release()
    {
        _secuBsp.CloseDevice();
    }
}
