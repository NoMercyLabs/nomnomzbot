// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// The legacy <c>!bansong</c> strike thresholds, copied from the old bot's own code (BanSong.cs and
/// SongRequest.cs), quirk included. A strike is one track a person banned. The old bot printed the
/// warning for 6 to 10 bans (so exactly 10 still warned), printed the revoke notice only from 11 on, and
/// refused that person's song requests from 10 on.
/// </summary>
public static class SongBanStrikes
{
    /// <summary>The first ban count that prints the warning.</summary>
    public const int WarningFrom = 6;

    /// <summary>The last ban count that prints the warning (the old bot's <c>&lt;= 10</c>).</summary>
    public const int WarningThrough = 10;

    /// <summary>The first ban count that prints the revoke notice.</summary>
    public const int RevokedNoticeFrom = WarningThrough + 1;

    /// <summary>From this ban count on, the person's own song requests are refused.</summary>
    public const int RequestsRefusedFrom = 10;
}
