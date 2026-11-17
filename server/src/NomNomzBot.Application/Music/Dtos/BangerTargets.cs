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

namespace NomNomzBot.Application.Music.Dtos;

/// <summary>The values of the music setting that says where <c>!banger</c> saves a song.</summary>
public static class BangerTargets
{
    /// <summary>The channel's banger playlist, or the provider's likes when none is chosen.</summary>
    public const string Default = "default";

    /// <summary>One playlist per requester and year, on the playing song's service.</summary>
    public const string PerYear = "perYear";

    /// <summary>One playlist per requester, kept across years.</summary>
    public const string AllTime = "allTime";
}
