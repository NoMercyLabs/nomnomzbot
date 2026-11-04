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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.PickLists.Services;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Fun;

/// <summary>The channel's 8-ball answers and a valid RPS choice.</summary>
internal static class FunSurfaceFixture
{
    public static IEnumerable<string> ShapesFor(string key) => key == "fun.rps.vs" ? [":rock"] : [];

    public static void Configure(ServiceCollection services, Guid channel)
    {
        IPickListService lists = (IPickListService)
            services.Last(d => d.ServiceType == typeof(IPickListService)).ImplementationInstance!;
        PickListDto answers = new(
            Guid.Parse("0192b400-0000-7000-8000-00000000f049"),
            "8ball",
            null,
            ["It is certain", "Ask again later"],
            DateTime.UnixEpoch,
            DateTime.UnixEpoch
        );
        lists
            .GetByNameAsync(channel, "8ball", Arg.Any<CancellationToken>())
            .Returns(Result.Success(answers));
    }
}
