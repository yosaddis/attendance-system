using libzkfpcsharp;

namespace AttendanceAgent.Devices.Zk;

public class ZkFingerprintVerifier : IFingerprintVerifier
{
    public bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate)
    {
        // DBMatch is pure software but, unlike SecuGen's VerifyMatch, still
        // needs the algorithm library initialized and a DB handle — it does
        // NOT need an open device. This runs independently of
        // ZkFingerprintDevice, which has already been released by the time
        // this is called (see DeviceCapture.CaptureOnce).
        var initErr = zkfp2.Init();
        if (initErr != zkfperrdef.ZKFP_ERR_OK && initErr != zkfperrdef.ZKFP_ERR_ALREADY_INIT)
            throw new InvalidOperationException($"ZKFinger algorithm init failed (Init: {initErr}).");

        var dbHandle = zkfp2.DBInit();
        if (dbHandle == IntPtr.Zero)
        {
            zkfp2.Terminate();
            throw new InvalidOperationException("ZKFinger DBInit failed.");
        }

        try
        {
            // DBMatch's return value is overloaded the same way GetParameters' is:
            // a positive number is a match score, 0 is a genuine non-match, and a
            // NEGATIVE number is an error code, not a score. Confirmed on real
            // hardware: DBMatch with an all-zero template pair returns -17
            // (ZKFP_ERR_FAIL).
            //
            // This used to be a bare `return score > 0`, which folded every
            // matcher error into "fingerprint did not match". A corrupted or
            // wrong-format stored template would then make every punch for that
            // employee fail with "Fingerprint did not match" — pointing the
            // investigation at the employee's finger instead of at the template,
            // with no signal anywhere that the matcher itself had failed. Throw on
            // negative so only score > 0 vs score == 0 decides match/no-match,
            // mirroring SecuGenFingerprintVerifier's throw on a non-ERROR_NONE
            // VerifyMatch result.
            var score = zkfp2.DBMatch(dbHandle, capturedTemplate, enrolledTemplate);
            if (score < 0)
                throw new InvalidOperationException($"Fingerprint match failed (DBMatch: {score}).");

            return score > 0;
        }
        finally
        {
            zkfp2.DBFree(dbHandle);
            zkfp2.Terminate();
        }
    }
}
