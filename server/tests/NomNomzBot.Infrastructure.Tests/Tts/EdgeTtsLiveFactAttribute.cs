// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
