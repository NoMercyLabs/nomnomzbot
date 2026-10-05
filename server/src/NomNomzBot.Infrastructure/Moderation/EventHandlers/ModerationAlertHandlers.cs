// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// The moderation-notice trigger sources: <c>channel.ban</c> (permanent bans AND timeouts — Twitch's own
/// <c>channel.ban</c> topic covers both, split only by duration), <c>channel.unban</c>, and the moderator role
/// notices <c>channel.moderator.add</c> / <c>channel.moderator.remove</c> (only <c>{user}</c> and <c>{user.id}</c>). Variables:
/// <c>{user}</c> (the affected viewer), <c>{moderator}</c> (display name, falling back to the moderator id on
/// non-Twitch ingests), <c>{reason}</c>, and <c>{duration}</c> ("permanent" for a ban, a human-readable span
/// for a timeout). Grouped in one file because the five handlers are one surface — the same variable contract over
/// the moderation events.
/// </summary>
public sealed class UserBannedAlertHandler
    : TwitchAlertHandlerBase<UserBannedEvent>,
        IEventHandler<UserBannedEvent>
{
    protected override string EventTypeKey => "channel.ban";

    public UserBannedAlertHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<UserBannedAlertHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(UserBannedEvent e) => e.TargetUserId;

    protected override string? GetUserDisplayName(UserBannedEvent e) => e.TargetDisplayName;

    protected override Dictionary<string, string> BuildVariables(UserBannedEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.TargetDisplayName,
            ["user.id"] = e.TargetUserId,
            ["moderator"] = e.ModeratorDisplayName ?? e.ModeratorUserId,
            ["reason"] = e.Reason ?? string.Empty,
            ["duration"] = "permanent",
        };

    protected override UserBannedEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            TargetUserId = "100000008",
            TargetDisplayName = "SpamBot42",
            ModeratorUserId = "100000007",
            ModeratorDisplayName = "ModMaya",
            Reason = "Posting spam links",
        };

    public Task HandleAsync(UserBannedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>The timeout leg of <c>channel.ban</c> — <c>{duration}</c> carries the timeout seconds.</summary>
public sealed class UserTimedOutAlertHandler
    : TwitchAlertHandlerBase<UserTimedOutEvent>,
        IEventHandler<UserTimedOutEvent>
{
    protected override string EventTypeKey => "channel.ban";

    public UserTimedOutAlertHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<UserTimedOutAlertHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(UserTimedOutEvent e) => e.TargetUserId;

    protected override string? GetUserDisplayName(UserTimedOutEvent e) => e.TargetDisplayName;

    protected override Dictionary<string, string> BuildVariables(UserTimedOutEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.TargetDisplayName,
            ["user.id"] = e.TargetUserId,
            ["moderator"] = e.ModeratorDisplayName ?? e.ModeratorUserId,
            ["reason"] = e.Reason ?? string.Empty,
            ["duration"] = HumanDuration(e.DurationSeconds),
        };

    protected override UserTimedOutEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            TargetUserId = "100000009",
            TargetDisplayName = "HeatedHarry",
            ModeratorUserId = "100000007",
            ModeratorDisplayName = "ModMaya",
            DurationSeconds = 600,
            Reason = "Heated argument in chat",
        };

    public Task HandleAsync(UserTimedOutEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>The <c>channel.unban</c> trigger source — no reason/duration on Twitch's unban notice.</summary>
public sealed class UserUnbannedAlertHandler
    : TwitchAlertHandlerBase<UserUnbannedEvent>,
        IEventHandler<UserUnbannedEvent>
{
    protected override string EventTypeKey => "channel.unban";

    public UserUnbannedAlertHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<UserUnbannedAlertHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(UserUnbannedEvent e) => e.TargetUserId;

    protected override string? GetUserDisplayName(UserUnbannedEvent e) => e.TargetDisplayName;

    protected override Dictionary<string, string> BuildVariables(UserUnbannedEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.TargetDisplayName ?? e.TargetUserId,
            ["user.id"] = e.TargetUserId,
            ["moderator"] = e.ModeratorDisplayName ?? e.ModeratorUserId,
        };

    protected override UserUnbannedEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            TargetUserId = "100000008",
            TargetDisplayName = "SpamBot42",
            ModeratorUserId = "100000007",
            ModeratorDisplayName = "ModMaya",
        };

    public Task HandleAsync(UserUnbannedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>The <c>channel.moderator.add</c> trigger source — {user} is the new moderator.</summary>
public sealed class ModeratorAddedAlertHandler
    : TwitchAlertHandlerBase<ModeratorAddedEvent>,
        IEventHandler<ModeratorAddedEvent>
{
    protected override string EventTypeKey => "channel.moderator.add";

    public ModeratorAddedAlertHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<ModeratorAddedAlertHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(ModeratorAddedEvent e) => e.UserId;

    protected override string? GetUserDisplayName(ModeratorAddedEvent e) => e.UserDisplayName;

    protected override Dictionary<string, string> BuildVariables(ModeratorAddedEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.UserDisplayName,
            ["user.id"] = e.UserId,
        };

    protected override ModeratorAddedEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            UserId = "100000007",
            UserDisplayName = "ModMaya",
            UserLogin = "modmaya",
        };

    public Task HandleAsync(ModeratorAddedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>The <c>channel.moderator.remove</c> trigger source — {user} is the user who lost the role.</summary>
public sealed class ModeratorRemovedAlertHandler
    : TwitchAlertHandlerBase<ModeratorRemovedEvent>,
        IEventHandler<ModeratorRemovedEvent>
{
    protected override string EventTypeKey => "channel.moderator.remove";

    public ModeratorRemovedAlertHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<ModeratorRemovedAlertHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(ModeratorRemovedEvent e) => e.UserId;

    protected override string? GetUserDisplayName(ModeratorRemovedEvent e) => e.UserDisplayName;

    protected override Dictionary<string, string> BuildVariables(ModeratorRemovedEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.UserDisplayName,
            ["user.id"] = e.UserId,
        };

    protected override ModeratorRemovedEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            UserId = "100000007",
            UserDisplayName = "ModMaya",
            UserLogin = "modmaya",
        };

    public Task HandleAsync(ModeratorRemovedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
