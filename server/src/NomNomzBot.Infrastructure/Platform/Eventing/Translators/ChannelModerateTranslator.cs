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
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Translates <c>channel.moderate</c> (v2) into a single general <see cref="ModerationActionTakenEvent"/>. This
/// is the catch-all moderation feed: every payload carries the <c>moderator_user_*</c> fields plus an
/// <c>action</c> string (<c>ban</c>, <c>timeout</c>, <c>unban</c>, <c>delete</c>, <c>clear</c>, <c>emoteonly</c>,
/// <c>followers</c>, <c>slow</c>, <c>subscribers</c>, <c>raid</c>, <c>unraid</c>, <c>mod</c>, <c>unmod</c>,
/// <c>vip</c>, <c>unvip</c>, <c>warn</c>, …) and, for actions that target a chatter, ONE nested object named
/// after the action (e.g. the <c>ban</c> action carries a <c>ban</c> object with <c>user_id</c>/<c>user_login</c>/
/// <c>user_name</c>). Rather than enumerate all ~40 actions as distinct events, this maps the common envelope
/// (<c>action</c> + moderator) and, when the action's nested object names a target chatter, lifts that target
/// user and any nested <c>reason</c> into the event. Actions with no target (settings toggles like
/// <c>emoteonly</c>/<c>slow</c>) publish with an empty <see cref="ModerationActionTakenEvent.TargetUserId"/>.
/// </summary>
public sealed class ChannelModerateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.moderate";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        string action = payload.GetRequiredString("action");

        // The action-specific detail object is named after the action itself (the `ban` action carries a `ban`
        // object, `warn` a `warn` object, …). When present it names the affected chatter and may carry a reason;
        // settings-only actions have no such object, so the target degrades to empty.
        JsonElement? detail = payload.GetObject(action);

        ModerationActionTakenEvent moderated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            ChannelId = notification.TwitchBroadcasterUserId,
            ModeratorId = payload.GetRequiredString("moderator_user_id"),
            ActionType = action,
            TargetUserId = detail?.GetRequiredString("user_id") ?? string.Empty,
            Reason = detail?.GetString("reason"),
        };

        return PublishAsync(moderated, ct);
    }
}
