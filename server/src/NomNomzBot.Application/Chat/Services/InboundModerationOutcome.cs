// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Chat.Services;

/// <summary>What a platform actually did with a moderation action.</summary>
public enum InboundModerationStatus
{
    /// <summary>The platform accepted the action.</summary>
    Done,

    /// <summary>The platform was reached but refused or could not carry out the action.</summary>
    Failed,

    /// <summary>No chat platform is registered for the provider, so nothing was attempted anywhere.</summary>
    NotSupported,
}

/// <summary>
/// The honest result of one moderation action routed by <see cref="IInboundOriginModerator"/>.
/// <see cref="Reason"/> is a short explanation for <see cref="InboundModerationStatus.Failed"/> and
/// <see cref="InboundModerationStatus.NotSupported"/>, and null for <see cref="InboundModerationStatus.Done"/>.
/// </summary>
public sealed record InboundModerationOutcome(InboundModerationStatus Status, string? Reason)
{
    public static InboundModerationOutcome Done() => new(InboundModerationStatus.Done, null);

    public static InboundModerationOutcome Failed(string reason) =>
        new(InboundModerationStatus.Failed, reason);

    public static InboundModerationOutcome NotSupported(string reason) =>
        new(InboundModerationStatus.NotSupported, reason);
}
