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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Contracts.Discord;
using NSec.Cryptography;

namespace NomNomzBot.Infrastructure.Discord.Interactions;

/// <summary>
/// Ed25519 verification of Discord interaction deliveries (docs.discord.com "Preparing for Interactions"):
/// the signed message is <c>X-Signature-Timestamp + raw body bytes</c>, the signature is the 64-byte hex in
/// <c>X-Signature-Ed25519</c>, the key is the application's 32-byte hex public key from the Developer Portal
/// (<c>Discord:PublicKey</c>). .NET 10 ships no Ed25519 signature primitive, so this uses the libsodium-backed
/// NSec — the same construction Discord's own reference implementations verify with. Fails closed: any
/// malformed input or unconfigured key verifies false, never throws.
/// </summary>
public sealed class DiscordInteractionVerifier : IDiscordInteractionVerifier
{
    private const int PublicKeyBytes = 32;
    private const int SignatureBytes = 64;

    private readonly IConfiguration _configuration;

    public DiscordInteractionVerifier(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsConfigured => TryImportPublicKey(out _);

    public bool Verify(string? signatureHex, string? timestamp, ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrEmpty(signatureHex) || string.IsNullOrEmpty(timestamp))
            return false;
        if (!TryImportPublicKey(out PublicKey? publicKey))
            return false;
        if (!TryFromHex(signatureHex, SignatureBytes, out byte[]? signature))
            return false;

        // The signed message is the exact concatenation timestamp-bytes + raw-body-bytes.
        byte[] timestampBytes = Encoding.UTF8.GetBytes(timestamp);
        byte[] message = new byte[timestampBytes.Length + body.Length];
        timestampBytes.CopyTo(message, 0);
        body.CopyTo(message.AsSpan(timestampBytes.Length));

        return SignatureAlgorithm.Ed25519.Verify(publicKey!, message, signature!);
    }

    private bool TryImportPublicKey(out PublicKey? publicKey)
    {
        publicKey = null;
        string? configured = _configuration["Discord:PublicKey"];
        if (string.IsNullOrWhiteSpace(configured))
            return false;
        if (!TryFromHex(configured.Trim(), PublicKeyBytes, out byte[]? keyBytes))
            return false;
        return PublicKey.TryImport(
            SignatureAlgorithm.Ed25519,
            keyBytes,
            KeyBlobFormat.RawPublicKey,
            out publicKey
        );
    }

    private static bool TryFromHex(string hex, int expectedLength, out byte[]? bytes)
    {
        bytes = null;
        if (hex.Length != expectedLength * 2)
            return false;
        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
