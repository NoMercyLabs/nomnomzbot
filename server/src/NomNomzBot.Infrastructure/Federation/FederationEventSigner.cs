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
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Domain.Federation.Entities;
using NomNomzBot.Domain.Federation.Enums;

namespace NomNomzBot.Infrastructure.Federation;

/// <summary>
/// Per-message federation signatures (federation-oidc.md §3.3). The signed body is the deterministic sorted-key
/// UTF-8 JSON of the envelope, so signatures are stable across serializer differences. In-box RSA (rsa-sha256,
/// PKCS#1) — zero third-party crypto. Verify fails closed: a non-rsa-sha256 algorithm, unknown/inactive/expired
/// key, or a bad signature all fail.
/// </summary>
public sealed class FederationEventSigner(
    IApplicationDbContext db,
    IFederationSigningKeyProvider keyProvider,
    TimeProvider clock
) : IFederationEventSigner
{
    public Task<Result<FederationSignature>> SignAsync(
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        Result<FederationSigningKey> key = keyProvider.GetActiveSigningKey();
        if (key.IsFailure)
            return Task.FromResult(
                Result.Failure<FederationSignature>(key.ErrorMessage, key.ErrorCode)
            );

        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(key.Value.PrivateKeyPem);
        byte[] signature = rsa.SignData(
            CanonicalBytes(envelope),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return Task.FromResult(
            Result.Success(
                new FederationSignature(
                    key.Value.KeyId,
                    FederationKeyAlgorithm.RsaSha256,
                    Convert.ToBase64String(signature)
                )
            )
        );
    }

    public async Task<Result> VerifyAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        FederationSignature signature,
        CancellationToken cancellationToken = default
    )
    {
        if (signature.Algorithm != FederationKeyAlgorithm.RsaSha256)
            return Result.Failure("Unsupported signature algorithm.", "algorithm_unsupported");

        FederationPeerKey? key = await db.FederationPeerKeys.FirstOrDefaultAsync(
            k => k.PeerId == peerId && k.KeyId == signature.KeyId,
            cancellationToken
        );
        if (key is null || !key.IsActive)
            return Result.Failure("Signing key unknown or inactive.", "key_unknown");
        if (key.Algorithm != FederationKeyAlgorithm.RsaSha256)
            return Result.Failure("Key algorithm unsupported.", "algorithm_unsupported");

        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (key.ValidFrom > now || (key.ValidTo is DateTime validTo && validTo < now))
            return Result.Failure("Signing key outside its validity window.", "key_unknown");

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signature.SignatureBase64);
        }
        catch (FormatException)
        {
            return Result.Failure("Malformed signature.", "signature_invalid");
        }

        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(key.PublicKey);
        bool valid = rsa.VerifyData(
            CanonicalBytes(envelope),
            signatureBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return valid
            ? Result.Success()
            : Result.Failure("Signature does not verify.", "signature_invalid");
    }

    /// <summary>Deterministic sorted-key UTF-8 JSON over the envelope's signable fields.</summary>
    private static byte[] CanonicalBytes(FederationEventEnvelope e)
    {
        SortedDictionary<string, string?> fields = new(StringComparer.Ordinal)
        {
            ["eventId"] = e.EventId.ToString(),
            ["federatedEventType"] = e.FederatedEventType,
            ["occurredAt"] = e.OccurredAt.ToUnixTimeMilliseconds().ToString(),
            ["originBroadcasterId"] = e.OriginBroadcasterId?.ToString(),
            ["originInstanceId"] = e.OriginInstanceId,
            ["payloadJson"] = e.PayloadJson,
            ["schemaVersion"] = e.SchemaVersion.ToString(),
            ["targetBroadcasterId"] = e.TargetBroadcasterId?.ToString(),
        };
        return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(fields));
    }
}

/// <summary>
/// Config-backed instance signing key (<c>Federation:SigningKeyId</c> + <c>Federation:SigningKeyPem</c>). A
/// documented seam: the OIDC issuer key vault supersedes this once the asymmetric issuer lands. Unconfigured =
/// SERVICE_UNAVAILABLE, so outbound signing safely no-ops until a key exists.
/// </summary>
public sealed class FederationSigningKeyProvider(
    Microsoft.Extensions.Configuration.IConfiguration configuration
) : IFederationSigningKeyProvider
{
    public Result<FederationSigningKey> GetActiveSigningKey()
    {
        string? keyId = configuration["Federation:SigningKeyId"];
        string? pem = configuration["Federation:SigningKeyPem"];
        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(pem))
            return Result.Failure<FederationSigningKey>(
                "Federation signing key is not configured.",
                "SERVICE_UNAVAILABLE"
            );
        return Result.Success(new FederationSigningKey(keyId, pem));
    }
}
