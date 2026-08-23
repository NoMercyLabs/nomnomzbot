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

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// Proves the build actually stamps a version onto the running API assembly (S111d) so
/// <c>/health/version</c> reports something an operator can use to verify what is deployed, rather
/// than the unstamped SDK default. Mirrors the exact resolution order used by the endpoint
/// (<c>Program.cs</c>: informational version, else assembly version, else "unknown") and reads it via
/// reflection from the assembly under test — the same assembly the running host serves the endpoint
/// from — so this fails if the <c>Directory.Build.props</c> &lt;Version&gt; stamp is ever removed.
/// </summary>
public sealed class HealthVersionEndpointTests
{
    [Fact]
    public void RunningAssembly_ReportsStampedVersion_NotTheUnstampedFallback()
    {
        Assembly apiAssembly = typeof(Program).Assembly;

        string version =
            apiAssembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? apiAssembly.GetName().Version?.ToString()
            ?? "unknown";

        version
            .Should()
            .NotBe(
                "1.0.0.0",
                "the API must stamp a real build version (Directory.Build.props <Version>) so an "
                    + "operator can verify what is deployed via /health/version — the unstamped SDK "
                    + "default is not a usable answer"
            );
        version.Should().NotBe("unknown");
        version
            .Should()
            .StartWith(
                "0.1.0",
                "must resolve to the version stamped in Directory.Build.props (a source-link commit "
                    + "suffix may follow it)"
            );
    }
}
