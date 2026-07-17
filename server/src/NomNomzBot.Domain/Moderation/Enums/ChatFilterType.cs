// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.Enums;

/// <summary>
/// How a <c>ChatFilter</c> (moderation.md J.6) matches a chat message: a compiled <see cref="Regex"/>
/// pattern, a <see cref="Blocklist"/> of literal terms, or a <see cref="LinkPolicy"/> (URL detection).
/// </summary>
public enum ChatFilterType
{
    Regex,
    Blocklist,
    LinkPolicy,
}
