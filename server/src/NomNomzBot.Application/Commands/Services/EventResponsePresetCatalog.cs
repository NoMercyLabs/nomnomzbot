// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Dtos;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The canonical event-response catalog: every configurable event type with a ready-to-use default
/// template and the EXACT template variables its trigger source seeds (verified against each handler's
/// <c>BuildVariables</c> — a preset must never advertise a placeholder the event won't fill). The
/// dashboard pre-fills the template input from <see cref="Presets"/>; the lazy per-channel seeding uses
/// <see cref="EventTypes"/>, so the seeded rows and the catalog can never drift apart.
/// </summary>
public static class EventResponsePresetCatalog
{
    private static readonly string[] SupporterVariables =
    [
        "user",
        "supporter.name",
        "supporter.kind",
        "supporter.amount",
        "supporter.currency",
        "supporter.tier",
        "supporter.quantity",
        "supporter.message",
    ];

    /// <summary>Ordered as the event-responses page groups them: Twitch alerts, stream lifecycle, engagement, supporters.</summary>
    public static IReadOnlyList<EventResponsePresetDto> Presets { get; } =
    [
        new(
            "channel.follow",
            "Thanks for the follow, {user}!",
            ["user", "user.id", "user.name", "followed_at"]
        ),
        new("channel.subscribe", "Welcome to the sub squad, {user}!", ["user", "user.id", "tier"]),
        new(
            "channel.subscription.message",
            "{user} resubscribed — {months} months and counting!",
            ["user", "user.id", "tier", "months", "streak", "message"]
        ),
        new(
            "channel.subscription.gift",
            "{user} just gifted {count} subs — thank you!",
            ["user", "user.id", "tier", "count", "anonymous"]
        ),
        new(
            "channel.cheer",
            "{user} cheered {bits} bits — thank you!",
            ["user", "user.id", "bits", "message", "anonymous"]
        ),
        new(
            "channel.raid",
            "{user} is raiding with {viewers} viewers — welcome, raiders!",
            ["user", "user.id", "user.name", "viewers"]
        ),
        new(
            "channel.channel_points_custom_reward_redemption.add",
            "{user} redeemed {reward}!",
            ["user", "user.id", "reward", "reward.id", "redemption.id", "cost", "input"]
        ),
        new(
            "stream.online",
            "We're live: {title} — playing {game}!",
            ["broadcaster", "title", "game"]
        ),
        new(
            "stream.offline",
            "Stream's over after {duration} — thanks for watching!",
            ["broadcaster", "duration"]
        ),
        new(
            "engagement.first_time_chatter",
            "Welcome to the stream, {user}!",
            ["user", "user.id", "viewer.name"]
        ),
        new(
            "engagement.returning_chatter",
            "Welcome back, {user}!",
            ["user", "user.id", "viewer.name", "engagement.daysSinceLastSeen"]
        ),
        new(
            "engagement.watch_streak",
            "{user} is on a {engagement.streak}-stream watch streak!",
            ["user", "user.id", "viewer.name", "engagement.streak"]
        ),
        new(
            "engagement.session_first_message",
            "Welcome in, {user}!",
            ["user", "user.id", "viewer.name"]
        ),
        new(
            "supporter.tip",
            "{user} tipped {supporter.amount} {supporter.currency} — thank you!",
            SupporterVariables
        ),
        new("supporter.membership", "{user} just became a member — thank you!", SupporterVariables),
        new("supporter.merch", "{user} grabbed some merch — thank you!", SupporterVariables),
        new(
            "supporter.charity",
            "{user} donated {supporter.amount} {supporter.currency} to charity!",
            SupporterVariables
        ),
        new("supporter.any", "{user} just supported the stream — thank you!", SupporterVariables),
        // OBS events (obs-control.md §6) — fields arrive as {obs.event.<name>} from the trigger source.
        new(
            "obs.CurrentProgramSceneChanged",
            "Scene changed to {obs.event.sceneName}",
            ["obs.event.type", "obs.event.sceneName"]
        ),
        new(
            "obs.StreamStateChanged",
            "OBS stream state: {obs.event.outputState}",
            ["obs.event.type", "obs.event.outputActive", "obs.event.outputState"]
        ),
        new(
            "obs.RecordStateChanged",
            "OBS recording state: {obs.event.outputState}",
            ["obs.event.type", "obs.event.outputActive", "obs.event.outputState"]
        ),
        new(
            "obs.ReplayBufferSaved",
            "Replay saved!",
            ["obs.event.type", "obs.event.savedReplayPath"]
        ),
        new(
            "obs.VendorEvent",
            "OBS vendor event from {obs.event.vendorName}",
            ["obs.event.type", "obs.event.vendorName", "obs.event.eventType"]
        ),
        // VTube Studio events (vtube-studio.md §4) — fields arrive as {vts.event.<name>}.
        new(
            "vts.ModelLoadedEvent",
            "Model {vts.event.modelName} loaded!",
            ["vts.event.type", "vts.event.modelName", "vts.event.modelID"]
        ),
        new(
            "vts.HotkeyTriggeredEvent",
            "Hotkey {vts.event.hotkeyName} fired!",
            ["vts.event.type", "vts.event.hotkeyID", "vts.event.hotkeyName"]
        ),
        new(
            "vts.ModelClickedEvent",
            "Someone poked the model!",
            ["vts.event.type", "vts.event.modelLoaded", "vts.event.mouseButtonID"]
        ),
    ];

    /// <summary>The configurable event-type keys, in catalog order — the per-channel seeding set.</summary>
    public static IReadOnlyList<string> EventTypes { get; } = [.. Presets.Select(p => p.EventType)];
}
