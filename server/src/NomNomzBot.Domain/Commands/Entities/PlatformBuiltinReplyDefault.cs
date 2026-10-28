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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// The platform admin's reply text for one built-in response slot (plan item A4), e.g. <c>(uptime, live)</c>.
/// GLOBAL (no tenant). A row exists only when the admin replaced the shipped wording: it then wins over the
/// personality-tone lines and the built-in's own fallback for every channel, while a channel's own response
/// override keeps winning over it. No seeder writes this table, so an edit survives every redeploy.
/// </summary>
public class PlatformBuiltinReplyDefault : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [MaxLength(50)]
    public string BuiltinKey { get; set; } = null!;

    [MaxLength(50)]
    public string Slot { get; set; } = null!;

    [MaxLength(500)]
    public string Template { get; set; } = null!;

    /// <summary>The platform operator who last changed this reply.</summary>
    public Guid? UpdatedByUserId { get; set; }
}
