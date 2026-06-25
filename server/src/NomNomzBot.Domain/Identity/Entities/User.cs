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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

public class User : BaseEntity
{
    // Surrogate UUIDv7 PK (schema §1.1) — generated app-side via Guid.CreateVersion7(),
    // never DB-default, never Guid.NewGuid(). The internal FK target; never sent to Twitch.
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // External Twitch user id — a first-class indexed attribute (schema A.1), NOT the key.
    // This is what every Helix/IRC/EventSub call uses; the Guid never reaches Twitch.
    [MaxLength(50)]
    public string TwitchUserId { get; set; } = null!;

    // Identity platform ([VC:enum], schema A.1). Defaults to Twitch — the only login provider today.
    [MaxLength(20)]
    public string Platform { get; set; } = AuthEnums.Platform.Twitch;

    [MaxLength(255)]
    public string Username { get; set; } = null!;

    // Lower-cased Username for case-insensitive unique lookups (schema A.1).
    [MaxLength(255)]
    public string UsernameNormalized { get; set; } = null!;

    [MaxLength(255)]
    public string DisplayName { get; set; } = null!;

    [MaxLength(255)]
    public string? NickName { get; set; }

    // AES-256-GCM-sealed email (schema A.1, [PII-shred]) — replaces a plaintext email column.
    [MaxLength(512)]
    public string? EmailCipher { get; set; }

    // The per-subject DEK that opens this user's [PII-shred] fields (FK→CryptoKey). Null until minted.
    public Guid? SubjectKeyId { get; set; }

    [MaxLength(50)]
    public string? Timezone { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(2048)]
    public string? ProfileImageUrl { get; set; }

    [MaxLength(2048)]
    public string? OfflineImageUrl { get; set; }

    [MaxLength(7)]
    public string? Color { get; set; }

    [MaxLength(50)]
    public string BroadcasterType { get; set; } = "";

    // Twitch staff classification — "" | "staff" | "admin" | "global_mod" (Helix Get Users `type`). Used by
    // the 18+ gambling gate's personnel inference (economy.md §3.6); revocable, so re-read live.
    [MaxLength(20)]
    public string Type { get; set; } = "";

    // Twitch account creation date (Helix Get Users `created_at`). Immutable; the 18+ gate's MONOTONIC
    // account-age inference reads it ({{user.accountage}}).
    public DateTime? AccountCreatedAt { get; set; }

    public bool Enabled { get; set; } = true;

    // Platform principal (operator/admin) — replaces the old IsAdmin. Sourced into the JWT and read by
    // platform-plane authorization (identity-auth §3.6).
    public bool IsPlatformPrincipal { get; set; }

    // This user record is a bot identity, not a human login.
    public bool IsBot { get; set; }

    // GDPR crypto-shred / anonymization has been applied to this subject (schema A.1).
    public bool IsAnonymized { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public int? PronounId { get; set; }
    public Pronoun? Pronoun { get; set; }
    public bool PronounManualOverride { get; set; }

    public virtual Channel? Channel { get; set; }
}
