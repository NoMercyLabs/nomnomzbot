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
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Composing;

/// <summary>The quote namespace's numbered, newest and random channel quote.</summary>
internal static class QuoteSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        QuoteDto quote = new(
            Guid.Parse("0192b400-0000-7000-8000-00000000f050"),
            2,
            "that was close",
            "SampleViewer",
            "Just Chatting",
            new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc)
        );
        IQuoteService quotes = (IQuoteService)
            services.Last(d => d.ServiceType == typeof(IQuoteService)).ImplementationInstance!;
        quotes.GetAsync(channel, 2, Arg.Any<CancellationToken>()).Returns(Result.Success(quote));
        quotes.GetRandomAsync(channel, Arg.Any<CancellationToken>()).Returns(Result.Success(quote));
        quotes
            .ListAsync(
                channel,
                Arg.Any<QuoteSearch>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<QuoteDto>([quote], 1, 1, 2)));
    }
}
