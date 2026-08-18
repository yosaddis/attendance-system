using SecuGen.SecuBSPPro.Windows;

namespace AttendanceAgent.Devices.SecuGen;

public class SecuGenFingerprintVerifier : IFingerprintVerifier
{
    public bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate)
    {
        // VerifyMatch compares two already-captured FIR templates purely
        // in software — it does not need an open device, so this creates
        // its own short-lived SecuBSPMx instance rather than sharing one
        // with SecuGenFingerprintDevice.
        using var secuBsp = new SecuBSPMx();

        var capturedFir = Convert.ToBase64String(capturedTemplate);
        var enrolledFir = Convert.ToBase64String(enrolledTemplate);

        var err = secuBsp.VerifyMatch(capturedFir, enrolledFir);
        if (err != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Fingerprint match failed (VerifyMatch: {err}).");

        return secuBsp.IsMatched;
    }
}
