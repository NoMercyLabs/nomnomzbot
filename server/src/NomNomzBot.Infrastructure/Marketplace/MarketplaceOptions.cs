// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Marketplace;

/// <summary>
/// Marketplace client configuration (marketplace.md D7): the hosted-catalog base URL, configurable per
/// deployment, defaulting to the NoMercy public marketplace. Self-host points elsewhere (or nowhere — an
/// empty URL disables the marketplace surface with a typed <c>MARKETPLACE_UNAVAILABLE</c>).
/// </summary>
public class MarketplaceOptions
{
    public const string SectionName = "Marketplace";

    /// <summary>The NoMercy public marketplace — the D7 default every profile starts on.</summary>
    public const string DefaultUrl = "https://marketplace.nomnomz.bot";

    public string Url { get; set; } = DefaultUrl;
}
