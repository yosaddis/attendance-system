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
            var score = zkfp2.DBMatch(dbHandle, capturedTemplate, enrolledTemplate);
            return score > 0;
        }
        finally
        {
            zkfp2.DBFree(dbHandle);
            zkfp2.Terminate();
        }
    }
}
