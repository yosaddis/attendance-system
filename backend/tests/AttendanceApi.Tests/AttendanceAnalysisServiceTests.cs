using AttendanceApi.Entities;
using AttendanceApi.Services;
using Xunit;

namespace AttendanceApi.Tests;

public class AttendanceAnalysisServiceTests
{
    private static Punch P(PunchType type, int hour, int minute = 0, int day = 10) =>
        new()
        {
            Id = Guid.NewGuid(),
            PunchType = type,
            Timestamp = new DateTimeOffset(2026, 8, day, hour, minute, 0, TimeSpan.Zero),
        };

    private static Shift TwoPunchShift(TimeOnly start, int grace = 0) => new()
    {
        Name = "Day",
        StartTime = start,
        EndTime = new TimeOnly(17, 0),
        GraceMinutes = grace,
        PunchMode = PunchMode.TwoPunch,
    };

    private static Shift FourPunchShift(TimeOnly start, int grace = 0) => new()
    {
        Name = "Day",
        StartTime = start,
        EndTime = new TimeOnly(17, 0),
        GraceMinutes = grace,
        PunchMode = PunchMode.FourPunch,
    };

    [Fact]
    public void NoShift_NeverComputesLate()
    {
        // 09:00 UTC = 12:00 local (GMT+3) — would be "late" against an 08:00 shift, but there's no shift.
        var result = AttendanceAnalysisService.Analyze(new List<Punch> { P(PunchType.In, 9) }, null);

        Assert.False(result.HasShift);
        Assert.False(result.IsLate);
        Assert.Null(result.LateMinutes);
    }

    [Fact]
    public void WithShift_OnTimeWithinGrace_IsNotLate()
    {
        // Shift starts 05:00 local with 10 min grace. Punch at 06:00 UTC = 09:00 local... use a
        // shift start that makes the local arrival land inside grace.
        var shift = TwoPunchShift(new TimeOnly(9, 5), grace: 10);
        // 06:00 UTC + 3h offset = 09:00 local, which is before 09:05 + 10min grace (09:15).
        var result = AttendanceAnalysisService.Analyze(new List<Punch> { P(PunchType.In, 6) }, shift);

        Assert.True(result.HasShift);
        Assert.False(result.IsLate);
        Assert.Null(result.LateMinutes);
    }

    [Fact]
    public void WithShift_ArrivalPastGrace_IsLateWithMinutes()
    {
        // Shift starts 08:00 local, 5 min grace (threshold 08:05 local). Punch at 06:20 UTC = 09:20 local.
        var shift = TwoPunchShift(new TimeOnly(8, 0), grace: 5);
        var result = AttendanceAnalysisService.Analyze(new List<Punch> { P(PunchType.In, 6, 20) }, shift);

        Assert.True(result.IsLate);
        Assert.Equal(75, result.LateMinutes); // 09:20 local - 08:05 threshold = 75 minutes
    }

    [Fact]
    public void AbsentDay_NoPunchesAtAll_IsNotLateAndHasNoWorkedHours()
    {
        var shift = TwoPunchShift(new TimeOnly(8, 0));
        var result = AttendanceAnalysisService.Analyze(new List<Punch>(), shift);

        Assert.Null(result.FirstIn);
        Assert.Null(result.LastOut);
        Assert.Null(result.WorkedHours);
        Assert.False(result.IsLate);
        Assert.False(result.IsMissingCheckout);
    }

    [Fact]
    public void LastPunchIsIn_IsMissingCheckout()
    {
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9), P(PunchType.Out, 13), P(PunchType.In, 14) }, null);

        Assert.True(result.IsMissingCheckout);
    }

    [Fact]
    public void LastPunchIsOut_IsNotMissingCheckout()
    {
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9), P(PunchType.Out, 17) }, null);

        Assert.False(result.IsMissingCheckout);
    }

    [Fact]
    public void TwoPunchesOfSameTypeWithinFiveMinutes_IsDoublePunch()
    {
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9, 0), P(PunchType.In, 9, 3) }, null);

        Assert.True(result.HasDoublePunch);
    }

    [Fact]
    public void TwoPunchesOfSameTypeSixMinutesApart_IsNotDoublePunch()
    {
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9, 0), P(PunchType.In, 9, 6) }, null);

        Assert.False(result.HasDoublePunch);
    }

    [Fact]
    public void TwoPunchMode_SumsCompletedInOutPairs()
    {
        var shift = TwoPunchShift(new TimeOnly(8, 0));
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9), P(PunchType.Out, 17) }, shift);

        Assert.Equal(8.0, result.WorkedHours);
    }

    [Fact]
    public void TwoPunchMode_DuplicateInIgnoredForWorkedHours()
    {
        var shift = TwoPunchShift(new TimeOnly(8, 0));
        // Double-punched In at 09:03 must not reset the open "In" from 09:00.
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9, 0), P(PunchType.In, 9, 3), P(PunchType.Out, 17, 0) }, shift);

        Assert.Equal(8.0, result.WorkedHours);
        Assert.True(result.HasDoublePunch);
    }

    [Fact]
    public void FourPunchMode_AllFourPresent_SubtractsBreakDuration()
    {
        var shift = FourPunchShift(new TimeOnly(8, 0));
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch>
            {
                P(PunchType.In, 9), P(PunchType.BreakOut, 13), P(PunchType.BreakIn, 15), P(PunchType.Out, 18),
            }, shift);

        Assert.Equal(7.0, result.WorkedHours); // (13-9) + (18-15) = 4 + 3
    }

    [Fact]
    public void FourPunchMode_MissingBreakIn_WorkedHoursIsNull()
    {
        var shift = FourPunchShift(new TimeOnly(8, 0));
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9), P(PunchType.BreakOut, 13), P(PunchType.Out, 18) }, shift);

        Assert.Null(result.WorkedHours);
    }
}
