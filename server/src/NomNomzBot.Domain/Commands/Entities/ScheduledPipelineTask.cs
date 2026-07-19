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

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// One deferred, one-shot pipeline run: "execute pipeline <see cref="PipelineId"/> once, at <see cref="DueAt"/>,
/// with <see cref="VariablesJson"/> as its initial variables." The generic primitive behind timed follow-ups —
/// a voice-swap auto-revert, a feather auto-hide, a timed reward — that must survive a process restart (the row
/// is the durable record; a background sweeper fires it when due). Unlike a <c>Timer</c> (recurring interval) or
/// a <c>RedemptionTimer</c> (reward-scoped countdown), this expresses a single delayed dispatch.
///
/// Lifecycle is a status machine (NOT a delete): a row is created <see cref="ScheduledPipelineTaskStatus.Pending"/>
/// and transitions terminal to <see cref="ScheduledPipelineTaskStatus.Fired"/> (dispatched),
/// <see cref="ScheduledPipelineTaskStatus.Cancelled"/> (revoked before firing), or
/// <see cref="ScheduledPipelineTaskStatus.Expired"/> (still due but too stale to run — e.g. after a long
/// downtime). Terminal rows are kept (not soft-deleted) so the sweeper's status-filter is the natural
/// idempotency guard (a fired row can never fire twice) and so a short audit trail survives; there is no
/// <c>DeletedAt</c> — a periodic prune of old terminal rows is a separate concern.
/// </summary>
public class ScheduledPipelineTask : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    /// <summary>
    /// The target pipeline, resolved to its Guid at schedule time for determinism (the id the sweeper dispatches).
    /// </summary>
    public Guid PipelineId { get; set; }

    /// <summary>
    /// The target pipeline's name at schedule time — a bundle-portable fallback for display/diagnostics; the
    /// <see cref="PipelineId"/> stays authoritative for dispatch.
    /// </summary>
    [MaxLength(200)]
    public string? PipelineName { get; set; }

    /// <summary>When the deferred run is due (UTC). The sweeper fires pending rows whose <c>DueAt</c> has passed.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>
    /// The initial variables handed to the deferred run, serialized as a JSON object of string→string (the
    /// captured context from the scheduling call). Empty object when there are none.
    /// </summary>
    public string VariablesJson { get; set; } = "{}";

    /// <summary>The triggering viewer id carried through to the deferred run (so it runs with the same actor).</summary>
    [MaxLength(100)]
    public string TriggeredByUserId { get; set; } = string.Empty;

    [MaxLength(255)]
    public string TriggeredByDisplayName { get; set; } = string.Empty;

    /// <summary>pending | fired | cancelled | expired — see <see cref="ScheduledPipelineTaskStatus"/>.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = ScheduledPipelineTaskStatus.Pending;

    /// <summary>
    /// Optional caller-supplied idempotency key. While a task is <see cref="ScheduledPipelineTaskStatus.Pending"/>,
    /// re-scheduling with the same key REPLACES the pending row instead of stacking a second (a partial unique
    /// index on <c>(BroadcasterId, DedupeKey) WHERE Status = 'pending'</c> enforces at most one live per key).
    /// </summary>
    [MaxLength(200)]
    public string? DedupeKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the task reaches a terminal state (fired / expired); null while pending or cancelled.</summary>
    public DateTimeOffset? FiredAt { get; set; }
}

/// <summary>The <see cref="ScheduledPipelineTask.Status"/> vocabulary.</summary>
public static class ScheduledPipelineTaskStatus
{
    public const string Pending = "pending";
    public const string Fired = "fired";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
}
