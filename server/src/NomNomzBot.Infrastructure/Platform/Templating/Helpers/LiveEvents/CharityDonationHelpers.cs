// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
