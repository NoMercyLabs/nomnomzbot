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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// AES-256-GCM field cipher. 96-bit random nonce per call, 128-bit (16-byte) authentication tag, and the
/// <see cref="CipherAad"/> bound as GCM associated data. The tag covers both the ciphertext and the AAD,
/// so any tampering or any change of context (tenant / provider / token-type / key-version) makes
/// authentication fail and decryption return a closed failure — never a silent or partial plaintext.
/// Stateless: the 32-byte DEK is supplied per call and never retained.
/// </summary>
public sealed class AesGcmFieldCipher : IFieldCipher
{
    private const int KeySizeBytes = 32; // AES-256
    private const int NonceSizeBytes = 12; // 96-bit GCM nonce (recommended)
    private const int TagSizeBytes = 16; // 128-bit GCM tag

    public Result<CipherPayload> Encrypt(
        ReadOnlySpan<byte> dataEncryptionKey,
        string plaintext,
        CipherAad aad
    )
    {
        if (dataEncryptionKey.Length != KeySizeBytes)
            return Result.Failure<CipherPayload>(
                "Data-encryption key must be exactly 32 bytes (AES-256).",
                "INVALID_KEY"
            );

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = new byte[NonceSizeBytes];
        byte[] cipherBytes = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagSizeBytes];
        byte[] associatedData = aad.ToBytes();

        try
        {
            RandomNumberGenerator.Fill(nonce);

            using AesGcm aes = new(dataEncryptionKey, TagSizeBytes);
            aes.Encrypt(nonce, plaintextBytes, cipherBytes, tag, associatedData);

            // Persist ciphertext‖tag as one blob; the tag is required to authenticate on decrypt.
            byte[] sealedBlob = new byte[cipherBytes.Length + TagSizeBytes];
            cipherBytes.CopyTo(sealedBlob, 0);
            tag.CopyTo(sealedBlob, cipherBytes.Length);

            return Result.Success(
                new CipherPayload(Convert.ToBase64String(sealedBlob), Convert.ToBase64String(nonce))
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public Result<string> Decrypt(
        ReadOnlySpan<byte> dataEncryptionKey,
        CipherPayload payload,
        CipherAad aad
    )
    {
        if (dataEncryptionKey.Length != KeySizeBytes)
            return Result.Failure<string>(
                "Data-encryption key must be exactly 32 bytes (AES-256).",
                "INVALID_KEY"
            );

        byte[] sealedBlob;
        byte[] nonce;
        try
        {
            sealedBlob = Convert.FromBase64String(payload.CipherText);
            nonce = Convert.FromBase64String(payload.Nonce);
        }
        catch (FormatException)
        {
            return Result.Failure<string>(
                "Ciphertext or nonce is not valid base64.",
                "DECRYPT_FAILED"
            );
        }

        if (nonce.Length != NonceSizeBytes || sealedBlob.Length < TagSizeBytes)
            return Result.Failure<string>(
                "Ciphertext or nonce length is invalid.",
                "DECRYPT_FAILED"
            );

        int cipherLength = sealedBlob.Length - TagSizeBytes;
        ReadOnlySpan<byte> cipherBytes = sealedBlob.AsSpan(0, cipherLength);
        ReadOnlySpan<byte> tag = sealedBlob.AsSpan(cipherLength, TagSizeBytes);
        byte[] plaintextBytes = new byte[cipherLength];
        byte[] associatedData = aad.ToBytes();

        try
        {
            using AesGcm aes = new(dataEncryptionKey, TagSizeBytes);
            aes.Decrypt(nonce, cipherBytes, tag, plaintextBytes, associatedData);
            return Result.Success(Encoding.UTF8.GetString(plaintextBytes));
        }
        catch (AuthenticationTagMismatchException)
        {
            // Tag / AAD mismatch or tampered ciphertext. Fail closed — never leak the buffer contents.
            return Result.Failure<string>(
                "Authentication failed: ciphertext was tampered with, or the AAD context does not match.",
                "DECRYPT_FAILED"
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }
}
