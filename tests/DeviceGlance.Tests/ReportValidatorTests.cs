using System.Text.Json;
using DeviceGlance.Core;

namespace DeviceGlance.Tests;

public class ReportValidatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static (bool Ok, DeviceGlance.Infrastructure.PushReport? Report, string? Error) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var ok = ReportValidator.TryParse(doc.RootElement, Now, out var report, out var error);
        return (ok, report, error);
    }

    [Fact]
    public void Valid_report_is_trimmed_and_rounded()
    {
        var (ok, report, _) = Parse("""{"device":"  iPad ","battery":14.6,"charging":true}""");
        Assert.True(ok);
        Assert.Equal("iPad", report!.Device);
        Assert.Equal(15, report.Battery);
        Assert.True(report.Charging);
        Assert.Equal(Now, report.ReceivedAt);
    }

    [Fact]
    public void Charging_is_optional()
    {
        var (ok, report, _) = Parse("""{"device":"iPad","battery":0}""");
        Assert.True(ok);
        Assert.Null(report!.Charging);
    }

    [Theory]
    [InlineData("""{"device":"iPad","battery":101}""", "battery invalid")]
    [InlineData("""{"device":"iPad","battery":-1}""", "battery invalid")]
    [InlineData("""{"device":"iPad","battery":"50"}""", "battery invalid")]
    [InlineData("""{"device":"iPad"}""", "battery invalid")]
    [InlineData("""{"battery":50}""", "device invalid")]
    [InlineData("""{"device":"   ","battery":50}""", "device invalid")]
    [InlineData("""{"device":5,"battery":50}""", "device invalid")]
    [InlineData("""{"device":"iPad","battery":50,"charging":"yes"}""", "charging invalid")]
    [InlineData("[]", "body invalid")]
    public void Invalid_report_names_the_field(string json, string expected)
    {
        var (ok, report, error) = Parse(json);
        Assert.False(ok);
        Assert.Null(report);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Device_name_longer_than_64_chars_is_rejected()
    {
        var (ok, _, error) = Parse($$"""{"device":"{{new string('x', 65)}}","battery":50}""");
        Assert.False(ok);
        Assert.Equal("device invalid", error);
    }
}
