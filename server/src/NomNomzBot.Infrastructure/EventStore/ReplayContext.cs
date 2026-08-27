// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// Ambient flag set around <see cref="ImportReplayProjection"/>'s republish of an imported event, so the
/// outbound Twitch chat send sites (<c>HelixChatProvider</c>, <c>TwitchChatApi</c>) can skip posting to the
/// LIVE channel while a historical event is being replayed — currency, streaks, TTS, and every other side
/// effect still run for real, only the visible chat/announcement/shoutout text is suppressed. An
/// <see cref="AsyncLocal{T}"/> flows through the same async call chain <c>IEventBus.PublishAsync</c> uses to
/// reach the handler, including across the DI scope <see cref="EventStoreProjectionDriver"/> creates per pass.
/// </summary>
public static class ReplayContext
{
    private static readonly AsyncLocal<bool> Flag = new();

    public static bool IsReplaying => Flag.Value;

    public static IDisposable Enter()
    {
        Flag.Value = true;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Flag.Value = false;
    }
}
