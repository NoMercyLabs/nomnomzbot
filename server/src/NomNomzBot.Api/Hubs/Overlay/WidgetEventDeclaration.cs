// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs.Overlay;

/// <summary>
/// The event types a widget page subscribes to on the hub (<c>Subscribe</c>). A name is one or more of
/// <c>A-Z a-z 0-9 _ . : -</c> (the same character set the save-time scan reads), or exactly
/// <see cref="Widget.WildcardEvent"/>. One bad name refuses the whole set, so a half-valid list never lands.
/// </summary>
public static class WidgetEventDeclaration
{
    public const int MaxEvents = 200;
    public const int MaxNameLength = 100;

    /// <summary>The distinct names in first-seen order; false when the set is missing, too big or holds a bad name.</summary>
    public static bool TryNormalize(string[]? events, out List<string> distinct)
    {
        distinct = [];
        if (events is null || events.Length > MaxEvents)
            return false;

        foreach (string name in events)
        {
            if (!IsValidName(name))
                return false;
            if (!distinct.Contains(name))
                distinct.Add(name);
        }

        return true;
    }

    private static bool IsValidName(string? name) =>
        name is { Length: > 0 and <= MaxNameLength }
        && (name == Widget.WildcardEvent || name.All(IsNameCharacter));

    private static bool IsNameCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ':' or '-';
}
