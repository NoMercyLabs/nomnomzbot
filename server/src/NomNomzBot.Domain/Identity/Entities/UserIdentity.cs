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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// One proven external account linked to a <see cref="User"/> (schema A.6, platform-identity §1). A user has
/// 1..n identities, at most one per provider; any of them can log the user in. Exactly one is
/// <see cref="IsPrimary"/> (display/default; seeds <c>User.Username</c>/avatar refresh). This row is GLOBAL —
/// it is not tenant-scoped — and unique on both <c>(Provider, ProviderUserId)</c> and <c>(UserId, Provider)</c>.
/// Tokens never live here: <see cref="ConnectionId"/> points at the user-level login connection in the vault.
/// </summary>
public class UserIdentity : SoftDeletableEntity
{
    // Surrogate UUIDv7 PK (schema §1.1) — generated app-side via Guid.CreateVersion7(), never DB-default.
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // The internal person this external identity proves (FK→User.Id).
    public Guid UserId { get; set; }

    // Login provider ([VC:enum] AuthEnums.Platform) — "twitch" | "kick" | "youtube". Part of both unique keys.
    [MaxLength(20)]
    public string Provider { get; set; } = AuthEnums.Platform.Twitch;

    // The provider's stable account id (Twitch user id, Google channel/account id, Kick user id). Never the key.
    [MaxLength(100)]
    public string ProviderUserId { get; set; } = null!;

    [MaxLength(255)]
    public string ProviderUsername { get; set; } = null!;

    [MaxLength(255)]
    public string? ProviderDisplayName { get; set; }

    [MaxLength(2048)]
    public string? ProviderAvatarUrl { get; set; }

    // Exactly one identity per user is primary; it seeds User.Username/DisplayName/avatar + User.Platform.
    public bool IsPrimary { get; set; }

    // The user-level login connection in the vault (IntegrationConnection with BroadcasterId = null). Null until
    // a token grant is stored against this identity.
    public Guid? ConnectionId { get; set; }
    public IntegrationConnection? Connection { get; set; }

    public DateTime LinkedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public virtual User? User { get; set; }
}
