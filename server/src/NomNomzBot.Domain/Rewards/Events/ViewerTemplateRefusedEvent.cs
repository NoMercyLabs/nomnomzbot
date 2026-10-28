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
