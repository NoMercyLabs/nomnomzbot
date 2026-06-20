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
}
