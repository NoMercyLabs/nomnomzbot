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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Enforces the channel's heat threshold (S-OWN23). Until this handler existed,
/// <see cref="UserHeatThresholdCrossedEvent"/> was published — and asserted-published by tests — with no
/// consumer anywhere, so the dashboard's heat-threshold control set a number that changed nothing while
/// reading as protection. That is the failure this closes.
///
/// <para><b>Two hard guarantees, in this order:</b></para>
/// <list type="number">
/// <item><b>Opt-in.</b> Nothing fires unless the channel turned
/// <see cref="AutomodConfigDto.AutoTimeoutOnHeat"/> on. Off (the default) a crossing is flagged for a
/// human in the moderation queue, once per crossing, and nothing else happens.</item>
/// <item><b>Immunity is absolute and is checked BEFORE the action, not weighed against it.</b> The
/// broadcaster and everyone on the channel's moderator roster is never auto-timed-out, at any
/// threshold, by any amount of accumulated heat (spam-defense SD8/SD11). This is a short-circuit, not a
/// high bar a big enough score could clear. The roster check is role-agnostic, so any future VIP rows
/// stored there are covered automatically — but note that a Twitch VIP badge is NOT tracked locally
/// today, so VIPs are only immune if they are also on the roster. Widening immunity to badge-holders
/// needs that roster first; it is not claimed here.</item>
/// </list>
/// A skipped crossing is logged with its reason so the operator can see the system looked and chose not
/// to act — SD7, no silent decisions.
/// </summary>
public sealed class HeatThresholdAutoTimeoutHandler : IEventHandler<UserHeatThresholdCrossedEvent>
{
    /// <summary>Applied when the channel has not set its own length (a stored 0 predates the field).</summary>
    private const int DefaultTimeoutSeconds = 600;

    private readonly IApplicationDbContext _db;
    private readonly IModerationService _moderation;
    private readonly IModerationQueueService _queue;
    private readonly IInboundOriginModerator _origin;
    private readonly IEventBus _bus;
    private readonly ILogger<HeatThresholdAutoTimeoutHandler> _logger;

    public HeatThresholdAutoTimeoutHandler(
        IApplicationDbContext db,
        IModerationService moderation,
        IModerationQueueService queue,
        IInboundOriginModerator origin,
        IEventBus bus,
        ILogger<HeatThresholdAutoTimeoutHandler> logger
    )
    {
        _db = db;
        _moderation = moderation;
        _queue = queue;
        _origin = origin;
        _bus = bus;
        _logger = logger;
    }

    public async Task HandleAsync(
        UserHeatThresholdCrossedEvent @event,
        CancellationToken ct = default
    )
    {
        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty)
            return;

        Result<AutomodConfigDto> config = await _moderation.GetAutomodConfigAsync(
            broadcasterId.ToString(),
            ct
        );
        string? username = await _db
            .Users.Where(u => u.Id == @event.SubjectUserId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(ct);

        if (config.IsFailure || !config.Value.AutoTimeoutOnHeat)
        {
            await FlagForHumanAsync(@event, username, ct); // opt-in: heat only flags until the channel enables enforcement
            return;
        }

        if (
            await IsImmuneAsync(broadcasterId, @event.SubjectUserId, @event.SubjectTwitchUserId, ct)
        )
        {
            _logger.LogInformation(
                "Heat threshold crossed by {TwitchUserId} in {BroadcasterId} (heat {Heat} >= {Threshold}) "
                    + "but they are the broadcaster or on the moderator roster — flagged, not actioned.",
                @event.SubjectTwitchUserId,
                broadcasterId,
                @event.HeatScore,
                @event.Threshold
            );
            await FlagForHumanAsync(@event, username, ct);
            return;
        }

        int seconds =
            config.Value.HeatTimeoutSeconds > 0
                ? config.Value.HeatTimeoutSeconds
                : DefaultTimeoutSeconds;

        string reason =
            $"Automatic: moderation heat reached {@event.HeatScore:0} (threshold {@event.Threshold}).";

        // A viewer is actioned on the platform their heat came from. Twitch rides the Twitch moderation
        // service; any other platform goes through the inbound-origin seam, which says honestly when it
        // could not act.
        string? error;
        if (
            string.Equals(
                @event.SubjectProvider,
                AuthEnums.Platform.Twitch,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            // Issued as the broadcaster (operatorUserId = the channel owner): this is the channel's own
            // automation, not a moderator's personal action, and no dashboard user is in the loop.
            Guid ownerUserId = await _db
                .Channels.Where(c => c.Id == broadcasterId)
                .Select(c => c.OwnerUserId)
                .FirstOrDefaultAsync(ct);
            if (ownerUserId == Guid.Empty)
                return;

            Result<ModerationActionResult> result = await _moderation.TimeoutAsync(
                broadcasterId.ToString(),
                ownerUserId,
                @event.SubjectTwitchUserId,
                seconds,
                reason,
                null,
                ct
            );
            error = result.IsFailure
                ? result.ErrorMessage ?? "The platform refused the timeout."
                : null;
        }
        else
            error = await TimeoutViaOriginAsync(@event, seconds, reason, ct);

        if (error is not null)
        {
            _logger.LogWarning(
                "Heat auto-timeout failed for {UserId} on {Provider} in {BroadcasterId}: {Error}",
                @event.SubjectTwitchUserId,
                @event.SubjectProvider,
                broadcasterId,
                error
            );
            await _bus.PublishAsync(
                new UserHeatAutoTimeoutFailedEvent
                {
                    BroadcasterId = broadcasterId,
                    SubjectUserId = @event.SubjectUserId,
                    SubjectTwitchUserId = @event.SubjectTwitchUserId,
                    SubjectUsername = username,
                    Error = error,
                    HeatScore = @event.HeatScore,
                    Threshold = @event.Threshold,
                },
                ct
            );
        }
        else
            _logger.LogInformation(
                "Heat auto-timeout applied to {TwitchUserId} in {BroadcasterId} for {Seconds}s "
                    + "(heat {Heat} >= {Threshold}).",
                @event.SubjectTwitchUserId,
                broadcasterId,
                seconds,
                @event.HeatScore,
                @event.Threshold
            );
    }

    /// <summary>
    /// Times the viewer out on the platform the crossing came from. Returns null when the platform did it,
    /// otherwise the reason it did not; an unsupported platform is a failure, never claimed as done.
    /// </summary>
    private async Task<string?> TimeoutViaOriginAsync(
        UserHeatThresholdCrossedEvent @event,
        int seconds,
        string reason,
        CancellationToken ct
    )
    {
        if (!_origin.Supports(@event.SubjectProvider))
            return $"No chat platform is connected for {@event.SubjectProvider}, so the timeout was not applied.";

        InboundModerationOutcome outcome = await _origin.TimeoutUserAsync(
            @event.BroadcasterId,
            @event.SubjectProvider,
            @event.SubjectTwitchUserId,
            seconds,
            reason,
            ct
        );
        return outcome.Status == InboundModerationStatus.Done
            ? null
            : outcome.Reason ?? $"{@event.SubjectProvider} did not apply the timeout.";
    }

    private async Task FlagForHumanAsync(
        UserHeatThresholdCrossedEvent @event,
        string? username,
        CancellationToken ct
    )
    {
        Result<Guid> flagged = await _queue.EnqueueFlagAsync(
            @event.BroadcasterId,
            ModerationQueueSource.HeatThreshold,
            @event.SubjectUserId,
            @event.SubjectTwitchUserId,
            username,
            $"Heat reached {@event.HeatScore:0} (threshold {@event.Threshold}). No automatic action was taken.",
            ct
        );
        if (flagged.IsFailure)
            _logger.LogWarning(
                "Could not flag heat crossing of {TwitchUserId} in {BroadcasterId}: {Error}",
                @event.SubjectTwitchUserId,
                @event.BroadcasterId,
                flagged.ErrorMessage
            );
    }

    /// <summary>
    /// The broadcaster and anyone on the channel's moderator roster are immune. Checked before the
    /// action so no signal, however strong, can reach them. Role-agnostic by design: whatever a roster
    /// row's <c>Role</c> says, being on it is enough.
    /// </summary>
    private async Task<bool> IsImmuneAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        string subjectTwitchUserId,
        CancellationToken ct
    )
    {
        Guid ownerUserId = await _db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId != Guid.Empty && ownerUserId == subjectUserId)
            return true;

        string? broadcasterTwitchId = await _db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.TwitchChannelId)
            .FirstOrDefaultAsync(ct);
        if (
            !string.IsNullOrEmpty(broadcasterTwitchId)
            && string.Equals(
                broadcasterTwitchId,
                subjectTwitchUserId,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return true;

        return await _db
            .ChannelModerators.IgnoreQueryFilters()
            .AnyAsync(
                m =>
                    m.ChannelId == broadcasterId
                    && m.UserId == subjectUserId
                    && m.DeletedAt == null,
                ct
            );
    }
}
