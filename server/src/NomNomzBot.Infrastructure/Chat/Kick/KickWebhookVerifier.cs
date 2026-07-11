// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Kick;

namespace NomNomzBot.Infrastructure.Chat.Kick;

/// <summary>
/// <see cref="IKickWebhookVerifier"/> over Kick's published RSA key: verifies
/// <c>SHA-256/PKCS#1 v1.5</c> over <c>{messageId}.{timestamp}.{rawBody}</c>. The PEM key from
/// <c>GET /public/v1/public-key</c> is cached for a day (it is effectively static); a verification MISS
/// with a cached key triggers ONE forced refetch before failing, so a Kick key rotation self-heals on
/// the first delivery after it instead of dropping webhooks until the cache expires. Registered as a
/// singleton — the cache and its lock are process-wide.
/// </summary>
public sealed class KickWebhookVerifier : IKickWebhookVerifier
{
    private const string PublicKeyEndpoint = "https://api.kick.com/public/v1/public-key";
    private static readonly TimeSpan KeyCacheTtl = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly ILogger<KickWebhookVerifier> _logger;
    private readonly SemaphoreSlim _keyLock = new(1, 1);

    private string? _cachedPem;
    private DateTime _cachedAtUtc = DateTime.MinValue;

    public KickWebhookVerifier(
        IHttpClientFactory httpClientFactory,
        TimeProvider clock,
        ILogger<KickWebhookVerifier> logger
    )
    {
        _http = httpClientFactory.CreateClient("kick");
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> VerifyAsync(
        string messageId,
        string timestamp,
        string rawBody,
        string signatureBase64,
        CancellationToken cancellationToken = default
    )
    {
        if (
            string.IsNullOrEmpty(messageId)
            || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(signatureBase64)
        )
            return false;

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] signedPayload = Encoding.UTF8.GetBytes($"{messageId}.{timestamp}.{rawBody}");

        string? pem = await GetKeyAsync(forceRefresh: false, cancellationToken);
        if (pem is not null && VerifyWithKey(pem, signedPayload, signature))
            return true;

        // A miss with a cached key may be a rotated key — refetch once, then fail closed.
        string? fresh = await GetKeyAsync(forceRefresh: true, cancellationToken);
        return fresh is not null && fresh != pem && VerifyWithKey(fresh, signedPayload, signature);
    }

    private static bool VerifyWithKey(string pem, byte[] signedPayload, byte[] signature)
    {
        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return rsa.VerifyData(
                signedPayload,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private async Task<string?> GetKeyAsync(bool forceRefresh, CancellationToken ct)
    {
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        if (!forceRefresh && _cachedPem is not null && now - _cachedAtUtc < KeyCacheTtl)
            return _cachedPem;

        await _keyLock.WaitAsync(ct);
        try
        {
            now = _clock.GetUtcNow().UtcDateTime;
            if (!forceRefresh && _cachedPem is not null && now - _cachedAtUtc < KeyCacheTtl)
                return _cachedPem;

            PublicKeyResponse? body = await _http.GetFromJsonAsync<PublicKeyResponse>(
                PublicKeyEndpoint,
                ct
            );
            string? pem = body?.Data?.PublicKey;
            if (string.IsNullOrWhiteSpace(pem))
            {
                _logger.LogWarning("Kick public-key endpoint returned no key");
                return _cachedPem; // keep whatever we had — fail closed happens at verify.
            }

            _cachedPem = pem;
            _cachedAtUtc = now;
            return pem;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to fetch the Kick webhook public key");
            return _cachedPem;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    private sealed class PublicKeyResponse
    {
        [JsonPropertyName("data")]
        public PublicKeyData? Data { get; set; }
    }

    private sealed class PublicKeyData
    {
        [JsonPropertyName("public_key")]
        public string? PublicKey { get; set; }
    }
}
