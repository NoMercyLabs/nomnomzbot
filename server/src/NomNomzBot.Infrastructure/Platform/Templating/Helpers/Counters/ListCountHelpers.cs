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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

/// <summary><c>{list.count}</c>: how many pick lists the channel has.</summary>
internal sealed class ListCountHelpers()
    : ListHelperGroup(
        new ListHelperRow(
            "list.count",
            "7",
            async call =>
                call.Args.Count == 0 && call.Source.HasChannel
                    ? ListText.Number(await call.Source.ListCountAsync(call.Ct))
                    : null,
            TemplateHelperFreshness.Live,
            TemplateHelperScope.Channel
        )
    );
