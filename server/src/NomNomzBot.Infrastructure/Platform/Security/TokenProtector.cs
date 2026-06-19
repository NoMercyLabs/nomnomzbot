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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Services;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// Envelope-encryption facade for stored OAuth secrets. Resolves a per-subject DEK (via
/// <see cref="ISubjectKeyService"/>), AES-256-GCM-seals the secret with an AAD bound to subject + provider +
/// field + key-version, and serializes the whole envelope into the single existing token column. A subject
/// id is mapped deterministically to a stable DEK identity, so the same subject always reuses its DEK and a
/// crypto-shred of that DEK renders all the subject's stored secrets unrecoverable.
/// </summary>
public sealed class TokenProtector : ITokenProtector
{
    private const string EnvelopeVersion = "v1";

    private readonly ISubjectKeyService _subjectKeys;
    private readonly ILogger<TokenProtector> _logger;

    public TokenProtector(ISubjectKeyService subjectKeys, ILogger<TokenProtector> logger)
    {
        _subjectKeys = subjectKeys;
        _logger = logger;
    }

    public async Task<string> ProtectAsync(
        string plaintext,
        TokenProtectionContext context,
        CancellationToken cancellationToken = default
    )
    {
        (Guid subjectUserId, string subjectIdHash) = DeriveSubjectIdentity(context);

        Result<Guid> keyId = await _subjectKeys.GetOrCreateSubjectKeyAsync(
            subjectUserId,
            subjectIdHash,
            cancellationToken
        );
        if (keyId.IsFailure)
            throw new InvalidOperationException(
                $"Failed to resolve subject key: {keyId.ErrorMessage}"
            );

        const string keyVersion = "1";
        CipherAad aad = BuildAad(context, keyVersion);

        Result<CipherPayload> sealedPayload = await _subjectKeys.ProtectAsync(
            keyId.Value,
            plaintext,
            aad,
            cancellationToken
        );
        if (sealedPayload.IsFailure)
            throw new InvalidOperationException(
                $"Failed to protect token: {sealedPayload.ErrorMessage}"
            );

        // Self-describing envelope so decrypt reconstructs the exact AAD without extra columns:
        // version | keyId | keyVersion | nonce | ciphertext
        return string.Join(
            '|',
            EnvelopeVersion,
            keyId.Value.ToString("N"),
            keyVersion,
            sealedPayload.Value.Nonce,
            sealedPayload.Value.CipherText
        );
    }

    public async Task<string?> TryUnprotectAsync(
        string? sealedEnvelope,
        TokenProtectionContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(sealedEnvelope))
            return null;

        string[] parts = sealedEnvelope.Split('|');
        if (parts.Length != 5 || parts[0] != EnvelopeVersion)
            return null;

        if (!Guid.TryParseExact(parts[1], "N", out Guid keyId))
            return null;

        string keyVersion = parts[2];
        CipherPayload payload = new(CipherText: parts[4], Nonce: parts[3]);
        CipherAad aad = BuildAad(context, keyVersion);

        Result<string> opened = await _subjectKeys.UnprotectAsync(
            keyId,
            payload,
            aad,
            cancellationToken
        );
        if (opened.IsFailure)
        {
            _logger.LogWarning(
                "Token unprotect failed for provider {Provider} field {Field}: {Code}",
                context.Provider,
                context.Field,
                opened.ErrorCode
            );
            return null;
        }

        return opened.Value;
    }

    private static CipherAad BuildAad(TokenProtectionContext context, string keyVersion) =>
        new(
            TenantId: context.SubjectId,
            Provider: context.Provider,
            TokenType: context.Field,
            KeyVersion: keyVersion
        );

    /// <summary>
    /// Maps a string subject + provider to a stable DEK identity: a deterministic name-based GUID and a
    /// hex hash, so the same subject always resolves to the same DEK across calls and restarts.
    /// </summary>
    private static (Guid SubjectUserId, string SubjectIdHash) DeriveSubjectIdentity(
        TokenProtectionContext context
    )
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{context.Provider}:{context.SubjectId}")
        );
        Guid subjectUserId = new(hash.AsSpan(0, 16));
        string subjectIdHash = Convert.ToHexStringLower(hash);
        return (subjectUserId, subjectIdHash);
    }
}
