// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// tts.md §3.2 — builds the channel's effective provider. Edge needs no key and returns the shared
/// adapter; azure/elevenlabs open the channel's <see cref="TtsConfig"/> BYOK cipher envelope through the
/// vault (<see cref="ISubjectKeyService.UnprotectAsync"/> under the row's <c>SubjectKeyId</c>, AAD-bound to
/// tenant + provider + <c>api_key</c> + key version — gdpr-crypto §3.4) and bind a fresh adapter to the
/// decrypted key. No persistence, no events, no cipher primitives of its own.
/// </summary>
public sealed class ByokTtsProviderFactory : IByokTtsProviderFactory
{
    private readonly IApplicationDbContext _db;
    private readonly ISubjectKeyService _subjectKeys;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEnumerable<ITtsProvider> _sharedProviders;

    public ByokTtsProviderFactory(
        IApplicationDbContext db,
        ISubjectKeyService subjectKeys,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        IEnumerable<ITtsProvider> sharedProviders
    )
    {
        _db = db;
        _subjectKeys = subjectKeys;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _sharedProviders = sharedProviders;
    }

    public async Task<Result<ITtsProvider>> CreateForChannelAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken ct = default
    )
    {
        if (provider == "edge")
        {
            ITtsProvider? edge = _sharedProviders.OfType<EdgeTtsProvider>().FirstOrDefault();
            return edge is not null
                ? Result.Success(edge)
                : Result.Failure<ITtsProvider>(
                    "The edge TTS provider is not registered.",
                    "SERVICE_UNAVAILABLE"
                );
        }

        if (provider is not ("azure" or "elevenlabs"))
            return Errors
                .ValidationFailed($"Unknown TTS provider '{provider}'.")
                .ToTyped<ITtsProvider>();

        TtsConfig? config = await _db.TtsConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );

        (string? cipher, string? nonce, int? keyVersion) = provider switch
        {
            "azure" => (
                config?.AzureApiKeyCipher,
                config?.AzureApiKeyNonce,
                config?.AzureKeyVersion
            ),
            _ => (
                config?.ElevenLabsApiKeyCipher,
                config?.ElevenLabsApiKeyNonce,
                config?.ElevenLabsKeyVersion
            ),
        };

        if (
            config?.SubjectKeyId is not Guid keyId
            || cipher is null
            || nonce is null
            || keyVersion is null
        )
            return Errors.NotFound<ITtsProvider>("BYOK TTS key", provider);

        Result<string> apiKey = await _subjectKeys.UnprotectAsync(
            keyId,
            new CipherPayload(CipherText: cipher, Nonce: nonce),
            new CipherAad(
                TenantId: broadcasterId.ToString(),
                Provider: provider,
                TokenType: "api_key",
                KeyVersion: keyVersion.Value.ToString()
            ),
            ct
        );
        if (apiKey.IsFailure)
            return Result.Failure<ITtsProvider>(apiKey.ErrorMessage!, apiKey.ErrorCode!);

        return provider switch
        {
            "azure" => Result.Success<ITtsProvider>(
                new AzureTtsProvider(
                    _httpClientFactory,
                    _loggerFactory.CreateLogger<AzureTtsProvider>(),
                    apiKey.Value,
                    config.AzureRegion ?? "westeurope"
                )
            ),
            _ => Result.Success<ITtsProvider>(
                new ElevenLabsTtsProvider(
                    _httpClientFactory,
                    _loggerFactory.CreateLogger<ElevenLabsTtsProvider>(),
                    apiKey.Value
                )
            ),
        };
    }
}
