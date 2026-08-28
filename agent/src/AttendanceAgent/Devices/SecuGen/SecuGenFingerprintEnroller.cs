using SecuGen.SecuBSPPro.Windows;

namespace AttendanceAgent.Devices.SecuGen;

public class SecuGenFingerprintEnroller : IFingerprintEnroller
{
    public byte[] MergeCaptures(IReadOnlyList<byte[]> rawCaptures)
    {
        if (rawCaptures.Count == 0)
            throw new ArgumentException("At least one capture is required.", nameof(rawCaptures));

        // CreateTemplate compares two already-captured FIRs purely in software — it does not
        // need an open device, so this creates its own short-lived SecuBSPMx instance rather
        // than sharing one with SecuGenFingerprintDevice, mirroring
        // SecuGenFingerprintVerifier's existing pattern.
        using var secuBsp = new SecuBSPMx();

        // Unlike ZK's single DBMerge(t1, t2, t3) batch call, SecuGen folds incrementally: the
        // first capture's FIR becomes the running merged FIR directly, and each subsequent
        // capture is folded into it one at a time via CreateTemplate(nextFir, runningMergedFir,
        // payload) — confirmed by reading the vendor's own mainform.cs demo. The empty string is
        // the payload parameter; this system does not use vendor-side payload embedding.
        var mergedFir = SecuGenFirTextEncoding.ToFirText(rawCaptures[0]);
        for (var i = 1; i < rawCaptures.Count; i++)
        {
            var nextFir = SecuGenFirTextEncoding.ToFirText(rawCaptures[i]);
            var err = secuBsp.CreateTemplate(nextFir, mergedFir, "");
            if (err != BSPError.ERROR_NONE)
                throw new InvalidOperationException($"Fingerprint template merge failed (CreateTemplate: {err}).");
            mergedFir = secuBsp.FIRTextData;
        }

        return SecuGenFirTextEncoding.ToBytes(mergedFir);
    }
}
