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
/// <see cref="Id"/> is the item's stable identity (S-OWN22 T2), the key the dismiss endpoint accepts: a
/// single held message is <c>held:{queueItemGuid}</c>, a per-user group of held messages is
/// <c>held-user:{sourceUserId}</c>, and a dead token is <c>token:{connectionId}:{invalidatedAtUtcTicks}</c>
/// (re-invalidation after a fix mints a NEW key, so an old dismissal cannot hide it). Held messages from one
/// user are grouped into ONE item: <see cref="Count"/> pending holds, all of them in
/// <see cref="QueueItemIds"/>, with <see cref="SourceUserId"/>/<see cref="SourceUserName"/> naming the
/// sender. Dead-token items keep <see cref="Count"/> = 1 and an empty <see cref="QueueItemIds"/>.
/// </para>
/// </summary>
public sealed record ActionRequiredItemDto(
    string Id,
    string Kind,
    string Severity,
    string Title,
    string Message,
    DateTime DetectedAt,
    string DeepLinkRoute,
    string? SourceUserId,
    string? SourceUserName,
    int Count,
    List<Guid> QueueItemIds
);
