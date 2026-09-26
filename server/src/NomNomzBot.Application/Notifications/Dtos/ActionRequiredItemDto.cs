// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Notifications.Dtos;

/// <summary>
/// One row of the dashboard's action-required inbox (S071a) — a real, already-detected condition that needs
/// the streamer's attention, surfaced from an existing signal (never fabricated). <see cref="Kind"/> is a
/// stable machine key the dashboard groups/icons by (e.g. <c>integration_token_dead</c>,
/// <c>held_chat_message</c>); <see cref="Severity"/> is <c>critical</c> | <c>warning</c> | <c>info</c>.
/// <para>
/// The row carries no prose. <see cref="TitleKey"/> and <see cref="MessageKey"/> are dashboard string-resource
/// names, and <see cref="Parameters"/> are the named values those strings format in (a provider key, a widget
/// name, a failure count). The client renders them in the viewer's language; <see cref="Count"/> drives plurals.
/// </para>
/// <para>
/// <see cref="DeepLinkRoute"/> is the dashboard route slug of the page where the condition is fixed
/// (<c>integrations</c>, <c>moderationqueue</c>, <c>rewards</c>, ...), the lower-cased shell route name.
/// </para>
/// <para>
/// <see cref="Id"/> is the item's stable identity (S-OWN22 T2), the key the dismiss endpoint accepts. Each
/// source embeds whatever makes an old dismissal stale into the key, so a NEW occurrence surfaces again: a dead
/// token is <c>token:{connectionId}:{invalidatedAtUtcTicks}</c>, a single held message is
/// <c>held:{queueItemGuid}</c>, a per-user group of held messages is <c>held-user:{sourceUserId}</c>. Held
/// messages from one user are grouped into ONE item: <see cref="Count"/> pending holds, all of them in
/// <see cref="QueueItemIds"/>, with <see cref="SourceUserId"/>/<see cref="SourceUserName"/> naming the sender.
/// </para>
/// </summary>
public sealed record ActionRequiredItemDto(
    string Id,
    string Kind,
    string Severity,
    string TitleKey,
    string MessageKey,
    Dictionary<string, string> Parameters,
    DateTime DetectedAt,
    string DeepLinkRoute,
    string? SourceUserId,
    string? SourceUserName,
    int Count,
    List<Guid> QueueItemIds
);
