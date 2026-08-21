using libzkfpcsharp;

namespace AttendanceAgent.Devices.Zk;

public class ZkFingerprintEnroller : IFingerprintEnroller
{
    private const int MergedTemplateBufferSize = 2048;

    public byte[] MergeCaptures(IReadOnlyList<byte[]> rawCaptures)
    {
        if (rawCaptures.Count != 3)
            throw new ArgumentException($"ZKFinger DBMerge requires exactly 3 captures, got {rawCaptures.Count}.", nameof(rawCaptures));

        // Mirrors ZkFingerprintVerifier's independent init/cleanup pattern: DBMerge is pure
        // software (needs the algorithm library initialized and a DB handle) but does NOT need
        // an open device, and this runs after ZkFingerprintDevice has already been released.
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
            var merged = new byte[MergedTemplateBufferSize];
            var mergedLen = merged.Length;
            var err = zkfp2.DBMerge(dbHandle, rawCaptures[0], rawCaptures[1], rawCaptures[2], merged, ref mergedLen);
            if (err != zkfperrdef.ZKFP_ERR_OK)
                throw new InvalidOperationException($"Fingerprint template merge failed (DBMerge: {err}).");

            var result = new byte[mergedLen];
            Array.Copy(merged, result, mergedLen);
            return result;
        }
        finally
        {
            zkfp2.DBFree(dbHandle);
            zkfp2.Terminate();
        }
    }
}
