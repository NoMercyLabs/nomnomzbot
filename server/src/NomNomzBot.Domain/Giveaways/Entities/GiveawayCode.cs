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

namespace NomNomzBot.Domain.Giveaways.Entities;

/// <summary>
/// One prize code in a pool (giveaways.md G.10). The code itself is a SECRET: <see cref="CodeCipher"/>
/// holds AEAD ciphertext (D6) — no list/read API ever returns the plaintext (reads are masked); it is
/// decrypted only to whisper the winner, or for the broadcaster-gated reveal when the whisper failed.
/// A code is assigned to at most one winner, ever.
/// </summary>
public class GiveawayCode : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = MonotonicGuid.Create();

    public Guid BroadcasterId { get; set; }

    public Guid CodePoolId { get; set; }

    /// <summary>AEAD ciphertext of the code — never plaintext at rest, never echoed by reads.</summary>
    public string CodeCipher { get; set; } = null!;

    /// <summary>The masked tail (last 4 characters) shown in list reads so pools stay auditable.</summary>
    public string? Label { get; set; }

    /// <summary><c>available</c> | <c>assigned</c> | <c>delivered</c> | <c>revoked</c>.</summary>
    public string Status { get; set; } = GiveawayCodeStatus.Available;

    public Guid? AssignedWinnerId { get; set; }

    public DateTime? AssignedAt { get; set; }
}

/// <summary>The <see cref="GiveawayCode.Status"/> values.</summary>
public static class GiveawayCodeStatus
{
    public const string Available = "available";
    public const string Assigned = "assigned";
    public const string Delivered = "delivered";
    public const string Revoked = "revoked";
}
