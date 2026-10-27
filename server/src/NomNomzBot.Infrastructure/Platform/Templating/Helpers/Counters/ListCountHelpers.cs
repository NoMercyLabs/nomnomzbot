// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
