namespace AttendanceAgent.Devices.SecuGen;

/// <summary>
/// Encodes/decodes the SecuGen SDK's <c>FIRTextData</c> string to and from
/// <see cref="byte[]" /> for use with <see cref="IFingerprintDevice" />/
/// <see cref="IFingerprintVerifier" />.
///
/// CONFIRMED against real hardware (SecuGen USB SDU03P/FDU03): FIRTextData is
/// NOT base64 (two real captures, 806 and 848 chars — the 848-char one is
/// already a multiple of 4, ruling out a padding issue — both failed
/// Convert.FromBase64String with an illegal-character error). It's an opaque
/// vendor text string; UTF8 byte encoding round-trips it exactly (verified:
/// encode -> decode -> identical string) and a real capture/enroll/VerifyMatch
/// cycle through this exact encoding produced IsMatched: true.
/// </summary>
internal static class SecuGenFirTextEncoding
{
    public static byte[] ToBytes(string firText) => System.Text.Encoding.UTF8.GetBytes(firText);

    public static string ToFirText(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes);
}
