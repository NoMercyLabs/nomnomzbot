// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// The seeded global catalogue of gateable actions (roles-permissions schema B.3) — one row per
/// <c>ActionKey</c> (e.g. <c>economy:config:write</c>). <c>DefaultLevel</c> is the out-of-box required level;
/// <c>FloorLevel</c> is the lowest a channel override may set it to; <c>FloorTier</c> classifies its danger
/// (Critical/Tos/Low); <c>IsGrantableViaPermit</c> gates whether it may be delegated to an individual via
/// <c>!permit</c> (default-deny). GLOBAL (no tenant) — distinct from the pipeline
/// <c>NomNomzBot.Application.Abstractions.Pipeline.ActionDefinition</c>.
/// <para>
/// <c>DefaultLevel</c> is the SHIPPED default — the seeder owns it and re-syncs it on every deploy.
/// <c>PlatformDefaultLevel</c> is the platform admin's runtime replacement for it (never below the floor);
/// the seeder never writes it, so an admin edit survives every redeploy. A channel's own
/// <c>ChannelActionOverride</c> still wins over both.
/// </para>
/// </summary>
public class ActionDefinition : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string ActionKey { get; set; } = null!;
    public AuthPlane Plane { get; set; }
    public int DefaultLevel { get; set; }
    public int FloorLevel { get; set; }
    public DangerTier FloorTier { get; set; }
    public bool IsGrantableViaPermit { get; set; }
    public string? Description { get; set; }

    /// <summary>The platform admin's default, replacing <see cref="DefaultLevel"/> for every channel without an override; null = the shipped default.</summary>
    public int? PlatformDefaultLevel { get; set; }

    /// <summary>The platform operator who last set <see cref="PlatformDefaultLevel"/>.</summary>
    public Guid? PlatformDefaultSetByUserId { get; set; }

    /// <summary>When <see cref="PlatformDefaultLevel"/> was last set or cleared.</summary>
    public DateTime? PlatformDefaultSetAt { get; set; }
}
