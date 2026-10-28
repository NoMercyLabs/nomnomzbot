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
