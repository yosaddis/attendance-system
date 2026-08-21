namespace AttendanceAgent.Devices;

public class FakeFingerprintEnroller : IFingerprintEnroller
{
    public byte[] MergedResult { get; set; } = Array.Empty<byte>();
    public bool ThrowOnMerge { get; set; }
    public List<byte[]>? LastCaptures { get; private set; }

    public byte[] MergeCaptures(IReadOnlyList<byte[]> rawCaptures)
    {
        LastCaptures = rawCaptures.ToList();
        if (ThrowOnMerge) throw new InvalidOperationException("Merge failed");
        return MergedResult;
    }
}
