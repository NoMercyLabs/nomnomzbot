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
/// One channel's share of a moderator's mass ban (chat-client.md §3.5). Every ban rides the moderator's own token,
/// so Twitch still decides where it is allowed. A batch for a channel that is not the moderator's own and not the
/// attacked one waits while that channel is live: it runs when someone types <c>!allow massban</c> there, or when
/// the stream ends. <c>!disallow massban</c> stops it. Deliberately NOT tenant-filtered, like
/// <see cref="NetworkNukeBatch"/>: the channel may not be a tenant at all (the bot need not serve it).
/// </summary>
public class MassBanBatch : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The moderator who asked; every ban is issued with this user's token.</summary>
    public Guid OperatorUserId { get; set; }

    public string OperatorDisplayName { get; set; } = string.Empty;

    /// <summary>The channel's Twitch id: the <c>broadcaster_id</c> of every ban.</summary>
    public string ChannelTwitchId { get; set; } = string.Empty;

    public string ChannelLogin { get; set; } = string.Empty;

    /// <summary>The local channel when the bot serves it, so its chat can be told and can answer.</summary>
    public Guid? ChannelId { get; set; }

    /// <summary>False for the moderator's own channel and the attacked channel: those never wait.</summary>
    public bool HoldWhileLive { get; set; }

    /// <summary>
    /// True when the bans ride the channel's own broadcaster token instead of the operator's: Twitch does not
    /// list the operator as a moderator there, but the channel joined the bot and its owner opted in.
    /// </summary>
    public bool RunsAsBroadcaster { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>When the channel's chat was asked; only an asked chat gets the closing line.</summary>
    public DateTime? NoticeSentAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? DeclinedAt { get; set; }

    /// <summary>Who typed <c>!allow massban</c> or <c>!disallow massban</c>.</summary>
    public string? DecidedByDisplayName { get; set; }

    /// <summary>Set when every target has been processed.</summary>
    public DateTime? CompletedAt { get; set; }

    public List<MassBanBatchTarget> Targets { get; set; } = [];
}

/// <summary>One account inside a <see cref="MassBanBatch"/>, with its own ban outcome.</summary>
public class MassBanBatchTarget
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MassBanBatchId { get; set; }

    public string TwitchUserId { get; set; } = string.Empty;

    /// <summary>The ban reason Twitch shows, e.g. the channel and moment the account followed.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Set once the ban was attempted, so a restart resumes where it stopped.</summary>
    public DateTime? ProcessedAt { get; set; }

    public bool Banned { get; set; }

    public string? Error { get; set; }
}
