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
