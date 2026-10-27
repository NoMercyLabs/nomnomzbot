// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>One small group of <c>emote.*</c> rows; the provider finds every group by scan.</summary>
internal interface IEmoteHelperGroup
{
    IReadOnlyList<EmoteRow> Rows { get; }
}

internal abstract class EmoteHelperGroup(params EmoteRow[] rows) : IEmoteHelperGroup
{
    public IReadOnlyList<EmoteRow> Rows { get; } = rows;
}
