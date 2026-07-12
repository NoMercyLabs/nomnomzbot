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
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Tts.Entities;

/// <summary>
/// One TTS utterance held for moderator approval (tts.md P.1a). When a channel runs with <c>ModApprovalRequired</c>,
/// a passing request is NOT spoken immediately — it lands here as <c>pending</c> with the already-censored text a
/// moderator will actually hear on approval. A mod then approves (synthesize + play + ledger, status→approved) or
/// rejects (status→rejected, nothing spoken). Truthful: the row records exactly what was requested, what would be
/// spoken, and who reviewed it; it plays nothing on its own.
/// </summary>
public class TtsApprovalQueueEntry : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    /// <summary>The requesting viewer's internal user id; <see cref="System.Guid.Empty"/> for a system/pipeline-triggered utterance.</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>The requesting viewer's raw Twitch id, snapshotted so the row survives an identity change.</summary>
    [MaxLength(50)]
    public string RequestedByTwitchUserId { get; set; } = null!;

    /// <summary>Display-name snapshot for the moderator queue UI.</summary>
    [MaxLength(255)]
    public string? RequestedByDisplayName { get; set; }

    /// <summary>The raw text the viewer asked to have read out.</summary>
    public string OriginalText { get; set; } = null!;

    /// <summary>The post-censor text actually spoken on approval; null when the censor did not alter the text.</summary>
    public string? CensoredText { get; set; }

    [MaxLength(255)]
    public string VoiceId { get; set; } = null!;

    /// <summary>Best-effort provider resolved for the voice at queue time (informational; the ledger records the authoritative one on approval).</summary>
    [MaxLength(20)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Queue state: <c>pending</c> → <c>approved</c> / <c>rejected</c> / <c>expired</c>.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>Whether the censor altered the text (so the UI can flag it).</summary>
    public bool WasCensored { get; set; }

    /// <summary>The moderator who acted (null while pending).</summary>
    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    /// <summary>Originating chat message id, for the moderator to see the surrounding context.</summary>
    [MaxLength(255)]
    public string? SourceMessageId { get; set; }

    public Guid? StreamId { get; set; }

    /// <summary>Stale entries auto-expire (default: queued + 10 minutes) so the queue never fills with dead requests.</summary>
    public DateTime ExpiresAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
