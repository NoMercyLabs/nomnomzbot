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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// EventSub sessions that belong to one signed-in viewer (chat-client.md "Any Twitch channel"). Each session
/// rides the viewer's own token and has its own sink, so its frames never reach the bot pipeline.
/// </summary>
public interface IViewerEventSubSessions
{
    /// <summary>Binds the owner's sink, then opens (or reuses) the session. Returns its session id.</summary>
    Task<Result<string>> OpenAsync(
        string ownerKey,
        IEventSubNotificationSink sink,
        CancellationToken ct = default
    );

    /// <summary>Creates one subscription on the session with the viewer's own token. Returns its id.</summary>
    Task<Result<string>> SubscribeAsViewerAsync(
        Guid viewerUserId,
        string sessionId,
        string eventType,
        string version,
        IReadOnlyDictionary<string, string> condition,
        CancellationToken ct = default
    );

    /// <summary>Stops the owner's session and drops its sink. Twitch removes its subscriptions with it.</summary>
    Task CloseAsync(string ownerKey);
}
