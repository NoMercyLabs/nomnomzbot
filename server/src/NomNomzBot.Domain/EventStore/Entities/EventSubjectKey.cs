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

namespace NomNomzBot.Domain.EventStore.Entities;

/// <summary>
/// Per-subject DEK link for multi-subject journal events (schema O.1a) — a gift sub or raid carries PII of
/// two people, so each subject's payload slice is sealed under that subject's own DEK and mapped here.
/// Erasing one subject then shreds only their slice: the erasure planner
/// (<c>ISubjectKeyService.ResolveSubjectKeysAsync</c>) folds every <see cref="SubjectKeyId"/> mapped to the
/// subject's hash into the crypto-shred set, leaving the other participant's slice readable.
/// Unique per <c>(EventId, SubjectKeyId)</c>; append-only beside the journal (no soft delete, no timestamps
/// per the locked O.1a field list).
/// </summary>
public class EventSubjectKey
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The journal event carrying the sealed slice (→ <see cref="EventJournal.EventId"/>).</summary>
    public Guid EventId { get; set; }

    /// <summary>Owning tenant of the journal event; null = platform-global (mirrors the journal row).</summary>
    public Guid? BroadcasterId { get; set; }

    /// <summary>The hashed subject whose payload slice is sealed (64-hex keyed hash, never a raw id).</summary>
    [MaxLength(64)]
    public string SubjectIdHash { get; set; } = null!;

    /// <summary>The subject's DEK sealing their slice (FK → <c>CryptoKey</c>).</summary>
    public Guid SubjectKeyId { get; set; }

    /// <summary><c>gifter</c> | <c>recipient</c> | <c>raider</c> | <c>raided</c> (schema [VC:enum]); null when unclassified.</summary>
    [MaxLength(20)]
    public string? Role { get; set; }
}
