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

namespace NomNomzBot.Domain.Automation.Events;

/// <summary>
/// An automation API token was issued — at creation or rotation (a rotation mints a new credential
/// for the same row). Internal credential-audit event (automation-api.md §2): it has no automation
/// event descriptor, so it journals but is never streamed to external consumers.
/// </summary>
public sealed class AutomationTokenIssuedEvent : DomainEventBase
{
    public required Guid TokenId { get; init; }
    public required string TokenName { get; init; }
    public required IReadOnlyList<string> Scopes { get; init; }
    public required Guid CreatedByUserId { get; init; }

    /// <summary>True when this issue replaced an existing secret (rotate) rather than creating the row.</summary>
    public required bool WasRotation { get; init; }
}

/// <summary>An automation API token was revoked. Internal credential-audit event (automation-api.md §2).</summary>
public sealed class AutomationTokenRevokedEvent : DomainEventBase
{
    public required Guid TokenId { get; init; }
    public required Guid RevokedByUserId { get; init; }
}
