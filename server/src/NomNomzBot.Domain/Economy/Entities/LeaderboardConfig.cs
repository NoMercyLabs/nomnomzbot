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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// A configured leaderboard (economy.md L.1) — what to rank (<c>Metric</c>), over what set (<c>Scope</c>:
/// channel or jar), for which window (<c>Period</c>), how many to show (<c>TopN</c>), and whether viewers may
/// see it (<c>IsPublic</c>). <c>BroadcasterId</c>/<c>JarId</c> are nullable (a channel ranking sets
/// <c>BroadcasterId</c>; a jar ranking sets <c>JarId</c>), so this is NOT <c>ITenantScoped</c> — the service
/// filters explicitly.
/// </summary>
public class LeaderboardConfig : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? BroadcasterId { get; set; }
    public Guid? JarId { get; set; }
    public string Metric { get; set; } = null!;
    public string Scope { get; set; } = null!;
    public string Period { get; set; } = null!;
    public bool IsPublic { get; set; }
    public int TopN { get; set; }
}
