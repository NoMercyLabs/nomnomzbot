// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// Manages per-channel music configuration stored in the Configuration key-value store.
/// </summary>
public interface IMusicConfigService
{
    Task<Result<MusicConfigDto>> GetConfigAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );
    Task<Result<MusicConfigDto>> UpdateConfigAsync(
        string broadcasterId,
        UpdateMusicConfigDto request,
        CancellationToken cancellationToken = default
    );
}
