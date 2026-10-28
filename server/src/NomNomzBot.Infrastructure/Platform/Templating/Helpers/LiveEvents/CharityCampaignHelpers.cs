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
