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

namespace NomNomzBot.Domain.Automation.Events;

/// <summary>
/// An automation API token was issued — at creation or rotation (a rotation mints a new credential
/// for the same row). Internal credential-audit event (automation-api.md §2): it has no automation
/// event descriptor, so it journals but is never streamed to external consumers.
/// </summary>
public sealed class AutomationTokenIssuedEvent : DomainEventBase
{
    /// <summary>The id of the automation token.</summary>
    public required Guid TokenId { get; init; }

    /// <summary>The name the owner gave the token.</summary>
    public required string TokenName { get; init; }

    /// <summary>The permissions the token grants, as a list of scope names.</summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>The id of the user who issued the token.</summary>
    public required Guid CreatedByUserId { get; init; }

    /// <summary>True when this issue replaced an existing secret (rotate) rather than creating the row.</summary>
    public required bool WasRotation { get; init; }
}

/// <summary>An automation API token was revoked. Internal credential-audit event (automation-api.md §2).</summary>
public sealed class AutomationTokenRevokedEvent : DomainEventBase
{
    /// <summary>The id of the automation token that was revoked.</summary>
    public required Guid TokenId { get; init; }

    /// <summary>The id of the user who revoked the token.</summary>
    public required Guid RevokedByUserId { get; init; }
}
