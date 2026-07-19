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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Per-channel TTS settings over the <see cref="TtsConfig"/> table (tts.md P.1) — one row per channel,
/// created on first write; a channel with no row reads as the binding new-channel defaults. Also owns the
/// voice catalog read and the per-viewer voice assignments. BYOK cipher columns are written by the key
/// vault flow, never through this surface.
/// </summary>
public class TtsConfigService : ITtsConfigService
{
    /// <summary>Bound into every BYOK cipher's AAD; bump only alongside a re-encryption pass.</summary>
    private const int ByokKeyVersion = 1;

    private readonly IApplicationDbContext _db;
    private readonly ITtsService _ttsService;
    private readonly IEventBus _eventBus;
    private readonly ISubjectKeyService _subjectKeys;

    public TtsConfigService(
        IApplicationDbContext db,
        ITtsService ttsService,
        IEventBus eventBus,
        ISubjectKeyService subjectKeys
    )
    {
        _db = db;
        _ttsService = ttsService;
        _eventBus = eventBus;
        _subjectKeys = subjectKeys;
    }

    public async Task<Result<TtsConfigDto>> GetConfigAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        TtsConfig? config = await _db.TtsConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            cancellationToken
        );
        // No row yet = the binding new-channel defaults; the row is created on first write, not on read.
        return Result.Success(ToDto(config ?? new TtsConfig()));
    }

    public async Task<Result<TtsConfigDto>> UpdateConfigAsync(
        Guid broadcasterId,
        UpdateTtsConfigDto request,
        CancellationToken cancellationToken = default
    )
    {
        TtsConfig? config = await _db.TtsConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            cancellationToken
        );
        if (config is null)
        {
            config = new TtsConfig { BroadcasterId = broadcasterId };
            _db.TtsConfigs.Add(config);
        }

        if (request.IsEnabled.HasValue)
            config.IsEnabled = request.IsEnabled.Value;
        if (request.Mode is not null)
            config.Mode = request.Mode;
        if (request.DefaultProvider is not null)
            config.DefaultProvider = request.DefaultProvider;
        if (request.DefaultVoiceId is not null)
            config.DefaultVoiceId = request.DefaultVoiceId;
        if (request.MaxCharacters.HasValue)
            config.MaxCharacters = request.MaxCharacters.Value;
        if (request.MinPermission is not null)
            config.MinPermission = request.MinPermission;
        if (request.SkipBotMessages.HasValue)
            config.SkipBotMessages = request.SkipBotMessages.Value;
        if (request.ReadUsernames.HasValue)
            config.ReadUsernames = request.ReadUsernames.Value;
        if (request.ProfanityCensorEnabled.HasValue)
            config.ProfanityCensorEnabled = request.ProfanityCensorEnabled.Value;
        if (request.ModApprovalRequired.HasValue)
            config.ModApprovalRequired = request.ModApprovalRequired.Value;
        if (request.MinBitsToTts.HasValue)
            config.MinBitsToTts = request.MinBitsToTts.Value == 0 ? null : request.MinBitsToTts;
        if (request.ViewerVoiceSelfServiceEnabled.HasValue)
            config.ViewerVoiceSelfServiceEnabled = request.ViewerVoiceSelfServiceEnabled.Value;

        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcasterId, cancellationToken);

        return Result.Success(ToDto(config));
    }

    public async Task<Result<TtsConfigDto>> SetByokKeyAsync(
        Guid broadcasterId,
        string provider,
        SetTtsByokKeyDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (provider is not ("azure" or "elevenlabs"))
            return Errors
                .ValidationFailed($"Unknown BYOK TTS provider '{provider}'.")
                .ToTyped<TtsConfigDto>();
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return Errors.ValidationFailed("An API key is required.").ToTyped<TtsConfigDto>();

        TtsConfig? config = await _db.TtsConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            cancellationToken
        );
        if (config is null)
        {
            config = new TtsConfig { BroadcasterId = broadcasterId };
            _db.TtsConfigs.Add(config);
        }

        // One DEK per channel wraps both provider keys; destroying it crypto-shreds them (gdpr-crypto §3.4).
        if (config.SubjectKeyId is null)
        {
            Result<Guid> keyId = await ResolveSubjectKeyAsync(broadcasterId, cancellationToken);
            if (keyId.IsFailure)
                return Result.Failure<TtsConfigDto>(keyId.ErrorMessage!, keyId.ErrorCode!);
            config.SubjectKeyId = keyId.Value;
        }

        Result<CipherPayload> sealedKey = await _subjectKeys.ProtectAsync(
            config.SubjectKeyId.Value,
            request.ApiKey,
            new CipherAad(
                TenantId: broadcasterId.ToString(),
                Provider: provider,
                TokenType: "api_key",
                KeyVersion: ByokKeyVersion.ToString()
            ),
            resourceTable: "TtsConfigs",
            resourceColumn: provider == "azure" ? "AzureApiKeyCipher" : "ElevenLabsApiKeyCipher",
            cancellationToken
        );
        if (sealedKey.IsFailure)
            return Result.Failure<TtsConfigDto>(sealedKey.ErrorMessage!, sealedKey.ErrorCode!);

        if (provider == "azure")
        {
            config.AzureApiKeyCipher = sealedKey.Value.CipherText;
            config.AzureApiKeyNonce = sealedKey.Value.Nonce;
            config.AzureKeyVersion = ByokKeyVersion;
            if (request.Region is not null)
                config.AzureRegion = request.Region;
        }
        else
        {
            config.ElevenLabsApiKeyCipher = sealedKey.Value.CipherText;
            config.ElevenLabsApiKeyNonce = sealedKey.Value.Nonce;
            config.ElevenLabsKeyVersion = ByokKeyVersion;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcasterId, cancellationToken);
        return Result.Success(ToDto(config));
    }

    public async Task<Result<TtsConfigDto>> ClearByokKeyAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        if (provider is not ("azure" or "elevenlabs"))
            return Errors
                .ValidationFailed($"Unknown BYOK TTS provider '{provider}'.")
                .ToTyped<TtsConfigDto>();

        TtsConfig? config = await _db.TtsConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            cancellationToken
        );
        if (config is null)
            return Errors.NotFound<TtsConfigDto>("BYOK TTS key", provider);

        if (provider == "azure")
        {
            config.AzureApiKeyCipher = null;
            config.AzureApiKeyNonce = null;
            config.AzureKeyVersion = null;
            config.AzureRegion = null;
        }
        else
        {
            config.ElevenLabsApiKeyCipher = null;
            config.ElevenLabsApiKeyNonce = null;
            config.ElevenLabsKeyVersion = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcasterId, cancellationToken);
        return Result.Success(ToDto(config));
    }

    /// <summary>The channel's TTS DEK identity, derived the same deterministic way the token vault does it.</summary>
    private async Task<Result<Guid>> ResolveSubjectKeyAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"tts:{broadcasterId}")
        );
        Guid subjectUserId = new(hash.AsSpan(0, 16));
        string subjectIdHash = Convert.ToHexStringLower(hash);
        return await _subjectKeys.GetOrCreateSubjectKeyAsync(
            subjectUserId,
            subjectIdHash,
            cancellationToken
        );
    }

    private async Task PublishConfigChangedAsync(Guid broadcasterId, CancellationToken ct) =>
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "tts-config",
                Action = "updated",
            },
            ct
        );

    public async Task<Result<PagedList<TtsVoiceDto>>> SearchVoicesAsync(
        TtsVoiceQuery query,
        CancellationToken cancellationToken = default
    )
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = Math.Clamp(query.PageSize, 1, 200);

        // Pre-sync: the catalogue table is empty → fall back to what the providers can enumerate live, filtered
        // and paged in memory (the catalogue sync fills the table so this branch stops firing).
        if (!await _db.TtsVoices.AnyAsync(cancellationToken))
            return Result.Success(
                await SearchProviderFallbackAsync(query, page, pageSize, cancellationToken)
            );

        IQueryable<TtsVoice> voices = _db.TtsVoices;

        if (!string.IsNullOrWhiteSpace(query.Locale))
        {
            string locale = query.Locale.Trim().ToLower();
            voices = voices.Where(v => v.Locale.ToLower() == locale);
        }
        if (!string.IsNullOrWhiteSpace(query.Gender))
        {
            string gender = query.Gender.Trim().ToLower();
            voices = voices.Where(v => v.Gender.ToLower() == gender);
        }
        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            string provider = query.Provider.Trim().ToLower();
            voices = voices.Where(v => v.Provider.ToLower() == provider);
        }
        if (!string.IsNullOrWhiteSpace(query.Accent))
        {
            string accent = query.Accent.Trim().ToLower();
            voices = voices.Where(v => v.Accent != null && v.Accent.ToLower() == accent);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            string q = query.Q.Trim().ToLower();
            // A viewer types "!voice british" or "!voice male" — free-text spans accent and gender too, not just
            // the name/description/tags, so the natural words land the voice they mean.
            voices = voices.Where(v =>
                v.Name.ToLower().Contains(q)
                || v.DisplayName.ToLower().Contains(q)
                || v.Gender.ToLower().Contains(q)
                || (v.Accent != null && v.Accent.ToLower().Contains(q))
                || (v.Description != null && v.Description.ToLower().Contains(q))
                || (v.TagsJson != null && v.TagsJson.ToLower().Contains(q))
            );
        }

        int total = await voices.CountAsync(cancellationToken);
        List<TtsVoice> pageRows = await voices
            .OrderBy(v => v.Provider)
            .ThenBy(v => v.Locale)
            .ThenBy(v => v.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        IReadOnlyList<TtsVoiceDto> dtos = pageRows.Select(ToDto).ToList();
        return Result.Success(new PagedList<TtsVoiceDto>(dtos, page, pageSize, total));
    }

    // Live-provider fallback used only while the catalogue table is empty (pre-sync). Enumerates, maps, filters
    // and pages entirely in memory — the provider lists are small and this path disappears once the sync runs.
    private async Task<PagedList<TtsVoiceDto>> SearchProviderFallbackAsync(
        TtsVoiceQuery query,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<TtsVoiceInfo> providerVoices = await _ttsService.GetAvailableVoicesAsync(
            cancellationToken
        );
        List<TtsVoiceDto> ordered = ApplyInMemoryFilter(providerVoices.Select(ToDto), query)
            .OrderBy(v => v.Provider)
            .ThenBy(v => v.Locale)
            .ThenBy(v => v.Name)
            .ToList();
        IReadOnlyList<TtsVoiceDto> pageItems = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return new PagedList<TtsVoiceDto>(pageItems, page, pageSize, ordered.Count);
    }

    private static IEnumerable<TtsVoiceDto> ApplyInMemoryFilter(
        IEnumerable<TtsVoiceDto> voices,
        TtsVoiceQuery query
    )
    {
        if (!string.IsNullOrWhiteSpace(query.Locale))
            voices = voices.Where(v =>
                string.Equals(v.Locale, query.Locale, StringComparison.OrdinalIgnoreCase)
            );
        if (!string.IsNullOrWhiteSpace(query.Gender))
            voices = voices.Where(v =>
                string.Equals(v.Gender, query.Gender, StringComparison.OrdinalIgnoreCase)
            );
        if (!string.IsNullOrWhiteSpace(query.Provider))
            voices = voices.Where(v =>
                string.Equals(v.Provider, query.Provider, StringComparison.OrdinalIgnoreCase)
            );
        if (!string.IsNullOrWhiteSpace(query.Accent))
            voices = voices.Where(v =>
                string.Equals(v.Accent, query.Accent, StringComparison.OrdinalIgnoreCase)
            );
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            string q = query.Q.Trim();
            voices = voices.Where(v =>
                v.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || v.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || v.Gender.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (v.Accent?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (v.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || v.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))
            );
        }
        return voices;
    }

    private static TtsVoiceDto ToDto(TtsVoice v) =>
        new(
            v.Id,
            v.Name,
            v.DisplayName,
            v.Locale,
            v.Gender,
            v.Provider,
            v.IsDefault,
            v.Accent,
            v.Age,
            ParseList(v.StylesJson),
            ParseList(v.TagsJson),
            v.Description,
            v.PreviewUrl
        );

    private static TtsVoiceDto ToDto(TtsVoiceInfo v) =>
        new(
            v.Id,
            v.Name,
            v.DisplayName,
            v.Locale,
            v.Gender,
            v.Provider,
            IsDefault: false,
            v.Accent,
            v.Age,
            v.Styles?.ToList() ?? [],
            v.Tags?.ToList() ?? [],
            v.Description,
            v.PreviewUrl
        );

    // Parses a stored JSON array column (StylesJson / TagsJson) into a list; null/blank/malformed → empty.
    private static IReadOnlyList<string> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonConvert.DeserializeObject<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // True when the catalogue contains the voice, or (pre-sync, empty catalogue) a provider can enumerate it.
    private async Task<bool> VoiceExistsAsync(string voiceId, CancellationToken cancellationToken)
    {
        if (await _db.TtsVoices.AnyAsync(v => v.Id == voiceId, cancellationToken))
            return true;
        if (await _db.TtsVoices.AnyAsync(cancellationToken))
            return false; // catalogue present but no match
        IReadOnlyList<TtsVoiceInfo> providerVoices = await _ttsService.GetAvailableVoicesAsync(
            cancellationToken
        );
        return providerVoices.Any(v => v.Id == voiceId);
    }

    public async Task<Result<TtsTestResultDto>> TestVoiceAsync(
        Guid broadcasterId,
        TtsTestRequestDto request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            TtsResult result = await _ttsService.SynthesizeAsync(
                request.Text,
                request.VoiceId,
                cancellationToken
            );
            string base64 = Convert.ToBase64String(result.AudioData);
            return Result.Success(
                new TtsTestResultDto(result.VoiceId, result.Provider, result.DurationMs, base64)
            );
        }
        catch (Exception)
        {
            return Errors.ExternalServiceUnavailable("TTS").ToTyped<TtsTestResultDto>();
        }
    }

    public async Task<Result<UserTtsVoiceDto>> GetUserVoiceAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        UserTtsVoice? assignment = await _db.UserTtsVoices.FirstOrDefaultAsync(
            v => v.BroadcasterId == broadcasterId && v.UserId == userId,
            cancellationToken
        );

        if (assignment is null)
            return Errors.NotFound<UserTtsVoiceDto>("TTS voice assignment", userId);

        return Result.Success(new UserTtsVoiceDto(assignment.UserId, assignment.VoiceId));
    }

    public async Task<Result<UserTtsVoiceDto>> SetUserVoiceAsync(
        Guid broadcasterId,
        string userId,
        SetUserVoiceDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Errors.ValidationFailed("A viewer id is required.").ToTyped<UserTtsVoiceDto>();

        // Truthful: only accept a voice the channel can actually synthesize — the same set the picker shows and
        // the dispatch resolver hands to the provider. A voice that could never play is rejected, not stored.
        if (!await VoiceExistsAsync(request.VoiceId, cancellationToken))
            return Errors.NotFound<UserTtsVoiceDto>("TTS voice", request.VoiceId);

        UserTtsVoice? existing = await _db.UserTtsVoices.FirstOrDefaultAsync(
            v => v.BroadcasterId == broadcasterId && v.UserId == userId,
            cancellationToken
        );

        if (existing is not null)
        {
            existing.VoiceId = request.VoiceId;
        }
        else
        {
            _db.UserTtsVoices.Add(
                new UserTtsVoice
                {
                    BroadcasterId = broadcasterId,
                    UserId = userId,
                    VoiceId = request.VoiceId,
                }
            );
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(new UserTtsVoiceDto(userId, request.VoiceId));
    }

    private const int MaxImportRows = 500;

    public async Task<Result<TtsVoiceImportResultDto>> ImportUserVoiceAssignmentsAsync(
        Guid broadcasterId,
        IReadOnlyList<TtsVoiceAssignmentRowDto> rows,
        CancellationToken cancellationToken = default
    )
    {
        if (rows.Count > MaxImportRows)
            return Errors
                .ValidationFailed(
                    $"A voice-assignment import is capped at {MaxImportRows} rows per request."
                )
                .ToTyped<TtsVoiceImportResultDto>();
        if (rows.Count == 0)
            return Result.Success(new TtsVoiceImportResultDto(0, []));

        // One query per lookup table, not per row: known Twitch users (never created here), the voice ids
        // the channel can actually synthesize, and the viewer assignments that already exist.
        List<string> userIds = rows.Select(r => r.TwitchUserId).Distinct().ToList();
        HashSet<string> knownUsers = (
            await _db
                .UserIdentities.Where(i =>
                    i.Provider == Domain.Identity.Enums.AuthEnums.Platform.Twitch
                    && userIds.Contains(i.ProviderUserId)
                )
                .Select(i => i.ProviderUserId)
                .ToListAsync(cancellationToken)
        ).ToHashSet(StringComparer.Ordinal);

        List<string> voiceIds = rows.Select(r => r.VoiceId).Distinct().ToList();
        HashSet<string> knownVoices = (
            await _db
                .TtsVoices.Where(v => voiceIds.Contains(v.Id))
                .Select(v => v.Id)
                .ToListAsync(cancellationToken)
        ).ToHashSet(StringComparer.Ordinal);
        // Pre-sync fallback: an empty catalogue means "what the providers enumerate" is the valid set.
        if (knownVoices.Count == 0 && !await _db.TtsVoices.AnyAsync(cancellationToken))
        {
            IReadOnlyList<TtsVoiceInfo> providerVoices = await _ttsService.GetAvailableVoicesAsync(
                cancellationToken
            );
            knownVoices = providerVoices
                .Select(v => v.Id)
                .Where(voiceIds.Contains)
                .ToHashSet(StringComparer.Ordinal);
        }

        Dictionary<string, UserTtsVoice> existing = await _db
            .UserTtsVoices.Where(v =>
                v.BroadcasterId == broadcasterId && userIds.Contains(v.UserId)
            )
            .ToDictionaryAsync(v => v.UserId, cancellationToken);

        int imported = 0;
        List<TtsVoiceImportSkipDto> skipped = [];
        foreach (TtsVoiceAssignmentRowDto row in rows)
        {
            if (!knownUsers.Contains(row.TwitchUserId))
            {
                skipped.Add(new TtsVoiceImportSkipDto(row.TwitchUserId, "unknown_user"));
                continue;
            }
            if (!knownVoices.Contains(row.VoiceId))
            {
                skipped.Add(new TtsVoiceImportSkipDto(row.TwitchUserId, "unknown_voice"));
                continue;
            }

            if (existing.TryGetValue(row.TwitchUserId, out UserTtsVoice? assignment))
            {
                assignment.VoiceId = row.VoiceId;
            }
            else
            {
                UserTtsVoice created = new()
                {
                    BroadcasterId = broadcasterId,
                    UserId = row.TwitchUserId,
                    VoiceId = row.VoiceId,
                };
                _db.UserTtsVoices.Add(created);
                existing[row.TwitchUserId] = created; // a later duplicate row upserts, not double-inserts
            }
            imported++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(new TtsVoiceImportResultDto(imported, skipped));
    }

    public async Task<Result> ClearUserVoiceAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        UserTtsVoice? existing = await _db.UserTtsVoices.FirstOrDefaultAsync(
            v => v.BroadcasterId == broadcasterId && v.UserId == userId,
            cancellationToken
        );

        if (existing is null)
            return Result.Failure($"TTS voice assignment '{userId}' was not found.", "NOT_FOUND");

        _db.UserTtsVoices.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<UserTtsVoiceDto>> SetOwnVoiceAsync(
        Guid broadcasterId,
        string viewerUserId,
        SetUserVoiceDto request,
        CancellationToken cancellationToken = default
    )
    {
        Result gate = await CheckSelfServiceEnabledAsync(broadcasterId, cancellationToken);
        if (gate.IsFailure)
            return Result.Failure<UserTtsVoiceDto>(gate.ErrorMessage!, gate.ErrorCode!);
        // The self-service gate passed; the upsert + catalogue validation is the same as the moderator path.
        return await SetUserVoiceAsync(broadcasterId, viewerUserId, request, cancellationToken);
    }

    public async Task<Result<UserTtsVoiceDto?>> GetOwnVoiceAsync(
        Guid broadcasterId,
        string viewerUserId,
        CancellationToken cancellationToken = default
    )
    {
        // A read is never gated — showing "you use the channel default" is harmless even when self-service is off.
        UserTtsVoice? assignment = await _db.UserTtsVoices.FirstOrDefaultAsync(
            v => v.BroadcasterId == broadcasterId && v.UserId == viewerUserId,
            cancellationToken
        );
        return Result.Success<UserTtsVoiceDto?>(
            assignment is null ? null : new UserTtsVoiceDto(assignment.UserId, assignment.VoiceId)
        );
    }

    public async Task<Result> ClearOwnVoiceAsync(
        Guid broadcasterId,
        string viewerUserId,
        CancellationToken cancellationToken = default
    )
    {
        Result gate = await CheckSelfServiceEnabledAsync(broadcasterId, cancellationToken);
        if (gate.IsFailure)
            return gate;
        Result cleared = await ClearUserVoiceAsync(broadcasterId, viewerUserId, cancellationToken);
        // Resetting a voice you never set is a no-op success, not an error, for a viewer-facing command.
        return cleared.IsSuccess || cleared.ErrorCode == "NOT_FOUND" ? Result.Success() : cleared;
    }

    // The self-service gate: TTS must be enabled AND viewer self-service allowed. A missing config row reads
    // as the binding new-channel defaults (both on), so a brand-new channel already lets viewers self-serve.
    private async Task<Result> CheckSelfServiceEnabledAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        TtsConfig? config = await _db.TtsConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            cancellationToken
        );
        TtsConfig effective = config ?? new TtsConfig();
        if (!effective.IsEnabled)
            return Result.Failure("TTS is turned off on this channel.", "FEATURE_DISABLED");
        if (!effective.ViewerVoiceSelfServiceEnabled)
            return Result.Failure(
                "Picking your own voice is turned off on this channel.",
                "FEATURE_DISABLED"
            );
        return Result.Success();
    }

    private static TtsConfigDto ToDto(TtsConfig c) =>
        new(
            c.IsEnabled,
            c.Mode,
            c.DefaultProvider,
            c.DefaultVoiceId,
            c.MaxCharacters,
            c.MinPermission,
            c.SkipBotMessages,
            c.ReadUsernames,
            c.ProfanityCensorEnabled,
            c.ModApprovalRequired,
            c.MinBitsToTts,
            c.ViewerVoiceSelfServiceEnabled,
            HasAzureByokKey: c.AzureApiKeyCipher is not null,
            HasElevenLabsByokKey: c.ElevenLabsApiKeyCipher is not null,
            AzureRegion: c.AzureRegion
        );
}
