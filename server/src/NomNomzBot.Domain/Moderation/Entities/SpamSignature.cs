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

/// <summary>Where a signature came from, which decides whether it may act (spam-defense.md §4).</summary>
public enum SignatureSource
{
    /// <summary>Confirmed by this instance's own correlation. Trusted here, and only here.</summary>
    Local,

    /// <summary>Pulled from the shared set with no curator signature — quarantined until corroborated.</summary>
    Network,

    /// <summary>Published by NoMercy with a curator signature. Skips quarantine.</summary>
    Curated,
}

/// <summary>What kind of thing the signature identifies.</summary>
public enum SignatureKind
{
    /// <summary>A normalized message skeleton.</summary>
    Skeleton,

    /// <summary>A domain known to be malicious.</summary>
    Domain,
}

/// <summary>
/// One entry in the signature corpus (spam-defense.md §4).
///
/// <para>Instance-wide rather than per-channel, which is the whole point: a campaign one channel
/// confirms should protect the next channel it hits. It is deliberately NOT tenant-scoped, and equally
/// deliberately carries no message text and no viewer identity — a signature is a skeleton or a domain
/// plus metadata, and nothing else ever leaves a channel.</para>
///
/// <para><b>Quarantine is the anti-poisoning property.</b> An entry from an unproven source only FLAGS
/// until enough independent reporters have seen it. Without that, one malicious contributor could cause
/// a mass removal everywhere at once, which is the worst outcome this system is capable of.</para>
/// </summary>
public class SpamSignature : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public SignatureKind Kind { get; set; }

    /// <summary>The skeleton or domain. Unique per kind.</summary>
    public string Value { get; set; } = string.Empty;

    public SignatureSource Source { get; set; }

    /// <summary>
    /// How many independent reporters have confirmed it. Local confirmations count as one; the
    /// threshold that lets a quarantined entry act is the channel's <c>RequiredCorroborations</c>.
    /// </summary>
    public int Corroborations { get; set; } = 1;

    /// <summary>
    /// True while the entry may only flag. A curated entry is never quarantined; a network entry stops
    /// being quarantined once corroborated.
    /// </summary>
    public bool IsQuarantined { get; set; }

    /// <summary>
    /// Set when a moderator marked this signature wrong. A withdrawn entry never matches again and is
    /// never contributed — a false signature has to be removable, not merely regrettable.
    /// </summary>
    public DateTime? WithdrawnAt { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastConfirmedAt { get; set; }

    /// <summary>Whether this entry may be used to ACT rather than only to flag.</summary>
    public bool CanAct(int requiredCorroborations) =>
        WithdrawnAt is null && (!IsQuarantined || Corroborations >= requiredCorroborations);
}
