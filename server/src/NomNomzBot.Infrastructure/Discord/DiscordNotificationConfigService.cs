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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// Notification rules (discord.md §3.2). Validates the (connection, trigger) uniqueness, the ping-role
/// ownership, and the milestone fields; renders previews through <see cref="ITemplateEngine"/> without posting.
/// <see cref="CurrentEmbedConfigVersion"/> is the single source of truth for the <c>EmbedConfig</c> shape; on
/// read a stale row is upcast in memory (additive Newtonsoft changes need no version bump, so v1 is current).
/// </summary>
public sealed class DiscordNotificationConfigService : IDiscordNotificationConfigService
{
    /// <summary>The current EmbedConfig shape version. Only a breaking reshape raises it + adds an upcast step.</summary>
    private const int CurrentEmbedConfigVersion = 1;

    private static readonly string[] AllowedTriggers =
    [
        "go_live",
        "new_clip",
        "schedule",
        "milestone",
    ];

    private readonly IApplicationDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITemplateEngine _templateEngine;

    public DiscordNotificationConfigService(
        IApplicationDbContext db,
        IUnitOfWork unitOfWork,
        ITemplateEngine templateEngine
    )
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _templateEngine = templateEngine;
    }

    public async Task<Result<IReadOnlyList<DiscordNotificationConfigDto>>> GetConfigsAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        List<DiscordNotificationConfig> configs = await _db
            .DiscordNotificationConfigs.Where(c =>
                c.BroadcasterId == broadcasterId && c.GuildConnectionId == connectionId
            )
            .OrderBy(c => c.TriggerType)
            .ToListAsync(ct);

        IReadOnlyList<DiscordNotificationConfigDto> dtos = [.. configs.Select(ToDto)];
        return Result.Success(dtos);
    }

    public async Task<Result<DiscordNotificationConfigDto>> CreateConfigAsync(
        Guid broadcasterId,
        Guid connectionId,
        CreateDiscordNotificationConfigRequest request,
        CancellationToken ct = default
    )
    {
        Result validation = await ValidateAsync(
            broadcasterId,
            connectionId,
            request.TriggerType,
            request.TargetChannelId,
            request.PingRoleId,
            request.MilestoneType,
            request.MilestoneThreshold,
            ct
        );
        if (validation.IsFailure)
            return Result.Failure<DiscordNotificationConfigDto>(
                validation.ErrorMessage,
                validation.ErrorCode
            );

        bool connectionExists = await _db.DiscordGuildConnections.AnyAsync(
            c => c.Id == connectionId && c.BroadcasterId == broadcasterId,
            ct
        );
        if (!connectionExists)
            return Errors.NotFound<DiscordNotificationConfigDto>(
                "Discord connection",
                connectionId.ToString()
            );

        bool exists = await _db.DiscordNotificationConfigs.AnyAsync(
            c => c.GuildConnectionId == connectionId && c.TriggerType == request.TriggerType,
            ct
        );
        if (exists)
            return Result.Failure<DiscordNotificationConfigDto>(
                $"A '{request.TriggerType}' rule already exists for this connection.",
                "ALREADY_EXISTS"
            );

        DiscordNotificationConfig config = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            GuildConnectionId = connectionId,
            TriggerType = request.TriggerType,
            Enabled = request.Enabled,
            TargetChannelId = request.TargetChannelId.Trim(),
            PingRoleId = request.PingRoleId,
            MessageTemplate = request.MessageTemplate,
            EmbedConfig = DiscordEmbedMapper.ToDomain(request.EmbedConfig),
            MilestoneType = request.MilestoneType,
            MilestoneThreshold = request.MilestoneThreshold,
            ConfigSchemaVersion = CurrentEmbedConfigVersion,
        };

        _db.DiscordNotificationConfigs.Add(config);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToDto(config));
    }

    public async Task<Result<DiscordNotificationConfigDto>> UpdateConfigAsync(
        Guid broadcasterId,
        Guid configId,
        UpdateDiscordNotificationConfigRequest request,
        CancellationToken ct = default
    )
    {
        DiscordNotificationConfig? config =
            await _db.DiscordNotificationConfigs.FirstOrDefaultAsync(
                c => c.Id == configId && c.BroadcasterId == broadcasterId,
                ct
            );
        if (config is null)
            return Errors.NotFound<DiscordNotificationConfigDto>(
                "Discord config",
                configId.ToString()
            );

        Result validation = await ValidateAsync(
            broadcasterId,
            config.GuildConnectionId,
            config.TriggerType,
            request.TargetChannelId,
            request.PingRoleId,
            request.MilestoneType,
            request.MilestoneThreshold,
            ct
        );
        if (validation.IsFailure)
            return Result.Failure<DiscordNotificationConfigDto>(
                validation.ErrorMessage,
                validation.ErrorCode
            );

        config.Enabled = request.Enabled;
        config.TargetChannelId = request.TargetChannelId.Trim();
        config.PingRoleId = request.PingRoleId;
        config.MessageTemplate = request.MessageTemplate;
        config.EmbedConfig = DiscordEmbedMapper.ToDomain(request.EmbedConfig);
        config.MilestoneType = request.MilestoneType;
        config.MilestoneThreshold = request.MilestoneThreshold;
        // Writing persists the upcast: the row now holds the current EmbedConfig shape + version.
        config.ConfigSchemaVersion = CurrentEmbedConfigVersion;

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ToDto(config));
    }

    public async Task<Result> DeleteConfigAsync(
        Guid broadcasterId,
        Guid configId,
        CancellationToken ct = default
    )
    {
        DiscordNotificationConfig? config =
            await _db.DiscordNotificationConfigs.FirstOrDefaultAsync(
                c => c.Id == configId && c.BroadcasterId == broadcasterId,
                ct
            );
        if (config is null)
            return Errors.NotFound<object>("Discord config", configId.ToString());

        _db.DiscordNotificationConfigs.Remove(config); // soft-delete via interceptor
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<DiscordNotificationPreviewDto>> PreviewAsync(
        Guid broadcasterId,
        Guid configId,
        CancellationToken ct = default
    )
    {
        DiscordNotificationConfig? config =
            await _db.DiscordNotificationConfigs.FirstOrDefaultAsync(
                c => c.Id == configId && c.BroadcasterId == broadcasterId,
                ct
            );
        if (config is null)
            return Errors.NotFound<DiscordNotificationPreviewDto>(
                "Discord config",
                configId.ToString()
            );

        // Sample variables so the streamer sees a realistic preview without a live stream.
        Dictionary<string, string> sample = SampleVariables(config.TriggerType);

        string content = _templateEngine.Render(config.MessageTemplate ?? string.Empty, sample);

        DiscordEmbedDto? embed = DiscordEmbedMapper.ToDto(config.EmbedConfig);
        DiscordEmbedDto? renderedEmbed = embed is null
            ? null
            : DiscordEmbedMapper.RenderTemplates(embed, t => _templateEngine.Render(t, sample));

        string? pingMention = config.PingRoleId is null
            ? null
            : await ResolvePingMentionAsync(config.PingRoleId.Value, ct);

        return Result.Success(
            new DiscordNotificationPreviewDto(content, renderedEmbed, pingMention)
        );
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<Result> ValidateAsync(
        Guid broadcasterId,
        Guid connectionId,
        string triggerType,
        string targetChannelId,
        Guid? pingRoleId,
        string? milestoneType,
        int? milestoneThreshold,
        CancellationToken ct
    )
    {
        if (!AllowedTriggers.Contains(triggerType))
            return Errors.ValidationFailed($"Unknown trigger type '{triggerType}'.");

        if (string.IsNullOrWhiteSpace(targetChannelId))
            return Errors.ValidationFailed("A target Discord channel is required.");

        bool isMilestone = triggerType == "milestone";
        bool hasMilestoneFields = milestoneType is not null && milestoneThreshold is not null;
        if (isMilestone != hasMilestoneFields)
            return Errors.ValidationFailed(
                "Milestone type and threshold are required for (and only for) the milestone trigger."
            );

        if (pingRoleId is { } roleId)
        {
            bool roleBelongs = await _db.DiscordNotificationRoles.AnyAsync(
                r =>
                    r.Id == roleId
                    && r.BroadcasterId == broadcasterId
                    && r.GuildConnectionId == connectionId,
                ct
            );
            if (!roleBelongs)
                return Errors.ValidationFailed("The ping role does not belong to this connection.");
        }

        return Result.Success();
    }

    private async Task<string?> ResolvePingMentionAsync(Guid pingRoleId, CancellationToken ct)
    {
        string? discordRoleId = await _db
            .DiscordNotificationRoles.Where(r => r.Id == pingRoleId)
            .Select(r => r.DiscordRoleId)
            .FirstOrDefaultAsync(ct);
        return discordRoleId is null ? null : $"<@&{discordRoleId}>";
    }

    private static Dictionary<string, string> SampleVariables(string triggerType) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["channel.name"] = "SampleStreamer",
            ["channel.title"] = "Sample stream title",
            ["channel.game"] = "Just Chatting",
            ["channel.url"] = "https://twitch.tv/SampleStreamer",
            ["trigger.type"] = triggerType,
        };

    private static DiscordNotificationConfigDto ToDto(DiscordNotificationConfig c) =>
        new(
            c.Id,
            c.GuildConnectionId,
            c.TriggerType,
            c.Enabled,
            c.TargetChannelId,
            c.PingRoleId,
            c.MessageTemplate,
            DiscordEmbedMapper.ToDto(UpcastEmbed(c)),
            c.MilestoneType,
            c.MilestoneThreshold,
            c.CreatedAt,
            c.UpdatedAt
        );

    /// <summary>
    /// Forward-migrates a stale <c>EmbedConfig</c> to the current shape on read (discord.md §3.2). With
    /// <see cref="CurrentEmbedConfigVersion"/> = 1 and Newtonsoft tolerating additive changes, no upcast step
    /// is needed yet; the chain is the anchor for the first breaking reshape.
    /// </summary>
    private static Domain.Discord.ValueObjects.DiscordEmbedConfig? UpcastEmbed(
        DiscordNotificationConfig c
    )
    {
        // No breaking version exists yet (v1 is current). When one lands, chain per-version upcast steps here
        // from c.ConfigSchemaVersion up to CurrentEmbedConfigVersion before returning.
        return c.EmbedConfig;
    }
}
