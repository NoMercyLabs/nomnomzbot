// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Identity;

/// <summary>
/// How active a viewer is in one channel, from the viewer's real message count there. A channel sets its own
/// steps (<c>Channel.ViewerCasualFrom</c>, <c>ViewerRegularFrom</c>, <c>ViewerVeteranFrom</c>); the defaults below
/// are what a channel starts with and can change.
/// </summary>
public static class ViewerActivitySteps
{
    public const int DefaultCasualFrom = 10;
    public const int DefaultRegularFrom = 100;
    public const int DefaultVeteranFrom = 500;

    /// <summary>The highest value a step may take.</summary>
    public const int MaxStep = 1_000_000;

    public const string New = "new";
    public const string Casual = "casual";
    public const string Regular = "regular";
    public const string Veteran = "veteran";

    /// <summary>
    /// The level name for <paramref name="messages"/>: <c>new</c> below the casual step, then <c>casual</c>,
    /// <c>regular</c> and <c>veteran</c> from their own steps.
    /// </summary>
    public static string Level(long messages, int casualFrom, int regularFrom, int veteranFrom) =>
        messages >= veteranFrom ? Veteran
        : messages >= regularFrom ? Regular
        : messages >= casualFrom ? Casual
        : New;

    /// <summary>True when 1 &lt;= casual &lt; regular &lt; veteran &lt;= <see cref="MaxStep"/>.</summary>
    public static bool AreValid(int casualFrom, int regularFrom, int veteranFrom) =>
        casualFrom >= 1
        && casualFrom < regularFrom
        && regularFrom < veteranFrom
        && veteranFrom <= MaxStep;
}
