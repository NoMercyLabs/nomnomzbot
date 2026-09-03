// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// One recorded spam-defence verdict (spam-defense.md §6.2, SD7).
///
/// <para>This row is the whole safety story. During dry run — the shipped default, and the state a
/// channel spends its first week in — every layer evaluates and this is the only thing that happens.
/// The operator reads a week of these and sees a wrongly-caught regular <i>in a list</i> rather than in
/// an apology, then decides whether to switch enforcement on.</para>
///
/// <para>Because of that it stores <see cref="WouldHaveBeen"/> alongside <see cref="Outcome"/>, and a
/// <see cref="Reason"/> for every verdict including the ones where nothing happened. Per SD7 there are
/// no black-box decisions: a moderator can always see that the system looked and chose not to act.</para>
/// </summary>
public class SpamDetection : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>Platform-native id of the account that sent the message.</summary>
    public string SubjectPlatformUserId { get; set; } = string.Empty;

    /// <summary>Display name at the time, so a review a week later is readable.</summary>
    public string SubjectDisplayName { get; set; } = string.Empty;

    /// <summary>Platform the message arrived on.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Platform message id, so the message can still be deleted or restored on review.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// The message as sent. Kept because a review queue that cannot show what was said is one a
    /// moderator has to guess at — and per §4 this is exactly the field that is NEVER sent to the
    /// signature network.
    /// </summary>
    public string MessageText { get; set; } = string.Empty;

    /// <summary>The normalized form the engine actually matched on.</summary>
    public string Skeleton { get; set; } = string.Empty;

    /// <summary>Which content signals fired, comma-separated.</summary>
    public string Signals { get; set; } = string.Empty;

    public SpamConfidence Confidence { get; set; }

    /// <summary>The trust tier the sender held at evaluation time.</summary>
    public SpamTrustTier Tier { get; set; }

    /// <summary>What actually happened. <see cref="SpamOutcome.None"/> throughout dry run.</summary>
    public SpamOutcome Outcome { get; set; }

    /// <summary>What would have happened with enforcement on.</summary>
    public SpamOutcome WouldHaveBeen { get; set; }

    /// <summary>True when this was observed rather than acted on.</summary>
    public bool WasDryRun { get; set; }

    /// <summary>Why, in terms a moderator can act on (SD7).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Set when a moderator reviewed this and disagreed — the false-positive signal.</summary>
    public DateTime? OverturnedAt { get; set; }

    public DateTime DetectedAt { get; set; }
}
