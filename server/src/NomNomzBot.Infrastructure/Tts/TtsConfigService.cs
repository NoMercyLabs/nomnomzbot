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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
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
    private readonly IApplicationDbContext _db;
    private readonly ITtsService _ttsService;
    private readonly IEventBus _eventBus;

    public TtsConfigService(IApplicationDbContext db, ITtsService ttsService, IEventBus eventBus)
    {
        _db = db;
        _ttsService = ttsService;
        _eventBus = eventBus;
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

        await _db.SaveChangesAsync(cancellationToken);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "tts-config",
                Action = "updated",
            },
            cancellationToken
        );

        return Result.Success(ToDto(config));
    }

    public async Task<Result<IReadOnlyList<TtsVoiceDto>>> GetVoicesAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<TtsVoice> dbVoices = await _db
            .TtsVoices.OrderBy(v => v.Provider)
            .ThenBy(v => v.Locale)
            .ThenBy(v => v.Name)
            .ToListAsync(cancellationToken);

        if (dbVoices.Count > 0)
        {
            IReadOnlyList<TtsVoiceDto> dbDtos = dbVoices
                .Select(v => new TtsVoiceDto(
                    v.Id,
                    v.Name,
                    v.DisplayName,
                    v.Locale,
                    v.Gender,
                    v.Provider,
                    v.IsDefault
                ))
                .ToList();
            return Result.Success(dbDtos);
        }

        // Fallback: enumerate directly from providers
        IReadOnlyList<TtsVoiceInfo> providerVoices = await _ttsService.GetAvailableVoicesAsync(
            cancellationToken
        );
        IReadOnlyList<TtsVoiceDto> dtos = providerVoices
            .Select(v => new TtsVoiceDto(
                v.Id,
                v.Name,
                v.DisplayName,
                v.Locale,
                v.Gender,
                v.Provider,
                IsDefault: false
            ))
            .ToList();
        return Result.Success(dtos);
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
        Result<IReadOnlyList<TtsVoiceDto>> voices = await GetVoicesAsync(cancellationToken);
        if (voices.IsFailure)
            return Result.Failure<UserTtsVoiceDto>(
                voices.ErrorMessage,
                voices.ErrorCode,
                voices.ErrorDetail
            );
        if (!voices.Value.Any(v => v.Id == request.VoiceId))
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
            c.MinBitsToTts
        );
}
