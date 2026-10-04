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

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// Takes the state the channel's overlay YouTube player reports: what it plays, and whether it plays, paused,
/// ended or failed. An ended or failed video starts the buffered next one.
/// </summary>
public interface IYouTubePlayerReportService
{
    /// <summary>Fails, and changes nothing, for an empty video id or a state that is not
    /// playing, paused, ended or error.</summary>
    Task<Result> ReportAsync(
        Guid broadcasterId,
        Guid widgetId,
        string videoId,
        string state,
        long positionMs,
        CancellationToken cancellationToken = default
    );
}
