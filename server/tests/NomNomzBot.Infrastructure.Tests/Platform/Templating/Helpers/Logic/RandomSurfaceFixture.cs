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

using Microsoft.Extensions.DependencyInjection;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Logic;

/// <summary>A repeatable random source for the pure random namespace.</summary>
internal static class RandomSurfaceFixture
{
    public static void Configure(ServiceCollection services) =>
        services.AddSingleton(new Random(1));
}
