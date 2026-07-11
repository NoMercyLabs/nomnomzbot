// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;
using ChannelConfiguration = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Tts;

public class TtsConfigService : ITtsConfigService
{
    private const string ConfigKey = "tts:config";

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
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        TtsConfigDto config = await LoadConfigAsync(broadcasterId, cancellationToken);
        return Result.Success(config);
    }

    public async Task<Result<TtsConfigDto>> UpdateConfigAsync(
        string broadcasterId,
        UpdateTtsConfigDto request,
        CancellationToken cancellationToken = default
    )
    {
        Guid? tenantId = Guid.TryParse(broadcasterId, out Guid g) ? g : null;
        ChannelConfiguration? existing = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == tenantId && c.Key == ConfigKey,
            cancellationToken
        );

        TtsConfigData current = existing is not null
            ? JsonSerializer.Deserialize<TtsConfigData>(existing.Value ?? "{}")
                ?? new TtsConfigData()
            : new();

        if (request.IsEnabled.HasValue)
            current.IsEnabled = request.IsEnabled.Value;
        if (request.DefaultVoiceId is not null)
            current.DefaultVoiceId = request.DefaultVoiceId;
        if (request.MaxLength.HasValue)
            current.MaxLength = request.MaxLength.Value;
        if (request.MinPermission is not null)
            current.MinPermission = request.MinPermission;
        if (request.SkipBotMessages.HasValue)
            current.SkipBotMessages = request.SkipBotMessages.Value;
        if (request.ReadUsernames.HasValue)
            current.ReadUsernames = request.ReadUsernames.Value;

        string json = JsonSerializer.Serialize(current);

        if (existing is not null)
        {
            existing.Value = json;
        }
        else
        {
            _db.Configurations.Add(
                new()
                {
                    BroadcasterId = tenantId,
                    Key = ConfigKey,
                    Value = json,
                }
            );
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = tenantId ?? Guid.Empty,
                Domain = "tts-config",
                Action = "updated",
            },
            cancellationToken
        );

        return Result.Success(ToDto(current));
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
        string broadcasterId,
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
        string broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors
                .ValidationFailed("A valid channel id is required.")
                .ToTyped<UserTtsVoiceDto>();

        UserTtsVoice? assignment = await _db.UserTtsVoices.FirstOrDefaultAsync(
            v => v.BroadcasterId == tenantId && v.UserId == userId,
            cancellationToken
        );

        if (assignment is null)
            return Errors.NotFound<UserTtsVoiceDto>("TTS voice assignment", userId);

        return Result.Success(new UserTtsVoiceDto(assignment.UserId, assignment.VoiceId));
    }

    public async Task<Result<UserTtsVoiceDto>> SetUserVoiceAsync(
        string broadcasterId,
        string userId,
        SetUserVoiceDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors
                .ValidationFailed("A valid channel id is required.")
                .ToTyped<UserTtsVoiceDto>();

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
            v => v.BroadcasterId == tenantId && v.UserId == userId,
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
                    BroadcasterId = tenantId,
                    UserId = userId,
                    VoiceId = request.VoiceId,
                }
            );
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(new UserTtsVoiceDto(userId, request.VoiceId));
    }

    public async Task<Result> ClearUserVoiceAsync(
        string broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ValidationFailed("A valid channel id is required.");

        UserTtsVoice? existing = await _db.UserTtsVoices.FirstOrDefaultAsync(
            v => v.BroadcasterId == tenantId && v.UserId == userId,
            cancellationToken
        );

        if (existing is null)
            return Result.Failure($"TTS voice assignment '{userId}' was not found.", "NOT_FOUND");

        _db.UserTtsVoices.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<TtsConfigDto> LoadConfigAsync(
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        Guid? tenantId = Guid.TryParse(broadcasterId, out Guid g) ? g : null;
        ChannelConfiguration? entry = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == tenantId && c.Key == ConfigKey,
            cancellationToken
        );

        if (entry?.Value is null)
            return ToDto(new());

        TtsConfigData data =
            JsonSerializer.Deserialize<TtsConfigData>(entry.Value) ?? new TtsConfigData();
        return ToDto(data);
    }

    private static TtsConfigDto ToDto(TtsConfigData d) =>
        new(
            d.IsEnabled,
            d.DefaultVoiceId,
            d.MaxLength,
            d.MinPermission,
            d.SkipBotMessages,
            d.ReadUsernames
        );

    private sealed class TtsConfigData
    {
        public bool IsEnabled { get; set; } = true;
        public string DefaultVoiceId { get; set; } = "en-US-AriaNeural";
        public int MaxLength { get; set; } = 200;
        public string MinPermission { get; set; } = "everyone";
        public bool SkipBotMessages { get; set; } = true;
        public bool ReadUsernames { get; set; } = true;
    }
}
