// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Community.Entities;

/// <summary>
/// A bot-run chat poll: viewers vote by typing the option number in chat, so it works on EVERY
/// platform (Twitch, YouTube, Kick) with no affiliate gate — unlike the Helix-native polls the
/// live-ops page drives. One poll per channel is open at a time; votes are one per viewer with the
/// LAST vote winning (changing your mind is allowed). Closed polls stay as history.
/// </summary>
public class ChatPoll : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    [MaxLength(200)]
    public string Question { get; set; } = null!;

    /// <summary>JSON array of option labels (2–10), voted by 1-based index.</summary>
    public string OptionsJson { get; set; } = null!;

    /// <summary>open | closed.</summary>
    [MaxLength(10)]
    public string Status { get; set; } = ChatPollStatus.Open;

    /// <summary>Optional auto-close horizon; null = open until closed manually.</summary>
    public DateTime? ClosesAt { get; set; }

    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

/// <summary>The <see cref="ChatPoll.Status"/> vocabulary.</summary>
public static class ChatPollStatus
{
    public const string Open = "open";
    public const string Closed = "closed";
}

/// <summary>
/// One viewer's current vote in a <see cref="ChatPoll"/> — unique per (poll, voter), updated in place
/// when they change their mind.
/// </summary>
public class ChatPollVote : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public Guid PollId { get; set; }

    /// <summary>The voter's platform user id (with <see cref="VoterProvider"/> disambiguating platforms).</summary>
    [MaxLength(64)]
    public string VoterUserId { get; set; } = null!;

    [MaxLength(20)]
    public string VoterProvider { get; set; } = null!;

    /// <summary>1-based option index.</summary>
    public int OptionIndex { get; set; }

    public DateTime VotedAt { get; set; }
}
