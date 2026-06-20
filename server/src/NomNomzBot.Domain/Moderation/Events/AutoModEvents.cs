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
    public required string MessageId { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
    public required string Text { get; init; }
    public required string Category { get; init; }
    public required int Level { get; init; }
    public required DateTimeOffset HeldAt { get; init; }
}

/// <summary>
/// A held message's review was resolved (<c>automod.message.update</c> v2): a moderator approved or denied it,
/// or it expired. <see cref="Status"/> is the raw Twitch verdict (<c>approved</c>/<c>denied</c>/<c>expired</c>).
/// </summary>
public sealed class AutoModMessageUpdatedEvent : DomainEventBase
{
    public required string MessageId { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
    public required string ModeratorId { get; init; }
    public required string ModeratorDisplayName { get; init; }
    public required string Status { get; init; }
}

/// <summary>
/// The channel's AutoMod sensitivity settings changed (<c>automod.settings.update</c>). <see cref="OverallLevel"/>
/// is null when the broadcaster uses per-category levels rather than a single overall level; in that case the
/// per-category fields carry the active levels.
/// </summary>
public sealed class AutoModSettingsUpdatedEvent : DomainEventBase
{
    public required string ModeratorId { get; init; }
    public required string ModeratorDisplayName { get; init; }
    public required int? OverallLevel { get; init; }
    public required int Bullying { get; init; }
    public required int Aggression { get; init; }
    public required int Sexuality { get; init; }
    public required int Disability { get; init; }
    public required int Misogyny { get; init; }
    public required int RaceEthnicityOrReligion { get; init; }
    public required int SexBasedTerms { get; init; }
    public required int Swearing { get; init; }
}

/// <summary>
/// A permitted/blocked AutoMod term list changed (<c>automod.terms.update</c>). <see cref="Action"/> is the raw
/// Twitch action (<c>add_permitted</c>/<c>remove_permitted</c>/<c>add_blocked</c>/<c>remove_blocked</c>);
/// <see cref="FromAutomod"/> indicates whether AutoMod itself (rather than the moderator) sourced the terms.
/// </summary>
public sealed class AutoModTermsUpdatedEvent : DomainEventBase
{
    public required string ModeratorId { get; init; }
    public required string ModeratorDisplayName { get; init; }
    public required string Action { get; init; }
    public required bool FromAutomod { get; init; }
    public required IReadOnlyList<string> Terms { get; init; }
}
