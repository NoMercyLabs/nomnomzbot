// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Commands.Dtos;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The canonical event-response catalog: every configurable event type with a ready-to-use default
/// template and the EXACT template variables its trigger source seeds (verified against each handler's
/// <c>BuildVariables</c> — a preset must never advertise a placeholder the event won't fill). The
/// dashboard pre-fills the template input from <see cref="Presets"/>; the lazy per-channel seeding uses
/// <see cref="EventTypes"/>, so the seeded rows and the catalog can never drift apart.
/// </summary>
/// <remarks>
/// The default template is a translation KEY (<see cref="LocalizedText"/>), derived from the event type so it
/// cannot drift from it: <c>channel.follow</c> → <c>eventresponse.preset.channel_follow.template</c>. The
/// English and Dutch sentences live exclusively in the dashboard's <c>strings.xml</c> / <c>values-nl/</c>
/// (S-SCHEMA-I18N-redesign); this assembly carries no user-facing prose.
/// </remarks>
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
        Preset("channel.follow", ["user", "user.id", "user.name", "followed_at"]),
        Preset("channel.subscribe", ["user", "user.id", "tier"]),
        Preset(
            "channel.subscription.message",
            ["user", "user.id", "tier", "months", "streak", "message"]
        ),
        Preset("channel.subscription.gift", ["user", "user.id", "tier", "count", "anonymous"]),
        Preset("channel.cheer", ["user", "user.id", "bits", "message", "anonymous"]),
        Preset("channel.raid", ["user", "user.id", "user.name", "viewers"]),
        Preset(
            "channel.channel_points_custom_reward_redemption.add",
            ["user", "user.id", "reward", "reward.id", "redemption.id", "cost", "input"]
        ),
        // Reward state transitions — derived by RewardLifecycleHandler from the custom-reward.update feed
        // (old locally-synced state vs the incoming Twitch state; only actual flips fire).
        Preset("reward.paused", ["reward", "reward.id", "cost"]),
        Preset("reward.resumed", ["reward", "reward.id", "cost"]),
        Preset("reward.enabled", ["reward", "reward.id", "cost"]),
        Preset("reward.disabled", ["reward", "reward.id", "cost"]),
        // Ad breaks — {user} is the requester (empty on an automatic break).
        Preset("channel.ad_break.begin", ["user", "user.id", "ad.duration", "ad.automatic"]),
        // Moderation notices — channel.ban covers bans AND timeouts ({duration} = "permanent" or seconds).
        Preset("channel.ban", ["user", "user.id", "moderator", "reason", "duration"]),
        Preset("channel.unban", ["user", "user.id", "moderator"]),
        // Outgoing raid (channel.moderate's raid action) — {user} names the TARGET channel being raided.
        Preset("channel.raid.out", ["user", "user.id", "user.name", "viewers"]),
        Preset("stream.online", ["broadcaster", "title", "game"]),
        Preset("stream.offline", ["broadcaster", "duration"]),
        Preset("engagement.first_time_chatter", ["user", "user.id", "viewer.name"]),
        Preset(
            "engagement.returning_chatter",
            ["user", "user.id", "viewer.name", "engagement.daysSinceLastSeen"]
        ),
        Preset("engagement.watch_streak", ["user", "user.id", "viewer.name", "engagement.streak"]),
        Preset("engagement.session_first_message", ["user", "user.id", "viewer.name"]),
        // NO engagement.modiversary: Twitch exposes no mod-anniversary signal anywhere — it is not among
        // channel.chat.notification's notice types and Helix Get Moderators carries no granted-at date, so
        // there is no truthful data to fire it from. Deliberately absent rather than faked.
        Preset("supporter.tip", SupporterVariables),
        Preset("supporter.membership", SupporterVariables),
        Preset("supporter.merch", SupporterVariables),
        Preset("supporter.charity", SupporterVariables),
        Preset("supporter.any", SupporterVariables),
        // OBS events (obs-control.md §6) — fields arrive as {obs.event.<name>} from the trigger source.
        Preset("obs.CurrentProgramSceneChanged", ["obs.event.type", "obs.event.sceneName"]),
        Preset(
            "obs.StreamStateChanged",
            ["obs.event.type", "obs.event.outputActive", "obs.event.outputState"]
        ),
        Preset(
            "obs.RecordStateChanged",
            ["obs.event.type", "obs.event.outputActive", "obs.event.outputState"]
        ),
        Preset("obs.ReplayBufferSaved", ["obs.event.type", "obs.event.savedReplayPath"]),
        Preset(
            "obs.VendorEvent",
            ["obs.event.type", "obs.event.vendorName", "obs.event.eventType"]
        ),
        // VTube Studio events (vtube-studio.md §4) — fields arrive as {vts.event.<name>}.
        Preset(
            "vts.ModelLoadedEvent",
            ["vts.event.type", "vts.event.modelName", "vts.event.modelID"]
        ),
        Preset(
            "vts.HotkeyTriggeredEvent",
            ["vts.event.type", "vts.event.hotkeyID", "vts.event.hotkeyName"]
        ),
        Preset(
            "vts.ModelClickedEvent",
            ["vts.event.type", "vts.event.modelLoaded", "vts.event.mouseButtonID"]
        ),
    ];

    /// <summary>The configurable event-type keys, in catalog order — the per-channel seeding set.</summary>
    public static IReadOnlyList<string> EventTypes { get; } = [.. Presets.Select(p => p.EventType)];

    /// <summary>The translation key an event type's default template is published under.</summary>
    public static string TemplateKey(string eventType) =>
        $"eventresponse.preset.{eventType.Replace('.', '_')}.template";

    private static EventResponsePresetDto Preset(
        string eventType,
        IReadOnlyList<string> variables
    ) => new(eventType, new LocalizedText(TemplateKey(eventType)), variables);
}
