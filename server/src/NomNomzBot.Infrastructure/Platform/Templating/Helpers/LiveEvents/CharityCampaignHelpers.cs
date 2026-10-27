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

/// <summary>The campaign itself: its name, link, money raised and target, and how far it is.</summary>
internal sealed class CharityCampaignHelpers()
    : LiveEventHelperGroup(
        "charity",
        LiveCharityRows.Campaign("charity.name", "Sample Charity", (c, _) => c.CharityName),
        LiveCharityRows.Campaign(
            "charity.link",
            "https://www.samplecharity.org",
            (c, _) => string.IsNullOrWhiteSpace(c.CharityWebsite) ? null : c.CharityWebsite
        ),
        LiveCharityRows.Campaign(
            "charity.raised",
            "€420.00",
            (c, call) => LiveText.Money(c.CurrentAmount, call.Culture)
        ),
        LiveCharityRows.Campaign(
            "charity.target",
            "€1,000.00",
            (c, call) => LiveText.Money(c.TargetAmount, call.Culture)
        ),
        LiveCharityRows.Campaign(
            "charity.percent",
            "42%",
            (c, _) => LiveText.Percent(c.CurrentAmount.Value, c.TargetAmount.Value)
        ),
        LiveCharityRows.Campaign(
            "charity.bar",
            "████░░░░░░ 42%",
            (c, _) => LiveText.Bar(c.CurrentAmount.Value, c.TargetAmount.Value)
        )
    );
