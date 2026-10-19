// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
