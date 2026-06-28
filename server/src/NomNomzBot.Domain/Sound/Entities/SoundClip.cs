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

namespace NomNomzBot.Domain.Sound.Entities;

/// <summary>
/// A broadcaster-uploaded audio clip from the sound library (spec P.18). Curated by the broadcaster/editor;
/// played by the <c>play_sound</c> pipeline action on the overlay audio bus. Soft-delete, tenant-scoped.
/// Unique constraint on <c>(BroadcasterId, Name)</c> so pipelines can reference clips by slug.
/// </summary>
public class SoundClip : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>URL-safe slug used by the <c>play_sound</c> action's <c>Clip</c> parameter.</summary>
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string DisplayName { get; set; } = null!;

    /// <summary>Opaque key in <c>ISoundClipStore</c> — disk path on self-host, object-store key on SaaS.</summary>
    [MaxLength(200)]
    public string StorageKey { get; set; } = null!;

    /// <summary><c>audio/mpeg</c>, <c>audio/ogg</c>, or <c>audio/wav</c> — content-sniffed, not trusted from the request.</summary>
    [MaxLength(40)]
    public string MimeType { get; set; } = null!;

    public int DurationMs { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>0–100 playback volume used when the pipeline action does not override. Default 80.</summary>
    public int DefaultVolume { get; set; } = 80;

    public bool IsEnabled { get; set; } = true;

    public Guid CreatedByUserId { get; set; }

    // ── Navigations ─────────────────────────────────────────────────────────────

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual User CreatedByUser { get; set; } = null!;
}
