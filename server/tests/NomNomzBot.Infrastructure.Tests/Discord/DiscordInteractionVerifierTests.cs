// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Infrastructure.Discord.Interactions;
using NSec.Cryptography;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Proves the Ed25519 verification really implements Discord's scheme with a REAL keypair generated in-test:
/// the signature is over <c>timestamp + raw body bytes</c>; a valid signature verifies; a tampered body, a
/// wrong key, a shifted timestamp, or malformed header material all fail closed (false, never a throw); and an
/// unconfigured / malformed <c>Discord:PublicKey</c> reports not-configured.
/// </summary>
public sealed class DiscordInteractionVerifierTests
{
    private const string Timestamp = "1751673600";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"type":1}""");

    [Fact]
    public void Verify_ValidSignatureOverTimestampPlusBody_ReturnsTrue()
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        DiscordInteractionVerifier verifier = VerifierFor(key);

        string signature = Sign(key, Timestamp, Body);

        verifier.IsConfigured.Should().BeTrue();
        verifier.Verify(signature, Timestamp, Body).Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsFalse()
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        DiscordInteractionVerifier verifier = VerifierFor(key);

        string signature = Sign(key, Timestamp, Body);
        byte[] tampered = Encoding.UTF8.GetBytes("""{"type":3}""");

        verifier.Verify(signature, Timestamp, tampered).Should().BeFalse();
    }

    [Fact]
    public void Verify_DifferentTimestampThanSigned_ReturnsFalse()
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        DiscordInteractionVerifier verifier = VerifierFor(key);

        string signature = Sign(key, Timestamp, Body);

        // The timestamp is part of the signed message — replaying the signature under another fails.
        verifier.Verify(signature, "1751673601", Body).Should().BeFalse();
    }

    [Fact]
    public void Verify_SignedByAnotherKey_ReturnsFalse()
    {
        using Key configured = Key.Create(SignatureAlgorithm.Ed25519);
        using Key attacker = Key.Create(SignatureAlgorithm.Ed25519);
        DiscordInteractionVerifier verifier = VerifierFor(configured);

        string signature = Sign(attacker, Timestamp, Body);

        verifier.Verify(signature, Timestamp, Body).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-hex-at-all")]
    [InlineData("abcd")] // valid hex, wrong length
    public void Verify_MalformedSignatureMaterial_ReturnsFalse_NeverThrows(string? signature)
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        DiscordInteractionVerifier verifier = VerifierFor(key);

        verifier.Verify(signature, Timestamp, Body).Should().BeFalse();
    }

    [Fact]
    public void Verify_MissingTimestamp_ReturnsFalse()
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        DiscordInteractionVerifier verifier = VerifierFor(key);

        string signature = Sign(key, Timestamp, Body);

        verifier.Verify(signature, null, Body).Should().BeFalse();
        verifier.Verify(signature, "", Body).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zz-not-hex")]
    [InlineData("abcd")] // valid hex, not 32 bytes
    public void UnconfiguredOrMalformedPublicKey_ReportsNotConfigured_AndVerifiesFalse(
        string? publicKey
    )
    {
        DiscordInteractionVerifier verifier = new(ConfigWith(publicKey));

        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        string signature = Sign(key, Timestamp, Body);

        verifier.IsConfigured.Should().BeFalse();
        verifier.Verify(signature, Timestamp, Body).Should().BeFalse();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DiscordInteractionVerifier VerifierFor(Key key) =>
        new(
            ConfigWith(
                Convert
                    .ToHexString(key.PublicKey.Export(KeyBlobFormat.RawPublicKey))
                    .ToLowerInvariant()
            )
        );

    private static IConfiguration ConfigWith(string? publicKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Discord:PublicKey"] = publicKey }
            )
            .Build();

    private static string Sign(Key key, string timestamp, byte[] body)
    {
        byte[] timestampBytes = Encoding.UTF8.GetBytes(timestamp);
        byte[] message = new byte[timestampBytes.Length + body.Length];
        timestampBytes.CopyTo(message, 0);
        body.CopyTo(message, timestampBytes.Length);
        return Convert
            .ToHexString(SignatureAlgorithm.Ed25519.Sign(key, message))
            .ToLowerInvariant();
    }
}
