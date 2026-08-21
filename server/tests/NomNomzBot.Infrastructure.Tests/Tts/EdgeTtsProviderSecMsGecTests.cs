// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the Edge TTS handshake token (Sec-MS-GEC) matches Microsoft's actual validation algorithm
/// (SHA256 of a 5-minute-floored Windows-epoch tick count concatenated with the trusted client token,
/// uppercase hex) — without this exact derivation the WebSocket handshake 403s, the provider swallows
/// that as an empty result, and every self-host channel silently loses TTS (the reported bug).
/// </summary>
public sealed class EdgeTtsProviderSecMsGecTests
{
    private const string TrustedToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const long WindowsEpochOffsetSeconds = 11_644_473_600L;

    private static EdgeTtsProvider CreateProvider(TimeProvider timeProvider) =>
        new(
            timeProvider,
            Substitute.For<IHttpClientFactory>(),
            NullLogger<EdgeTtsProvider>.Instance
        );

    [Fact]
    public void GenerateSecMsGecToken_MatchesReferenceAlgorithm()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-08-21T13:53:37Z"));
        EdgeTtsProvider provider = CreateProvider(time);

        string actual = provider.GenerateSecMsGecToken();

        string expected = ComputeExpectedToken(time.GetUtcNow());
        actual.Should().Be(expected);
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

    private static string ComputeExpectedToken(DateTimeOffset now)
    {
        long windowsEpochSeconds = now.ToUnixTimeSeconds() + WindowsEpochOffsetSeconds;
        long flooredToFiveMinutes = windowsEpochSeconds - (windowsEpochSeconds % 300);
        long hundredNanosecondTicks = flooredToFiveMinutes * 10_000_000L;

        string stringToHash = $"{hundredNanosecondTicks}{TrustedToken}";
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(stringToHash));
        return Convert.ToHexString(hash);
    }
}
