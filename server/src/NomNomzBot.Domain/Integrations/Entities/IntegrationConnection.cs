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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Integrations.Entities;

/// <summary>
/// A user/tenant's connection to an external provider (schema E.1) — Twitch login, the platform bot, or a
/// connected Spotify/Discord/YouTube account. Replaces the flat-token <c>Service</c> entity. Holds no
/// secrets; the access/refresh/app tokens live crypto-vaulted in <see cref="IntegrationToken"/> rows.
/// <c>BroadcasterId</c> is null for platform/global connections (the shared bot), so it is scoped
/// explicitly by the vault rather than via the non-nullable <c>ITenantScoped</c> filter.
/// </summary>
public class IntegrationConnection : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // Tenant (channel) owning this connection; null = platform/global (e.g. the shared bot).
    public Guid? BroadcasterId { get; set; }

    [MaxLength(20)]
    public string Provider { get; set; } = null!;

    [MaxLength(255)]
    public string? ProviderAccountId { get; set; }

    [MaxLength(255)]
    public string? ProviderAccountName { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = null!;

    // The kept-truthful granted scope set ([VC:JSON]); progressive-scope decisions read this.
    public List<string> Scopes { get; set; } = [];

    [MaxLength(512)]
    public string? ClientId { get; set; }

    // Self-host operator supplied their own provider app credentials (BYOK).
    public bool IsByok { get; set; }

    // Free-form provider settings ([VC:JSON] string); null when none.
    public string? Settings { get; set; }

    public Guid? ConnectedByUserId { get; set; }

    public DateTime? ConnectedAt { get; set; }

    public DateTime? LastRefreshedAt { get; set; }

    public DateTime? LastErrorAt { get; set; }

    public int ConsecutiveFailureCount { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel? Channel { get; set; }

    public virtual ICollection<IntegrationToken> Tokens { get; set; } = [];
}
