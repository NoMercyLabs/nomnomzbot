// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// What the channel stored about the person: the per-viewer key/value data (<c>IViewerDataService</c>) and the
/// quotes they said (<c>IQuoteService</c>). <c>{user.data.color:none}</c> prints the default after the colon when
/// the key is unset; with no default it prints nothing.
/// </summary>
internal sealed class PeopleStoredHelpers()
    : PeopleHelperGroup(
        new PeopleRow(
            "data.",
            ViewerSides.User,
            "blue",
            async call =>
            {
                string? value = await call.Data.DataAsync(call.KeyTail, call.Ct);
                return string.IsNullOrEmpty(value)
                    ? call.Args.Count > 0
                        ? call.Args[0]
                        : string.Empty
                    : value;
            }
        )
        {
            Source = "viewerData",
            IsPrefix = true,
        },
        PeopleRow.Live(
            "quotes.count",
            ViewerSides.User,
            "3",
            "quotes",
            PeopleRow.Map(d => d.QuoteCountAsync(), (count, _) => PeopleText.Raw(count ?? 0))
        )
    );
