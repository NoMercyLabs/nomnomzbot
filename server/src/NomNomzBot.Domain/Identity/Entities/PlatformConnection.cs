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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// One streaming-platform presence attached to a <see cref="Channel"/> (product decision D1/D2 —
/// PRODUCT-ALIGNMENT.md: one channel, many platform connections, not one channel per platform). This
/// is the S019a data-model foundation only — no provisioner wiring, no reads/writes elsewhere in the
/// app yet fold through this entity.
/// </summary>
public class PlatformConnection : SoftDeletableEntity
{
    // Surrogate UUIDv7 PK, matching Channel's own id convention (schema §1.1, A.2).
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChannelId { get; set; }

    // Streaming platform this connection is on ([VC:enum] AuthEnums.Platform) — the same string-constant
    // set Channel.Provider already uses; not a new enum.
    [MaxLength(20)]
    public string Provider { get; set; } = AuthEnums.Platform.Twitch;

    // The platform's own channel/broadcaster id for this connection.
    [MaxLength(100)]
    public string ExternalChannelId { get; set; } = "";

    [MaxLength(255)]
    public string DisplayName { get; set; } = "";

    // Whether this is the channel's primary/original platform connection.
    public bool IsPrimary { get; set; }

    public bool IsLive { get; set; }

    [ForeignKey(nameof(ChannelId))]
    public virtual Channel Channel { get; set; } = null!;
}
