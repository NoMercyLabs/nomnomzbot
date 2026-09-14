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

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// One finalized chunk of the voice-listener page's live transcript (owner ask, 2026-09-14): the SAME
/// continuous <c>webkitSpeechRecognition</c> loop that spots trigger words also hears everything else said, so
/// every finalized recognition result the listener reports is kept — not only the ones that match a
/// <see cref="VoiceTrigger"/> — against the channel's CURRENT live <see cref="Stream.Entities.Stream"/>, so the
/// Analytics per-stream detail page can show a full chronological transcript once the stream is over.
/// A report that arrives while the channel has no live stream is dropped (nothing to attribute it to) rather
/// than stored orphaned.
/// </summary>
public class VoiceTranscriptSegment : BaseEntity
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>The live stream this segment was spoken during (<see cref="Stream.Entities.Stream.Id"/>).</summary>
    [MaxLength(50)]
    public string StreamId { get; set; } = null!;

    [MaxLength(1000)]
    public string Text { get; set; } = null!;

    public DateTime SpokenAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(StreamId))]
    public virtual Stream.Entities.Stream Stream { get; set; } = null!;
}
