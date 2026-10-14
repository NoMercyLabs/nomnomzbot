// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>In-memory <see cref="IConfiguration"/> for billing tests.</summary>
internal static class TestConfiguration
{
    public static IConfiguration Empty() => new ConfigurationBuilder().Build();

    public static IConfiguration With(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
}
