// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Counters;

/// <summary>
/// The catalog section 5 pick-list fixture served by a fake <see cref="IPickListService"/> that keeps lists
/// per channel: channel A holds <c>snacks</c> (chips, cookies, fruit) and 6 more lists (7 in total), channel B
/// holds one list, so a test can prove the two never mix.
/// </summary>
internal sealed class ListHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid ChannelB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static readonly Dictionary<Guid, Dictionary<string, string[]>> Lists = new()
    {
        [ChannelA] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["snacks"] = ["chips", "cookies", "fruit"],
            ["mixed"] = ["pear", "Apple", "fig"],
            ["pair"] = ["x", "y"],
            ["single"] = ["only"],
            ["extra1"] = ["e"],
            ["extra2"] = ["e"],
            ["extra3"] = ["e"],
        },
        [ChannelB] = new(StringComparer.OrdinalIgnoreCase) { ["snacks"] = ["tea", "toast"] },
    };

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public ListHelperFixture()
    {
        IPickListService pickLists = Substitute.For<IPickListService>();
        pickLists
            .GetByNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Get(call.ArgAt<Guid>(0), call.ArgAt<string>(1))));
        pickLists
            .PickRandomAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Pick(call.ArgAt<Guid>(0), call.ArgAt<string>(1))));
        pickLists
            .ListAsync(
                Arg.Any<Guid>(),
                Arg.Any<PickListSearch>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Task.FromResult(Page(call.ArgAt<Guid>(0))));

        _services = new ServiceCollection()
            .AddSingleton(pickLists)
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        Registry = _services.GetRequiredService<ITemplateHelperRegistry>();
        _resolver = new TemplateResolver(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero)),
            Registry
        );
    }

    public ITemplateHelperRegistry Registry { get; }

    public Task<string> RenderAsync(string template, Guid? broadcasterId) =>
        _resolver.ResolveAsync(template, new Dictionary<string, string>(), broadcasterId);

    public ListSource SourceFor(Guid broadcasterId) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), broadcasterId);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static Result<PickListDto> Get(Guid broadcasterId, string name) =>
        Lists[broadcasterId].TryGetValue(name, out string[]? items)
            ? Result.Success(
                new PickListDto(
                    Guid.NewGuid(),
                    name,
                    null,
                    items,
                    DateTime.UnixEpoch,
                    DateTime.UnixEpoch
                )
            )
            : Result.Failure<PickListDto>("missing", "NOT_FOUND");

    private static Result<string> Pick(Guid broadcasterId, string name) =>
        Lists[broadcasterId].TryGetValue(name, out string[]? items)
            ? Result.Success(items[Random.Shared.Next(items.Length)])
            : Result.Failure<string>("missing", "NOT_FOUND");

    private static Result<PagedList<PickListDto>> Page(Guid broadcasterId) =>
        Result.Success(new PagedList<PickListDto>([], 1, 1, Lists[broadcasterId].Count));
}
