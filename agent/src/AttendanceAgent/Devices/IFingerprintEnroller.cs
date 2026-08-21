namespace AttendanceAgent.Devices;

/// <summary>
/// Folds the raw samples IFingerprintDevice.CaptureForEnrollment produced into one final
/// enrollment template. Implementations decide internally whether that's a single batch call
/// (ZK4500's DBMerge) or an iterative fold (SecuGen's repeated CreateTemplate) — callers don't
/// need to know which.
/// </summary>
public interface IFingerprintEnroller
{
    byte[] MergeCaptures(IReadOnlyList<byte[]> rawCaptures);
}
