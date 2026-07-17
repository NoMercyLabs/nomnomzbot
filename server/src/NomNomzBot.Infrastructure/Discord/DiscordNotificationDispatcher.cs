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
using NomNomzBot.Domain.Discord.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// Dispatch + dedupe (discord.md §3.4). For each enabled config matching the tenant+trigger:
/// <list type="number">
///   <item>gate on both-opt-in via <see cref="IDiscordGuildService.IsLinkActiveAsync"/> — else append
///   <c>skipped</c> with no post;</item>
///   <item>atomically dedupe: insert the <c>DiscordNotificationDispatch</c> row; a unique-constraint violation
///   on <c>(NotificationConfigId, DedupeKey)</c> → append <c>skipped_dupe</c>, no double post;</item>
///   <item>render the template + embed, post via <see cref="IDiscordBotGateway"/>;</item>
///   <item>persist the outcome on the SAME appended row;</item>
///   <item>publish <see cref="DiscordNotificationDispatchedEvent"/>.</item>
/// </list>
/// Never throws for a Discord-side failure (captured as <c>failed</c>).
/// </summary>
public sealed class DiscordNotificationDispatcher : IDiscordNotificationDispatcher
{
    private const string StatusSent = "sent";
    private const string StatusFailed = "failed";
    private const string StatusSkippedDupe = "skipped_dupe";
    private const string StatusSkipped = "skipped";

    private readonly IApplicationDbContext _db;
    private readonly IDiscordGuildService _guildService;
    private readonly IDiscordBotGateway _gateway;
    private readonly ITemplateEngine _templateEngine;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public DiscordNotificationDispatcher(
        IApplicationDbContext db,
        IDiscordGuildService guildService,
        IDiscordBotGateway gateway,
        ITemplateEngine templateEngine,
        IEventBus eventBus,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _guildService = guildService;
        _gateway = gateway;
        _templateEngine = templateEngine;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<Result<DiscordDispatchOutcomeDto>> DispatchAsync(
        DiscordDispatchRequest request,
        CancellationToken ct = default
    )
    {
        // The matching enabled rule(s) for this tenant + trigger. A single config per (connection, trigger)
        // is enforced by the unique index, but a tenant may link several guilds — dispatch to each.
        List<DiscordNotificationConfig> configs = await _db
            .DiscordNotificationConfigs.Where(c =>
                c.BroadcasterId == request.BroadcasterId
                && c.TriggerType == request.TriggerType
                && c.Enabled
            )
            .ToListAsync(ct);

        if (configs.Count == 0)
            return Result.Failure<DiscordDispatchOutcomeDto>(
                "No enabled Discord rule for this trigger.",
                "NOT_FOUND"
            );

        DiscordDispatchOutcomeDto? lastOutcome = null;
        foreach (DiscordNotificationConfig config in configs)
        {
            lastOutcome = await DispatchOneAsync(request, config, ct);
        }

        return Result.Success(lastOutcome!);
    }

    private async Task<DiscordDispatchOutcomeDto> DispatchOneAsync(
        DiscordDispatchRequest request,
        DiscordNotificationConfig config,
        CancellationToken ct
    )
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        // 1. Gate: both-opt-in must hold.
        Result<bool> active = await _guildService.IsLinkActiveAsync(
            request.BroadcasterId,
            config.GuildConnectionId,
            ct
        );
        if (active.IsFailure || !active.Value)
        {
            return await AppendAndPublishAsync(
                request,
                config,
                StatusSkipped,
                postedMessageId: null,
                error: "Discord link is not active (both-opt-in not met).",
                now,
                ct
            );
        }

        // 2. Atomic dedupe: the unique (NotificationConfigId, DedupeKey) insert IS the guard.
        DiscordNotificationDispatch dispatch = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = request.BroadcasterId,
            NotificationConfigId = config.Id,
            TriggerType = request.TriggerType,
            DedupeKey = request.DedupeKey,
            StreamId = request.StreamId,
            Status = StatusSent, // optimistic; corrected below on failure
            DispatchedAt = now,
        };
        _db.DiscordNotificationDispatches.Add(dispatch);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Duplicate insert → already dispatched for this dedupe key. Detach the conflicting add and record
            // the skip as a NEW append-only row (the original 'sent' row stays the source of truth).
            _db.DiscordNotificationDispatches.Entry(dispatch).State = EntityState.Detached;
            return await AppendAndPublishAsync(
                request,
                config,
                StatusSkippedDupe,
                postedMessageId: null,
                error: null,
                now,
                ct
            );
        }

        // 3. Render + post.
        string content = _templateEngine.Render(
            config.MessageTemplate ?? string.Empty,
            request.TemplateData
        );
        DiscordEmbedDto? embed = DiscordEmbedMapper.ToDto(config.EmbedConfig);
        DiscordEmbedDto? renderedEmbed = embed is null
            ? null
            : DiscordEmbedMapper.RenderTemplates(
                embed,
                t => _templateEngine.Render(t, request.TemplateData)
            );

        string? pingRoleId = config.PingRoleId is null
            ? null
            : await ResolveDiscordRoleIdAsync(config.PingRoleId.Value, ct);

        Result<string> posted = await _gateway.PostMessageAsync(
            request.BroadcasterId,
            config.TargetChannelId,
            new DiscordOutboundMessage(content, renderedEmbed, pingRoleId),
            ct
        );

        // 4. Persist the outcome on the SAME appended row.
        if (posted.IsSuccess)
        {
            dispatch.Status = StatusSent;
            dispatch.PostedMessageId = posted.Value;
            dispatch.Error = null;
        }
        else
        {
            dispatch.Status = StatusFailed;
            dispatch.Error = posted.ErrorMessage;
        }
        await _db.SaveChangesAsync(ct);

        DiscordDispatchOutcomeDto outcome = new(
            dispatch.Id,
            dispatch.Status,
            dispatch.PostedMessageId,
            dispatch.Error
        );
        await PublishDispatchedAsync(request, config, dispatch, ct);

        // 5. Personal DMs (decided 2026-07-17): an independent output of the same dispatch — a channel
        // failure never blocks them, and vice versa.
        await FanOutDmsAsync(request, config, content, renderedEmbed, ct);

        return outcome;
    }

    /// <summary>
    /// DMs the rendered notification to every opted-in member of the config's ping role when that role
    /// has <c>DmEnabled</c>. Sequential, per-member best-effort (closed DMs are logged and skipped);
    /// each DM is its own append-only dispatch row keyed <c>{baseDedupeKey}:dm:{memberId}</c>, so a
    /// re-dispatch is a per-member no-op. The member's DM channel id is cached on the opt-in row.
    /// </summary>
    private async Task FanOutDmsAsync(
        DiscordDispatchRequest request,
        DiscordNotificationConfig config,
        string content,
        DiscordEmbedDto? renderedEmbed,
        CancellationToken ct
    )
    {
        if (config.PingRoleId is null)
            return;

        bool dmEnabled = await _db.DiscordNotificationRoles.AnyAsync(
            r => r.Id == config.PingRoleId.Value && r.DmEnabled,
            ct
        );
        if (!dmEnabled)
            return;

        List<DiscordMemberOptIn> recipients = await _db
            .DiscordMemberOptIns.Where(o =>
                o.NotificationRoleId == config.PingRoleId.Value && o.OptedOutAt == null
            )
            .ToListAsync(ct);

        foreach (DiscordMemberOptIn recipient in recipients)
        {
            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
            DiscordNotificationDispatch dmDispatch = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = request.BroadcasterId,
                NotificationConfigId = config.Id,
                TriggerType = request.TriggerType,
                DedupeKey = $"{request.DedupeKey}:dm:{recipient.DiscordMemberId}",
                StreamId = request.StreamId,
                Status = StatusSent, // optimistic; corrected below on failure
                DispatchedAt = now,
            };
            _db.DiscordNotificationDispatches.Add(dmDispatch);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Already DMed for this dedupe key — a re-dispatch is a per-member no-op.
                _db.DiscordNotificationDispatches.Entry(dmDispatch).State = EntityState.Detached;
                continue;
            }

            string? dmChannelId = recipient.DmChannelId;
            if (string.IsNullOrEmpty(dmChannelId))
            {
                Result<string> opened = await _gateway.OpenDmChannelAsync(
                    request.BroadcasterId,
                    recipient.DiscordMemberId,
                    ct
                );
                if (opened.IsSuccess)
                {
                    dmChannelId = opened.Value;
                    recipient.DmChannelId = dmChannelId; // cache for the next go-live
                }
                else
                {
                    dmDispatch.Status = StatusFailed;
                    dmDispatch.Error = opened.ErrorMessage;
                    await _db.SaveChangesAsync(ct);
                    continue;
                }
            }

            Result<string> sent = await _gateway.PostMessageAsync(
                request.BroadcasterId,
                dmChannelId,
                new DiscordOutboundMessage(content, renderedEmbed, PingRoleId: null),
                ct
            );
            if (sent.IsSuccess)
            {
                dmDispatch.PostedMessageId = sent.Value;
            }
            else
            {
                // Closed DMs (Discord 50007) land here — best-effort, never fails the others.
                dmDispatch.Status = StatusFailed;
                dmDispatch.Error = sent.ErrorMessage;
            }
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<Result<PagedList<DiscordDispatchLogDto>>> GetDispatchLogAsync(
        Guid broadcasterId,
        Guid connectionId,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        int safePage = page < 1 ? 1 : page;
        int safeSize = pageSize is < 1 or > 200 ? 25 : pageSize;

        // The dispatch table carries the config id, not the connection id — scope through the configs of the
        // connection (tenant-scoped throughout).
        IQueryable<Guid> configIds = _db
            .DiscordNotificationConfigs.Where(c =>
                c.BroadcasterId == broadcasterId && c.GuildConnectionId == connectionId
            )
            .Select(c => c.Id);

        IQueryable<DiscordNotificationDispatch> query = _db.DiscordNotificationDispatches.Where(d =>
            d.BroadcasterId == broadcasterId && configIds.Contains(d.NotificationConfigId)
        );

        int total = await query.CountAsync(ct);

        List<DiscordDispatchLogDto> items = await query
            .OrderByDescending(d => d.DispatchedAt)
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .Select(d => new DiscordDispatchLogDto(
                d.Id,
                d.NotificationConfigId,
                d.TriggerType,
                d.DedupeKey,
                d.StreamId,
                d.PostedMessageId,
                d.Status,
                d.Error,
                d.DispatchedAt
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<DiscordDispatchLogDto>(items, safePage, safeSize, total)
        );
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<DiscordDispatchOutcomeDto> AppendAndPublishAsync(
        DiscordDispatchRequest request,
        DiscordNotificationConfig config,
        string status,
        string? postedMessageId,
        string? error,
        DateTime now,
        CancellationToken ct
    )
    {
        DiscordNotificationDispatch dispatch = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = request.BroadcasterId,
            NotificationConfigId = config.Id,
            TriggerType = request.TriggerType,
            // A skip/dupe row must not collide with the live dedupe key; suffix it so the unique index admits it.
            DedupeKey = $"{request.DedupeKey}#{status}:{now.Ticks}",
            StreamId = request.StreamId,
            PostedMessageId = postedMessageId,
            Status = status,
            Error = error,
            DispatchedAt = now,
        };
        _db.DiscordNotificationDispatches.Add(dispatch);
        await _db.SaveChangesAsync(ct);

        DiscordDispatchOutcomeDto outcome = new(dispatch.Id, status, postedMessageId, error);
        await PublishDispatchedAsync(request, config, dispatch, ct);
        return outcome;
    }

    private async Task<string?> ResolveDiscordRoleIdAsync(Guid pingRoleId, CancellationToken ct) =>
        await _db
            .DiscordNotificationRoles.Where(r => r.Id == pingRoleId)
            .Select(r => r.DiscordRoleId)
            .FirstOrDefaultAsync(ct);

    private Task PublishDispatchedAsync(
        DiscordDispatchRequest request,
        DiscordNotificationConfig config,
        DiscordNotificationDispatch dispatch,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new DiscordNotificationDispatchedEvent
            {
                BroadcasterId = request.BroadcasterId,
                DispatchId = dispatch.Id,
                NotificationConfigId = config.Id,
                TriggerType = dispatch.TriggerType,
                DedupeKey = dispatch.DedupeKey,
                Status = dispatch.Status,
                PostedMessageId = dispatch.PostedMessageId,
                Error = dispatch.Error,
            },
            ct
        );
}
