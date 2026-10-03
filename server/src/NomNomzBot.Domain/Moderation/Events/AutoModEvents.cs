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
/// A chat message was held by AutoMod for moderator review (<c>automod.message.hold</c> v2). Carries the
/// flattened message text (the v2 payload nests <c>message.fragments</c>; the translator concatenates them),
/// the offending category and its level, and when it was held.
/// </summary>
public sealed class AutoModMessageHeldEvent : DomainEventBase
{
    /// <summary>The Twitch id of the held chat message.</summary>
    public required string MessageId { get; init; }

    /// <summary>The Twitch user id of the chatter who wrote the message (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The chatter's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The chatter's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>The full text of the held message.</summary>
    public required string Text { get; init; }

    /// <summary>The AutoMod category that flagged the message, as Twitch names it.</summary>
    public required string Category { get; init; }

    /// <summary>How strongly AutoMod flagged the message, as a level number. A higher number means a stronger flag.</summary>
    public required int Level { get; init; }

    /// <summary>When AutoMod held the message, in UTC time.</summary>
    public required DateTimeOffset HeldAt { get; init; }
}

/// <summary>
/// A held message's review was resolved (<c>automod.message.update</c> v2): a moderator approved or denied it,
/// or it expired. <see cref="Status"/> is the raw Twitch verdict (<c>approved</c>/<c>denied</c>/<c>expired</c>).
/// </summary>
public sealed class AutoModMessageUpdatedEvent : DomainEventBase
{
    /// <summary>The Twitch id of the held chat message.</summary>
    public required string MessageId { get; init; }

    /// <summary>The Twitch user id of the chatter who wrote the message (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The chatter's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The chatter's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>The Twitch user id of the moderator who reviewed the message (a number as text).</summary>
    public required string ModeratorId { get; init; }

    /// <summary>The moderator's display name, as shown in chat.</summary>
    public required string ModeratorDisplayName { get; init; }

    /// <summary>The review result: approved, denied or expired.</summary>
    public required string Status { get; init; }
}

/// <summary>
/// The channel's AutoMod sensitivity settings changed (<c>automod.settings.update</c>). <see cref="OverallLevel"/>
/// is null when the broadcaster uses per-category levels rather than a single overall level; in that case the
/// per-category fields carry the active levels.
/// </summary>
public sealed class AutoModSettingsUpdatedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the moderator who changed the settings (a number as text).</summary>
    public required string ModeratorId { get; init; }

    /// <summary>The moderator's display name, as shown in chat.</summary>
    public required string ModeratorDisplayName { get; init; }

    /// <summary>The single AutoMod level for all categories. Empty when the channel sets a level per category.</summary>
    public required int? OverallLevel { get; init; }

    /// <summary>The AutoMod level for bullying.</summary>
    public required int Bullying { get; init; }

    /// <summary>The AutoMod level for aggression.</summary>
    public required int Aggression { get; init; }

    /// <summary>The AutoMod level for sexuality.</summary>
    public required int Sexuality { get; init; }

    /// <summary>The AutoMod level for disability.</summary>
    public required int Disability { get; init; }

    /// <summary>The AutoMod level for misogyny.</summary>
    public required int Misogyny { get; init; }

    /// <summary>The AutoMod level for race, ethnicity or religion.</summary>
    public required int RaceEthnicityOrReligion { get; init; }

    /// <summary>The AutoMod level for sex-based terms.</summary>
    public required int SexBasedTerms { get; init; }

    /// <summary>The AutoMod level for swearing.</summary>
    public required int Swearing { get; init; }
}

/// <summary>
/// A permitted/blocked AutoMod term list changed (<c>automod.terms.update</c>). <see cref="Action"/> is the raw
/// Twitch action (<c>add_permitted</c>/<c>remove_permitted</c>/<c>add_blocked</c>/<c>remove_blocked</c>);
/// <see cref="FromAutomod"/> indicates whether AutoMod itself (rather than the moderator) sourced the terms.
/// </summary>
public sealed class AutoModTermsUpdatedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the moderator who changed the list (a number as text).</summary>
    public required string ModeratorId { get; init; }

    /// <summary>The moderator's display name, as shown in chat.</summary>
    public required string ModeratorDisplayName { get; init; }

    /// <summary>What changed: add_permitted, remove_permitted, add_blocked or remove_blocked.</summary>
    public required string Action { get; init; }

    /// <summary>True when AutoMod itself added the terms. False when a moderator added them.</summary>
    public required bool FromAutomod { get; init; }

    /// <summary>The words or phrases that were added or removed.</summary>
    public required IReadOnlyList<string> Terms { get; init; }
}
