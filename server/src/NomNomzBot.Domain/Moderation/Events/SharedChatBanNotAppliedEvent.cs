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

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>
/// A shared ban that this channel should have placed was NOT placed (moderation.md §3.5). The channel
/// opted in to the trust web, so the streamer is owed the reason — the Twitch refusal, a missing shared-chat
/// session, or an origin that is not on the trust list. <c>BroadcasterId</c> = the channel that did NOT ban.
/// The action-required inbox reads this event back from the journal.
/// </summary>
public sealed class SharedChatBanNotAppliedEvent : DomainEventBase
{
    /// <summary>Why the ban was not placed — one of <see cref="SharedBanNotAppliedReasons"/>.</summary>
    public required string Reason { get; init; }

    /// <summary>Where the ban came from — <c>shared_chat</c> (Twitch session) or <c>federation</c> (peer instance).</summary>
    public required string Origin { get; init; }

    /// <summary>The channel the ban was issued in.</summary>
    public required Guid OriginChannelId { get; init; }

    /// <summary>The Twitch user id of the viewer who should have been banned (a number as text).</summary>
    public required string TargetTwitchUserId { get; init; }

    /// <summary>The viewer's display name, as shown in chat. Empty when it is not known.</summary>
    public string? TargetDisplayName { get; init; }

    /// <summary>The platform's own words for a refusal (code and message). Empty for the non-platform reasons.</summary>
    public string? Detail { get; init; }
}

/// <summary>The closed set of reasons a shared ban is not placed. These are also the apply path's error codes.</summary>
public static class SharedBanNotAppliedReasons
{
    /// <summary>The channel never opted in to accepting shared bans. A choice, so it is never reported.</summary>
    public const string NotAccepting = "not_accepting";

    /// <summary>The channel accepts shared bans but the origin is not on its trust list.</summary>
    public const string OriginNotTrusted = "origin_not_trusted";

    /// <summary>The channel is not in the same live shared-chat session as the origin.</summary>
    public const string NoSharedSession = "no_shared_session";

    /// <summary>Twitch refused the ban (missing scope, not a moderator, already banned, and so on).</summary>
    public const string TwitchBanFailed = "twitch_ban_failed";
}
