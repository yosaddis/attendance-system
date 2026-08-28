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

        // Reverses SecuGenFingerprintDevice.Capture()'s UTF8 encoding — see
        // SecuGenFirTextEncoding for the real-hardware verification this is
        // based on.
        var capturedFir = SecuGenFirTextEncoding.ToFirText(capturedTemplate);
        var enrolledFir = SecuGenFirTextEncoding.ToFirText(enrolledTemplate);

        var err = secuBsp.VerifyMatch(capturedFir, enrolledFir);
        if (err != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Fingerprint match failed (VerifyMatch: {err}).");

        return secuBsp.IsMatched;
    }
}
