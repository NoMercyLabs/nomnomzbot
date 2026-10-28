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

namespace NomNomzBot.Application.Obs.Services;

/// <summary>
/// Repairs OBS browser sources that show one of the bot's widgets but never got a live connection to the bot.
/// OBS keeps such a source loaded across scene changes, so a page that missed its connection stays dead until
/// somebody refreshes it. With the OBS control connection active, the bot does that refresh itself.
/// </summary>
public interface IObsBrowserSourceRecoveryService
{
    /// <summary>
    /// Refreshes every loaded OBS browser source whose widget (enabled, owned by the channel) has no live overlay
    /// connection. Returns the names of the sources it refreshed. Fails with the OBS error when OBS is not
    /// reachable (<c>OBS_NOT_CONNECTED</c>), in which case nothing is touched.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> RecoverAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}

/// <summary>
/// Asks for a recovery pass after a short delay, so a source that is reloading on its own (a normal reload, an
/// OBS start) reconnects first and is not refreshed a second time. Calls for one channel inside the delay collapse
/// into one pass at the end of the last delay.
/// </summary>
public interface IObsBrowserSourceRecoveryScheduler
{
    void Schedule(Guid broadcasterId);
}
