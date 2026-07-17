// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// No-op fallback for <see cref="ITtsOverlayNotifier"/> used where no live overlay connection exists
/// (worker processes, background services, tests). The API host replaces this with the SignalR-backed
/// adapter (<c>TtsOverlayNotifierAdapter</c>) via a later service registration.
/// </summary>
internal sealed class NullTtsOverlayNotifier : ITtsOverlayNotifier
{
    public Task SpeakAsync(
        Guid broadcasterId,
        TtsOverlaySpeakDto payload,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}
