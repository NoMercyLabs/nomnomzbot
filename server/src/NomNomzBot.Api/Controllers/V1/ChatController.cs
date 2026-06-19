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
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.ValueObjects;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/chat")]
[Authorize]
[Tags("Chat")]
public class ChatController : BaseController
{
    private readonly IApplicationDbContext _db;

    public ChatController(IApplicationDbContext db)
    {
        _db = db;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record ChatMessageDto(
        string Id,
        string ChannelId,
        string UserId,
        string Username,
        string DisplayName,
        string UserType,
        string? Color,
        string Message,
        List<ChatBadge> Badges,
        List<ChatMessageFragment> Fragments,
        string MessageType,
        bool IsCommand,
        bool IsCheer,
        int? BitsAmount,
        string? ReplyToMessageId,
        string Timestamp
    );

    public record ChatSettingsDto(
        bool SlowMode,
        int SlowModeDelay,
        bool SubscriberOnly,
        bool EmotesOnly,
        bool FollowersOnly,
        int FollowersOnlyDuration
    );

    private static readonly ChatSettingsDto DefaultSettings = new(
        SlowMode: false,
        SlowModeDelay: 0,
        SubscriberOnly: false,
        EmotesOnly: false,
        FollowersOnly: false,
        FollowersOnlyDuration: 0
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── GET messages ──────────────────────────────────────────────────────────

    [HttpGet("messages")]
    [ProducesResponseType<StatusResponseDto<List<ChatMessageDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMessages(
        string channelId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default
    )
    {
        limit = Math.Clamp(limit, 1, 200);

        var messages = await _db
            .ChatMessages.Where(m => m.BroadcasterId == channelId && m.DeletedAt == null)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .Select(m => new ChatMessageDto(
                m.Id,
                m.BroadcasterId,
                m.UserId,
                m.Username,
                m.DisplayName,
                m.UserType,
                m.ColorHex,
                m.Message,
                m.Badges,
                m.Fragments,
                m.MessageType,
                m.IsCommand,
                m.IsCheer,
                m.BitsAmount,
                m.ReplyToMessageId,
                m.CreatedAt.ToString("o")
            ))
            .ToListAsync(ct);

        // Return in chronological order (oldest first)
        messages.Reverse();

        return Ok(new StatusResponseDto<List<ChatMessageDto>> { Data = messages });
    }

    // ── GET settings ─────────────────────────────────────────────────────────

    [HttpGet("settings")]
    [ProducesResponseType<StatusResponseDto<ChatSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(string channelId, CancellationToken ct)
    {
        ConfigEntity? config = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == channelId && c.Key == "chat.settings",
            ct
        );

        ChatSettingsDto settings = config?.Value is not null
            ? JsonSerializer.Deserialize<ChatSettingsDto>(config.Value, JsonOptions)
                ?? DefaultSettings
            : DefaultSettings;

        return Ok(new StatusResponseDto<ChatSettingsDto> { Data = settings });
    }

    // ── PUT settings ──────────────────────────────────────────────────────────

    [HttpPut("settings")]
    [ProducesResponseType<StatusResponseDto<ChatSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(
        string channelId,
        [FromBody] ChatSettingsDto request,
        CancellationToken ct
    ) => await SaveSettings(channelId, request, ct);

    // ── PATCH settings (partial update) ───────────────────────────────────────

    [HttpPatch("settings")]
    [ProducesResponseType<StatusResponseDto<ChatSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PatchSettings(
        string channelId,
        [FromBody] JsonElement patch,
        CancellationToken ct
    )
    {
        // Load existing, apply partial override from patch body
        ConfigEntity? config = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == channelId && c.Key == "chat.settings",
            ct
        );

        ChatSettingsDto current = config?.Value is not null
            ? JsonSerializer.Deserialize<ChatSettingsDto>(config.Value, JsonOptions)
                ?? DefaultSettings
            : DefaultSettings;

        bool slowMode = patch.TryGetProperty("slowMode", out var sm)
            ? sm.GetBoolean()
            : current.SlowMode;
        int slowModeDelay = patch.TryGetProperty("slowModeDelay", out var smd)
            ? smd.GetInt32()
            : current.SlowModeDelay;
        bool subscriberOnly = patch.TryGetProperty("subscriberOnly", out var so)
            ? so.GetBoolean()
            : current.SubscriberOnly;
        bool emotesOnly = patch.TryGetProperty("emotesOnly", out var eo)
            ? eo.GetBoolean()
            : current.EmotesOnly;
        bool followersOnly = patch.TryGetProperty("followersOnly", out var fo)
            ? fo.GetBoolean()
            : current.FollowersOnly;
        int followersOnlyDuration = patch.TryGetProperty("followersOnlyDuration", out var fod)
            ? fod.GetInt32()
            : current.FollowersOnlyDuration;

        var merged = new ChatSettingsDto(
            slowMode,
            slowModeDelay,
            subscriberOnly,
            emotesOnly,
            followersOnly,
            followersOnlyDuration
        );
        return await SaveSettings(channelId, merged, ct);
    }

    private async Task<IActionResult> SaveSettings(
        string channelId,
        ChatSettingsDto settings,
        CancellationToken ct
    )
    {
        ConfigEntity? config = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == channelId && c.Key == "chat.settings",
            ct
        );

        string json = JsonSerializer.Serialize(settings, JsonOptions);

        if (config is null)
        {
            _db.Configurations.Add(
                new ConfigEntity
                {
                    BroadcasterId = channelId,
                    Key = "chat.settings",
                    Value = json,
                }
            );
        }
        else
        {
            config.Value = json;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new StatusResponseDto<ChatSettingsDto> { Data = settings });
    }
}
