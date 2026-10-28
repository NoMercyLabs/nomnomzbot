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
