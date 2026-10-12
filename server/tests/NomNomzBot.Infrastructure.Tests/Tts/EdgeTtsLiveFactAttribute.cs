// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself on a GitHub Actions runner. Microsoft refuses the Edge read-aloud
/// handshake from datacenter networks: since 2026-10-12 every runner gets a 403 where a home network gets audio from
/// the same build. A connectivity probe cannot tell that block apart from the Sec-MS-GEC regression the live test
/// guards (both are a 403), so the gate is the runner itself; the test still runs in every local gate.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EdgeTtsLiveFactAttribute : FactAttribute
{
    public EdgeTtsLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Skip =
                "Microsoft rejects the Edge read-aloud handshake from GitHub runner networks (403). "
                + "Run it locally: dotnet test --filter EdgeTtsProviderLiveIntegrationTests.";
        }
    }
}
