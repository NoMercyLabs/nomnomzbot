// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// A moderator's word that a streamer allowed their channel into the moderator's mass bans (chat-client.md §3.5).
/// Owner 2026-10-06: a mass ban touches only channels that opted in; a channel that never joined the bot cannot
/// set <c>Channel.AcceptsModeratorMassBans</c>, so its moderator records the permission here, on the streamer's
/// word. Scoped to the moderator who recorded it: another moderator of the same channel needs their own.
/// Not tenant-filtered: the channel need not be a tenant at all.
/// </summary>
public class ModeratorMassBanOptIn : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The moderator whose mass bans this opt-in covers.</summary>
    public Guid OperatorUserId { get; set; }

    public string BroadcasterTwitchId { get; set; } = string.Empty;

    public string BroadcasterLogin { get; set; } = string.Empty;

    /// <summary>Where the permission came from, in the moderator's words ("asked in Discord 2026-10-06").</summary>
    public string Note { get; set; } = string.Empty;

    public DateTime RecordedAt { get; set; }
}
