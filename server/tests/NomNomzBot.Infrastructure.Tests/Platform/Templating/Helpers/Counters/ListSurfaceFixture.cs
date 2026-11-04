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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Counters;

/// <summary>The list namespace's named channel pick list used by the matrix argument shapes.</summary>
internal static class ListSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        PickListDto list = new(
            Guid.Parse("0192b400-0000-7000-8000-00000000f048"),
            "a",
            null,
            ["chips", "cookies", "fruit"],
            DateTime.UnixEpoch,
            DateTime.UnixEpoch
        );
        IPickListService lists = Substitute.For<IPickListService>();
        lists
            .GetByNameAsync(channel, "a", Arg.Any<CancellationToken>())
            .Returns(Result.Success(list));
        lists
            .PickRandomAsync(channel, "a", Arg.Any<CancellationToken>())
            .Returns(Result.Success("chips"));
        lists
            .ListAsync(
                channel,
                Arg.Any<PickListSearch>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<PickListDto>([list], 1, 1, 1)));
        services.AddSingleton(lists);
    }
}
