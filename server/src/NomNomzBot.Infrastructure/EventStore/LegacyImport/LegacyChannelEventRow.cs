// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.EventStore.LegacyImport;

/// <summary>
/// One row of the legacy NoMercy bot's single <c>ChannelEvents</c> table (C#/EF/SQLite). Every channel event was
/// funnelled through this generic shape: <see cref="Id"/> is the Twitch EventSub message-id (the stable, idempotent
/// key the import derives its <c>EventId</c> from), <see cref="Type"/> is the free-text Twitch topic discriminator
/// (e.g. <c>channel.follow</c>), and <see cref="Data"/> is the TwitchLib event payload serialized to JSON. The
/// timestamp columns hold the legacy DB upsert time, not the Twitch event time — the real event time lives inside
/// <see cref="Data"/> (e.g. <c>FollowedAt</c>, <c>RedeemedAt</c>), so the mapper prefers the in-payload time.
/// </summary>
public sealed record LegacyChannelEventRow(
    string Id,
    string? ChannelId,
    string? UserId,
    string Type,
    string? Data,
    DateTime CreatedAt
);
