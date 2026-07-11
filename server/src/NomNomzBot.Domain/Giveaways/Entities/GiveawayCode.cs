// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    public Guid Id { get; set; } = Guid.CreateVersion7();

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
