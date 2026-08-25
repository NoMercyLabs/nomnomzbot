// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;

namespace NomNomzBot.Api.Tests.HealthChecks;

/// <summary>
/// Caddy-took-us-offline fix: a degraded EventSub connection (a background integration, not a serving
/// prerequisite) must no longer pull this instance out of a reverse proxy's pool. Before the fix,
/// <c>EventSubReadinessHealthCheck</c> was registered with <c>tags: ["ready"]</c> in <c>Program.cs</c>, so
/// <c>/health/ready</c> (which filters on the <c>ready</c> tag) failed whenever EventSub degraded — even
/// though the instance could serve every unrelated request fine. This reads the actual <c>Program.cs</c>
/// registration text (the same technique <see cref="HealthDetailStatusCodeRegressionTests"/> uses) so the
/// test fails against the pre-fix source and passes once the <c>ready</c> tag is removed from the check.
/// </summary>
public sealed class ReadinessTagScopeSourceTests
{
    [Fact]
    public void EventSubCheck_IsNotTaggedReady()
    {
        string source = LoadProgramCsSource();
        string eventSubRegistration = ExtractCheckRegistration(source, "eventsub");

        eventSubRegistration
            .Should()
            .NotContain(
                "\"ready\"",
                "EventSub is a background integration — its degradation must be visible on /health "
                    + "but must never remove this instance from a reverse proxy's readiness pool"
            );
    }

    [Fact]
    public void DatabaseChecks_StayTaggedReady()
    {
        string source = LoadProgramCsSource();

        ExtractCheckRegistration(source, "sqlite").Should().Contain("\"ready\"");
        ExtractCheckRegistration(source, "postgresql").Should().Contain("\"ready\"");
        ExtractCheckRegistration(source, "pending-migrations").Should().Contain("\"ready\"");
    }

    [Fact]
    public void ShutdownDrainCheck_StaysTaggedReady()
    {
        string source = LoadProgramCsSource();

        ExtractCheckRegistration(source, "shutdown")
            .Should()
            .Contain(
                "\"ready\"",
                "the drain check must keep removing this instance from the pool during graceful shutdown"
            );
    }

    private static string ExtractCheckRegistration(string source, string checkName)
    {
        string marker = $"\"{checkName}\"";
        int nameIndex = source.IndexOf(marker, StringComparison.Ordinal);
        nameIndex
            .Should()
            .BeGreaterThan(0, $"the \"{checkName}\" check must be registered in Program.cs");

        int statementEnd = source.IndexOf(';', nameIndex);
        statementEnd.Should().BeGreaterThan(nameIndex);

        int statementStart = source.LastIndexOf(".AddCheck", nameIndex, StringComparison.Ordinal);
        if (statementStart < 0)
        {
            statementStart = source.LastIndexOf(
                "healthChecks",
                nameIndex,
                StringComparison.Ordinal
            );
        }
        statementStart.Should().BeGreaterThan(0);

        return source[statementStart..statementEnd];
    }

    private static string LoadProgramCsSource()
    {
        string assemblyLocation = Assembly.GetAssembly(typeof(Program))!.Location;
        DirectoryInfo? dir = new FileInfo(assemblyLocation).Directory;
        string? found = null;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "NomNomzBot.Api", "Program.cs");
            if (File.Exists(candidate))
            {
                found = candidate;
                break;
            }
            dir = dir.Parent;
        }

        found.Should().NotBeNull("must locate the repo's server/src/NomNomzBot.Api/Program.cs");
        return File.ReadAllText(found);
    }
}
