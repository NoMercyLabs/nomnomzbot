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

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// The authoritative consent / lawful-basis ledger (gdpr-crypto.md O.5) — one active row per
/// <c>(BroadcasterId, SubjectUserId, ConsentType)</c>, latest-wins, recording that a human affirmatively
/// consented. <c>BroadcasterId</c> is nullable (null = platform-wide ToS), so this is NOT <c>ITenantScoped</c>.
/// Inferences NEVER reach this ledger — only an explicit consent is written here (economy.md §3.6).
/// <para>
/// The envelope-crypto fields are deferred until the GDPR-crypto subsystem lands: <c>SubjectKeyId</c>
/// (FK→CryptoKey) is nullable and <c>IpAddressCipher</c> is left unset — the lightweight 18+ gambling gate
/// guards fun-money, not KYC, so it does not require encrypted IP proof. <c>SubjectIdHash</c> is a plain hash
/// of the subject id (no key needed).
/// </para>
/// </summary>
public class ConsentRecord : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? BroadcasterId { get; set; }
    public Guid SubjectUserId { get; set; }
    public Guid? SubjectKeyId { get; set; }
    public string SubjectIdHash { get; set; } = null!;
    public string ConsentType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string LawfulBasis { get; set; } = null!;
    public string? ConsentVersion { get; set; }
    public string? Source { get; set; }
    public string? IpAddressCipher { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? WithdrawnAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
