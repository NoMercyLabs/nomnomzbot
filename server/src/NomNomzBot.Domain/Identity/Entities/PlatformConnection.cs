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
