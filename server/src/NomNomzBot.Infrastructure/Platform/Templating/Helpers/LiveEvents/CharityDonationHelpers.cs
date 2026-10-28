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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The donations: how many were made and the newest one.</summary>
internal sealed class CharityDonationHelpers()
    : LiveEventHelperGroup(
        "charity",
        LiveCharityRows.Donations(
            "charity.donations.count",
            "17",
            (page, call) => LiveText.Count(Math.Max(page.Total, page.Items.Count), call.Culture)
        ),
        LiveCharityRows.Donations(
            "charity.donations.latest",
            "ViewerOne (€10.00)",
            (page, call) =>
                page.Items.Count == 0
                    ? null
                    : $"{page.Items[0].UserName} ({LiveText.Money(page.Items[0].Amount, call.Culture)})"
        )
    );
