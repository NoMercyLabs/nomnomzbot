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
