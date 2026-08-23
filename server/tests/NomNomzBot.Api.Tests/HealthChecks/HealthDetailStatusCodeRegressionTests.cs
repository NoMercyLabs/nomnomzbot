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
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NomNomzBot.Api.Tests.HealthChecks;

/// <summary>
/// S116 scope guard: the detail <c>/health</c> endpoint's own status-code mapping must NOT change when
/// <c>/health/ready</c> is tightened (<see cref="ReadinessStatusCodeMap"/>) — operators still need a 200 with
/// a readable JSON body naming the degraded component, distinct from the readiness signal an orchestrator
/// acts on. Since <c>/health</c>'s mapping is an inline anonymous-object initializer in <c>Program.cs</c>
/// (not extracted, to keep this slice's footprint to the readiness path only), this reads the actual source
/// text of the <c>/health</c> <c>MapHealthChecks</c> call to prove the literal mapping still reads Degraded
/// as 200 — so an accidental future copy-paste of the tightened readiness map onto <c>/health</c> fails
/// this test.
/// </summary>
public sealed class HealthDetailStatusCodeRegressionTests
{
    [Fact]
    public void HealthEndpoint_SourceStillMapsDegradedTo200()
    {
        string programCsPath = LocateProgramCs();
        string source = File.ReadAllText(programCsPath);

        int healthBlockStart = source.IndexOf("\"/health\",", StringComparison.Ordinal);
        int readyBlockStart = source.IndexOf("\"/health/ready\"", StringComparison.Ordinal);
        healthBlockStart.Should().BeGreaterThan(0, "the /health MapHealthChecks call must exist");
        readyBlockStart.Should().BeGreaterThan(healthBlockStart);

        string healthBlock = source[healthBlockStart..readyBlockStart];

        healthBlock
            .Should()
            .Contain(
                "[HealthStatus.Degraded] = StatusCodes.Status200OK",
                "the detail /health endpoint must keep reporting Degraded as 200 so its JSON body stays reachable"
            );
        healthBlock
            .Should()
            .Contain(
                "[HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable",
                "unhealthy must still fail /health too"
            );
    }

    private static string LocateProgramCs()
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

        found
            .Should()
            .NotBeNull(
                "must locate the repo's server/src/NomNomzBot.Api/Program.cs from the test assembly location"
            );
        return found!;
    }
}
