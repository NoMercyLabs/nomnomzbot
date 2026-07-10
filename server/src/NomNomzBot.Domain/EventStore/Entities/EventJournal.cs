// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.EventStore.Entities;

/// <summary>
/// The append-only outcome/fact log (schema O.1) — the durable record of <em>what happened</em> and the
/// sole replay/projection source of truth. Rows are immutable: written once, never updated or deleted.
/// <para>
/// Append-only convention (schema §1, event-store §spec): <c>bigint</c> identity PK (global append order),
/// <c>RecordedAt</c>/<c>OccurredAt</c> timestamps only — no <c>UpdatedAt</c>, no soft-delete. Does NOT inherit
/// <c>BaseEntity</c> and does NOT implement <c>ITenantScoped</c>: <see cref="BroadcasterId"/> is nullable
/// (<c>null</c> = platform-global) and the journal is read across tenants during replay by design, so it is
/// excluded from the ambient tenant query filter. Tenant isolation for reads is enforced in the service layer.
/// </para>
/// </summary>
public class EventJournal
{
    /// <summary>Global append order. Database identity (<c>bigint</c>); assigned on insert.</summary>
    public long Id { get; set; }

    /// <summary>Idempotent dedupe key (EventSub/domain event id). Unique across the journal.</summary>
    public Guid EventId { get; set; }

    /// <summary>Owning tenant; <c>null</c> = platform-global. Not ambient-filtered (see class remarks).</summary>
    public Guid? BroadcasterId { get; set; }

    /// <summary>
    /// Per-tenant monotonic sequence — app-assigned under the per-tenant lock via <c>TenantSequences</c>
    /// (Q.3), NOT a DB sequence. Unique with <see cref="BroadcasterId"/>; drives projections/replay.
    /// </summary>
    public long StreamPosition { get; set; }

    /// <summary>The event type discriminator, e.g. <c>channel.chat.message</c>, <c>economy.balance.credited</c>.</summary>
    public string EventType { get; set; } = null!;

    /// <summary>The schema version of the stored payload — the upcaster chain anchor.</summary>
    public int EventVersion { get; set; }

    /// <summary><c>eventsub</c>|<c>domain</c>|<c>irc</c>|<c>import</c>|<c>federation</c>|<c>webhook</c>.</summary>
    public string Source { get; set; } = null!;

    /// <summary>Serialized event body (JSON string). Holds ids/refs; raw PII is encrypted under a DEK.</summary>
    public string Payload { get; set; } = null!;

    /// <summary>True when <see cref="Payload"/> holds PII encrypted under a per-subject DEK.</summary>
    public bool PayloadIsEncrypted { get; set; }

    /// <summary>Primary/single-subject DEK (FK→CryptoKey) encrypting PII in the payload; multi-subject sets live in EventSubjectKeys.</summary>
    public Guid? SubjectKeyId { get; set; }

    /// <summary>Trace correlation id (groups a causal chain of events).</summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>The event id that caused this event (lineage).</summary>
    public Guid? CausationId { get; set; }

    /// <summary>Internal surrogate of the actor (FK→Users).</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// The actor's platform-specific external user id (hashed PII when present); <c>null</c> for
    /// historical/system rows with no external actor. Interpreted under <see cref="ActorProvider"/>.
    /// </summary>
    public string? ActorExternalUserId { get; set; }

    /// <summary>
    /// The platform key naming <see cref="ActorExternalUserId"/>'s namespace —
    /// <c>twitch</c>|<c>kick</c>|<c>youtube</c>|<c>twitter</c> (the provider vocabulary shared with
    /// <c>Channel.Provider</c> / <c>UserIdentity</c>); <c>null</c> when there is no external actor.
    /// </summary>
    public string? ActorProvider { get; set; }

    /// <summary>Serialized headers/trace metadata (JSON string).</summary>
    public string Metadata { get; set; } = null!;

    /// <summary>Domain time — when the event actually occurred.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Ingest time — when the journal recorded the row.</summary>
    public DateTime RecordedAt { get; set; }
}
