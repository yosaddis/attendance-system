using AttendanceApi.Entities;

namespace AttendanceApi.Services;

public record AttendanceAnalysisResult(
    DateTimeOffset? FirstIn,
    DateTimeOffset? LastOut,
    double? WorkedHours,
    bool HasShift,
    bool IsLate,
    int? LateMinutes,
    bool IsMissingCheckout,
    bool HasDoublePunch);

public static class AttendanceAnalysisService
{
    // Stand-in until Tenant grows a real timezone field — every day-boundary and time-of-day
    // comparison in the attendance analytics feature goes through this one constant, so swapping
    // it for a per-tenant lookup later is a one-place change.
    public static readonly TimeSpan DefaultTenantOffset = TimeSpan.FromHours(3);

    private static readonly TimeSpan DoublePunchWindow = TimeSpan.FromMinutes(5);

    public static AttendanceAnalysisResult Analyze(List<Punch> dayPunches, Shift? shift)
    {
        var sorted = dayPunches.OrderBy(p => p.Timestamp).ToList();

        DateTimeOffset? firstIn = sorted.FirstOrDefault(p => p.PunchType == PunchType.In)?.Timestamp;
        DateTimeOffset? lastOut = sorted.LastOrDefault(p => p.PunchType == PunchType.Out)?.Timestamp;

        var workedHours = shift?.PunchMode == PunchMode.FourPunch
            ? WorkedHoursFourPunch(sorted)
            : WorkedHoursTwoPunch(sorted);

        var isMissingCheckout = sorted.Count > 0 && sorted[^1].PunchType != PunchType.Out;
        var hasDoublePunch = HasDoublePunch(sorted);

        var hasShift = shift is not null;
        var isLate = false;
        int? lateMinutes = null;
        if (hasShift && firstIn is not null)
        {
            var localTimeOfDay = firstIn.Value.ToOffset(DefaultTenantOffset).TimeOfDay;
            var threshold = shift!.StartTime.ToTimeSpan() + TimeSpan.FromMinutes(shift.GraceMinutes);
            if (localTimeOfDay > threshold)
            {
                isLate = true;
                lateMinutes = (int)(localTimeOfDay - threshold).TotalMinutes;
            }
        }

        return new AttendanceAnalysisResult(firstIn, lastOut, workedHours, hasShift, isLate, lateMinutes, isMissingCheckout, hasDoublePunch);
    }

    private static double? WorkedHoursTwoPunch(List<Punch> sorted)
    {
        double total = 0;
        var hadCompletedPair = false;
        DateTimeOffset? openIn = null;

        foreach (var punch in sorted)
        {
            if (punch.PunchType == PunchType.In)
            {
                openIn ??= punch.Timestamp;
            }
            else if (punch.PunchType == PunchType.Out && openIn is not null)
            {
                total += (punch.Timestamp - openIn.Value).TotalHours;
                openIn = null;
                hadCompletedPair = true;
            }
        }

        return hadCompletedPair ? total : null;
    }

    private static double? WorkedHoursFourPunch(List<Punch> sorted)
    {
        var firstByType = new Dictionary<PunchType, DateTimeOffset>();
        foreach (var punch in sorted)
        {
            if (!firstByType.ContainsKey(punch.PunchType))
                firstByType[punch.PunchType] = punch.Timestamp;
        }

        if (!firstByType.TryGetValue(PunchType.In, out var inAt) ||
            !firstByType.TryGetValue(PunchType.BreakOut, out var breakOutAt) ||
            !firstByType.TryGetValue(PunchType.BreakIn, out var breakInAt) ||
            !firstByType.TryGetValue(PunchType.Out, out var outAt))
        {
            return null;
        }

        return (breakOutAt - inAt).TotalHours + (outAt - breakInAt).TotalHours;
    }

    private static bool HasDoublePunch(List<Punch> sorted)
    {
        for (var i = 0; i < sorted.Count; i++)
        {
            for (var j = i + 1; j < sorted.Count; j++)
            {
                if (sorted[j].Timestamp - sorted[i].Timestamp > DoublePunchWindow) break;
                if (sorted[i].PunchType == sorted[j].PunchType) return true;
            }
        }
        return false;
    }
}
