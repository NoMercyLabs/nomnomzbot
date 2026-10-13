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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the Edge TTS handshake token (Sec-MS-GEC) matches Microsoft's validation. Microsoft moved to
/// the v2 token on 2026-10-13 (a CS64 checksum and a seeded SHA-256, "&lt;16 hex&gt;-&lt;64 hex&gt;"); the
/// old plain SHA-256 token was refused more and more and live TTS failed. The expected values come from
/// rany2/edge-tts 7.3.1 drm.generate_sec_ms_gec_v2 (commit ba61d944).
/// </summary>
public sealed class EdgeTtsProviderSecMsGecTests
{
    private static EdgeTtsProvider CreateProvider(TimeProvider timeProvider) =>
        new(
            timeProvider,
            Substitute.For<IHttpClientFactory>(),
            NullLogger<EdgeTtsProvider>.Instance
        );

    [Theory]
    [InlineData(
        0L,
        "52BC7A17B50DFEC9-28C4394775C8751CD2F6859C030C06DB1489ACAB3B3A7651831FC26895177ED6"
    )]
    [InlineData(
        1_791_912_000L,
        "67F2FB7D7823B7BB-C877334B170BC85E95D6F18C3035773A6409C77C29EDAC229D6B7C5F5D4D62E7"
    )]
    [InlineData(
        1_791_912_299L,
        "67F2FB7D7823B7BB-C877334B170BC85E95D6F18C3035773A6409C77C29EDAC229D6B7C5F5D4D62E7"
    )]
    [InlineData(
        1_700_000_000L,
        "2CD23A2CA0B2595A-F3C846C370E05F9CC83ACC5F995C5F5D87A4D0F14CE8936A0F902E64099E38A2"
    )]
    public void GenerateSecMsGecToken_MatchesTheReferenceV2Token(long unixSeconds, string expected)
    {
        FakeTimeProvider time = new(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
        EdgeTtsProvider provider = CreateProvider(time);

        string actual = provider.GenerateSecMsGecToken();

        actual.Should().Be(expected);
    }

    [Fact]
    public void SecMsGecVersion_NamesTheV2Token()
    {
        EdgeTtsProvider.SecMsGecVersion.Should().StartWith("2-");
    }

    [Fact]
    public void GenerateSecMsGecToken_IsStableWithinTheSameFiveMinuteWindow()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-08-21T13:50:00Z"));
        EdgeTtsProvider provider = CreateProvider(time);

        string firstToken = provider.GenerateSecMsGecToken();
        time.SetUtcNow(DateTimeOffset.Parse("2026-08-21T13:54:59Z"));
        string secondToken = provider.GenerateSecMsGecToken();

        secondToken.Should().Be(firstToken);
    }

    [Fact]
    public void GenerateSecMsGecToken_ChangesAcrossAFiveMinuteBoundary()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-08-21T13:54:59Z"));
        EdgeTtsProvider provider = CreateProvider(time);

        string beforeBoundary = provider.GenerateSecMsGecToken();
        time.SetUtcNow(DateTimeOffset.Parse("2026-08-21T13:55:00Z"));
        string afterBoundary = provider.GenerateSecMsGecToken();

        afterBoundary.Should().NotBe(beforeBoundary);
    }
}
