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

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>What one run of <see cref="IMusicPlayerMigrationService"/> changed.</summary>
public sealed record MusicPlayerMigrationReport(
    int ChannelsChecked,
    int MusicPlayersEnsured,
    int AudioWidgetsRenamed
);

/// <summary>
/// One-time per-channel move to the Music Player widget (music-playback-determinism.md): every channel gets a
/// <c>music_player</c> widget row, and the first-party Audio widget loses its old "TTS Audio" name. Idempotent.
/// </summary>
public interface IMusicPlayerMigrationService
{
    Task<MusicPlayerMigrationReport> MigrateAllChannelsAsync(
        CancellationToken cancellationToken = default
    );
}
