// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Events;

/// <summary>
/// Generic dashboard-refresh signal (E5, gap-audit "dashboard live sync"). Published by a config-CRUD service
/// after a successful write so every OTHER open dashboard for the same channel stops going stale — the receiving
/// client just refetches <see cref="Domain"/>'s query, which is cheap and future-proof. One event/hub-push pair
/// covers every config page instead of a bespoke event per domain; runtime/high-frequency state (queue position,
/// now-playing, chat, redemptions, permission changes, reward lifecycle, ...) already has its own dedicated event
/// and does NOT go through this one.
/// </summary>
public sealed class ChannelConfigChangedEvent : DomainEventBase
{
    /// <summary>
    /// The dashboard config page this mutation belongs to — a closed catalogue matching the frontend's config
    /// query keys: commands, timers, pipelines, event-responses, rewards, economy-config, earning-rules, catalog,
    /// moderation-rules, blocked-terms, automod, tts-config, music-config, sr-config, webhooks, widgets, features,
    /// quotes, builtins, channel-settings, roles-permits.
    /// </summary>
    public required string Domain { get; init; }

    /// <summary>The id of the created/updated/deleted/toggled row, or <c>null</c> for a domain-wide change.</summary>
    public string? EntityId { get; init; }

    /// <summary>One of <c>created</c> / <c>updated</c> / <c>deleted</c> / <c>toggled</c>.</summary>
    public required string Action { get; init; }
}
