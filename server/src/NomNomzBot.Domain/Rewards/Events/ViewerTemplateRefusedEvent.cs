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

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// A viewer's reward input held template syntax and was left as typed. The channel enabled viewer templates, but
/// the viewer lacked the permission, or the text broke a limit. The event bus journals it; this is the audit trail.
/// </summary>
public sealed class ViewerTemplateRefusedEvent : DomainEventBase
{
    /// <summary>The id of the viewer whose text was refused.</summary>
    public required Guid CallerUserId { get; init; }

    /// <summary>The action key the viewer needed (<c>reward:input:template</c>).</summary>
    public required string ActionKey { get; init; }

    /// <summary>Why the text stayed literal: <c>no_permission</c>, <c>too_deep</c> or <c>too_long</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>The length of the viewer's text, in characters. The text itself is not stored.</summary>
    public required int InputLength { get; init; }
}
