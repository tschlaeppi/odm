using System.Text;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;
using Odm.SharedKernel.Text;
using Xunit;

namespace Odm.Tests;

public class ComplianceTests
{
    private static readonly Guid V1 = Guid.NewGuid();
    private static readonly Guid V2 = Guid.NewGuid();

    [Theory]
    [InlineData(InstallStatus.Failed, ComplianceRules.Failed)]
    [InlineData(InstallStatus.Pending, ComplianceRules.Pending)]
    [InlineData(InstallStatus.Running, ComplianceRules.Running)]
    public void Unfinished_installations_deviate(InstallStatus status, string expected) =>
        Assert.Equal(expected, ComplianceRules.Classify(status, false, null, V1));

    [Fact]
    public void Installed_in_target_version_is_compliant() =>
        Assert.Null(ComplianceRules.Classify(InstallStatus.Installed, false, V1, V1));

    [Fact]
    public void Installed_in_other_version_deviates() =>
        Assert.Equal(ComplianceRules.VersionMismatch, ComplianceRules.Classify(InstallStatus.Installed, false, V1, V2));

    [Theory]
    [InlineData(InstallStatus.Removed, null)]
    [InlineData(InstallStatus.Failed, ComplianceRules.RemovalFailed)]
    [InlineData(InstallStatus.Pending, ComplianceRules.RemovalPending)]
    [InlineData(InstallStatus.Running, ComplianceRules.RemovalPending)]
    [InlineData(InstallStatus.Installed, ComplianceRules.RemovalPending)]
    public void Removals(InstallStatus status, string? expected) =>
        Assert.Equal(expected, ComplianceRules.Classify(status, true, V1, V1));

    [Fact]
    public void Csv_neutralises_formulas_and_quotes()
    {
        Assert.Equal("'=1+1", CsvFormat.Cell("=1+1"));
        Assert.Equal("\"a,b\"", CsvFormat.Cell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", CsvFormat.Cell("say \"hi\""));
        Assert.Equal("", CsvFormat.Cell(null));
    }

    [Fact]
    public void Csv_has_bom_header_and_rows()
    {
        var text = Encoding.UTF8.GetString(CsvFormat.Build(["A", "B"], [["x", null]]));
        Assert.Equal("﻿A,B\r\nx,\r\n", text);
    }
}
