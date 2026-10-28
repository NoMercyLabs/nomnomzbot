// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Billing;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Assets.Entities;

/// <summary>
/// A broadcaster-uploaded media asset (image or audio) from the channel's asset library — the generic
/// binary store overlay widgets reference via <c>cfg.assets.&lt;key&gt;</c> URLs (chimes, boot screens,
/// svg icons, …). Same trust class as <c>SoundClip</c>: broadcaster-authored media for their OWN overlays.
/// Soft-delete, tenant-scoped. Unique constraint on <c>(BroadcasterId, Name)</c> so the public serving URL
/// (<c>/api/v1/assets/file/{channelId}/{name}</c>) is stable and upload replaces by name.
/// The stored bytes are a real marginal cost (disk + egress + backup, S-BUDGETS-b5) — counted via
/// <see cref="SizeBytes"/> against the <c>channel_asset_storage_bytes</c> COST_DRIVING limit.
/// </summary>
[CountedResource("channel_asset_storage_bytes", ResourceClass.CostDriving)]
public class ChannelAsset : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>URL-safe slug — the stable per-channel identity widget configs store.</summary>
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string DisplayName { get; set; } = null!;

    /// <summary><c>image</c> or <c>audio</c> — derived from the sniffed content type, never trusted from the request.</summary>
    [MaxLength(10)]
    public string Kind { get; set; } = null!;

    /// <summary>The content-sniffed MIME type (allowlist: png/jpeg/gif/webp/svg+xml/mpeg/ogg/wav).</summary>
    [MaxLength(40)]
    public string MimeType { get; set; } = null!;

    /// <summary>Opaque key in <c>IChannelAssetStore</c> — disk path on self-host, object-store key on SaaS.</summary>
    [MaxLength(200)]
    public string StorageKey { get; set; } = null!;

    public long SizeBytes { get; set; }

    public Guid CreatedByUserId { get; set; }

    // ── Navigations ─────────────────────────────────────────────────────────────

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual User CreatedByUser { get; set; } = null!;
}
