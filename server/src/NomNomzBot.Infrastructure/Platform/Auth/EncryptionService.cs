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
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Auth;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// IEncryptionService implementation using AES-256-CBC with a stable configured key.
/// The key is read from Encryption:Key (base64-encoded) and is deterministic across
/// container restarts — unlike the previous DataProtection implementation which generated
/// ephemeral keys, invalidating all stored tokens on every restart.
///
/// Format: Base64( IV[16 bytes] + CipherText )
/// </summary>
public sealed class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public EncryptionService(IOptions<EncryptionOptions> options)
    {
        // Derive a 32-byte AES-256 key from whatever the user configured, using SHA-256.
        // This handles keys that aren't exactly 32 bytes without crashing.
        byte[] raw = Convert.FromBase64String(options.Value.Key);
        _key = SHA256.HashData(raw);
    }

    public string Encrypt(string plainText)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);

        using Aes aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend the IV so we can extract it during decryption
        byte[] result = new byte[aes.IV.Length + cipherBytes.Length];
        aes.IV.CopyTo(result, 0);
        cipherBytes.CopyTo(result, aes.IV.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipherText);

        byte[] raw = Convert.FromBase64String(cipherText);

        using Aes aes = Aes.Create();
        aes.Key = _key;

        byte[] iv = raw[..16];
        byte[] cipher = raw[16..];

        aes.IV = iv;

        using ICryptoTransform decryptor = aes.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }

    public string? TryDecrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return null;

        try
        {
            return Decrypt(cipherText);
        }
        catch
        {
            return null;
        }
    }
}
