// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Hits Microsoft's real Edge Read Aloud WebSocket — the exact keyless default every self-host channel
/// falls back to (<see cref="EdgeTtsProviderVoiceCatalogTests"/> and the unit-level Sec-MS-GEC tests can
/// only prove the token math and framing logic in isolation; they cannot see a live handshake rejection or
/// a wire-format change on Microsoft's end, which is what silently broke TTS for every channel). Run this
/// directly (`dotnet test --filter EdgeTtsProviderLiveIntegrationTests`) whenever the "synthesis_failed"
/// rejection reappears, before touching the provider code again.
/// </summary>
public sealed class EdgeTtsProviderLiveIntegrationTests
{
    [Fact]
    public async Task SynthesizeAsync_AgainstTheRealMicrosoftEndpoint_ReturnsPlayableMp3Audio()
    {
        ServiceCollection services = new();
        services.AddHttpClient("edge-tts", client => client.Timeout = TimeSpan.FromSeconds(15));
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        EdgeTtsProvider provider = new(
            TimeProvider.System,
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            NullLogger<EdgeTtsProvider>.Instance
        );

        Domain.Tts.Interfaces.TtsSynthesisResult result = await provider.SynthesizeAsync(
            "This is a live integration check of the Edge TTS provider.",
            "en-US-AriaNeural"
        );

        result.AudioData.Should().NotBeEmpty();
        // MPEG frame sync (0xFFE0-0xFFFF) — proves the payload is real decodable audio, not a header
        // fragment or an empty/garbage buffer smuggled past a length check.
        result.AudioData[0].Should().Be(0xFF);
        (result.AudioData[1] & 0xE0).Should().Be(0xE0);
        result.DurationMs.Should().BePositive();
    }
}
