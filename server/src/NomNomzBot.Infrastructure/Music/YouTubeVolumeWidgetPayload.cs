// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Music;

/// <summary><c>youtube.volume</c>: the overlay player plays at this volume (0-100) from now on, for this video and the ones after it.</summary>
public sealed record YouTubeVolumeWidgetPayload(int Percent);
