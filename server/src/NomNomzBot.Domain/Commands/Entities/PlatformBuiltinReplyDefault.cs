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
