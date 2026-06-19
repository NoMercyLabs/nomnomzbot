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
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Platform.Security;

/// <summary>
/// Behavioral security tests for the AES-256-GCM field cipher: round-trip fidelity, AAD
/// non-transplantability, and tamper detection. Each asserts a security guarantee that breaks if the
/// primitive regresses (e.g. AAD dropped from the tag, or a tampered tag silently accepted).
/// </summary>
public class AesGcmFieldCipherTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static CipherAad AadA() => new("tenant-A", "twitch", "access", "1");

    private static CipherAad AadB() => new("tenant-B", "spotify", "refresh", "2");

    // ─── 1. Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginalPlaintext()
    {
        AesGcmFieldCipher cipher = new();
        byte[] key = NewKey();
        const string plaintext = "oauth-access-token-abc123-✓-üñîçødé";

        Result<CipherPayload> sealed_ = cipher.Encrypt(key, plaintext, AadA());
        sealed_.IsSuccess.Should().BeTrue();

        Result<string> opened = cipher.Decrypt(key, sealed_.Value, AadA());

        opened.IsSuccess.Should().BeTrue();
        opened.Value.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesDistinctNonceAndCiphertextPerCall()
    {
        AesGcmFieldCipher cipher = new();
        byte[] key = NewKey();
        const string plaintext = "same-input";

        CipherPayload first = cipher.Encrypt(key, plaintext, AadA()).Value;
        CipherPayload second = cipher.Encrypt(key, plaintext, AadA()).Value;

        // Random 96-bit nonce per call ⇒ non-deterministic ciphertext (no ECB-style leakage),
        // yet both still decrypt to the same plaintext.
        first.Nonce.Should().NotBe(second.Nonce);
        first.CipherText.Should().NotBe(second.CipherText);
        cipher.Decrypt(key, first, AadA()).Value.Should().Be(plaintext);
        cipher.Decrypt(key, second, AadA()).Value.Should().Be(plaintext);
    }

    // ─── 2. AAD non-transplantability ──────────────────────────────────────────────

    [Fact]
    public void Decrypt_WithDifferentAad_FailsAndYieldsNoPlaintext()
    {
        AesGcmFieldCipher cipher = new();
        byte[] key = NewKey();
        const string plaintext = "bound-to-tenant-A";

        // Sealed under context A...
        CipherPayload sealed_ = cipher.Encrypt(key, plaintext, AadA()).Value;

        // ...attempt to open under context B (a different tenant/provider/token-type/key-version),
        // same key. The AAD is part of the authenticated tag, so this must fail closed.
        Result<string> opened = cipher.Decrypt(key, sealed_, AadB());

        opened.IsFailure.Should().BeTrue();
        opened.ErrorCode.Should().Be("DECRYPT_FAILED");
        Action read = () => _ = opened.Value;
        read.Should()
            .Throw<InvalidOperationException>("a failed Result must never surface plaintext");
    }

    [Fact]
    public void Decrypt_WithSingleAadFieldChanged_Fails()
    {
        AesGcmFieldCipher cipher = new();
        byte[] key = NewKey();
        CipherPayload sealed_ = cipher.Encrypt(key, "x", AadA()).Value;

        // Only the KeyVersion differs (1 → 2): still non-transplantable across key versions.
        CipherAad bumpedVersion = AadA() with
        {
            KeyVersion = "2",
        };
        cipher.Decrypt(key, sealed_, bumpedVersion).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Decrypt_WithWrongKey_Fails()
    {
        AesGcmFieldCipher cipher = new();
        CipherPayload sealed_ = cipher.Encrypt(NewKey(), "x", AadA()).Value;

        cipher.Decrypt(NewKey(), sealed_, AadA()).IsFailure.Should().BeTrue();
    }

    // ─── 3. Tamper detection ───────────────────────────────────────────────────────

    [Fact]
    public void Decrypt_WithFlippedCiphertextByte_FailsAuthentication()
    {
        AesGcmFieldCipher cipher = new();
        byte[] key = NewKey();
        CipherPayload sealed_ = cipher.Encrypt(key, "tamper-target-plaintext", AadA()).Value;

        // Flip one bit in the ciphertext+tag blob (first byte).
        byte[] blob = Convert.FromBase64String(sealed_.CipherText);
        blob[0] ^= 0x01;
        CipherPayload tampered = sealed_ with { CipherText = Convert.ToBase64String(blob) };

        Result<string> opened = cipher.Decrypt(key, tampered, AadA());

        opened.IsFailure.Should().BeTrue();
        opened.ErrorCode.Should().Be("DECRYPT_FAILED");
    }

    [Fact]
    public void Decrypt_WithFlippedTagByte_FailsAuthentication()
    {
        AesGcmFieldCipher cipher = new();
        byte[] key = NewKey();
        CipherPayload sealed_ = cipher.Encrypt(key, "tamper-target", AadA()).Value;

        // The 16-byte tag is appended to the ciphertext; flip its last byte.
        byte[] blob = Convert.FromBase64String(sealed_.CipherText);
        blob[^1] ^= 0x80;
        CipherPayload tampered = sealed_ with { CipherText = Convert.ToBase64String(blob) };

        cipher.Decrypt(key, tampered, AadA()).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Decrypt_WithTamperedNonce_Fails()
    {
        AesGcmFieldCipher cipher = new();
        byte[] key = NewKey();
        CipherPayload sealed_ = cipher.Encrypt(key, "value", AadA()).Value;

        byte[] nonce = Convert.FromBase64String(sealed_.Nonce);
        nonce[0] ^= 0xFF;
        CipherPayload tampered = sealed_ with { Nonce = Convert.ToBase64String(nonce) };

        cipher.Decrypt(key, tampered, AadA()).IsFailure.Should().BeTrue();
    }
}
