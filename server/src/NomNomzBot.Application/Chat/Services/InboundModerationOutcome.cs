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
