// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Secret { get; set; }
    public string Issuer { get; set; } = "NomNomzBot";
    public string Audience { get; set; } = "NomNomzBot";
    public int ExpiryMinutes { get; set; } = 60;
    public int RefreshExpiryDays { get; set; } = 30;
}
