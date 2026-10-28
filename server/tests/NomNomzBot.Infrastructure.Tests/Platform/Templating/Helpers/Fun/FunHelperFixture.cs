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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Fun;

/// <summary>
/// The fun provider with a seeded <see cref="Random"/> and the catalog section 5 clock (2026-10-26T14:30:00Z),
/// served by a fake <see cref="IPickListService"/> that keeps the <c>8ball</c> list per channel: channel A and
/// channel B hold different answers, so a test can prove the two never mix, and channel C holds none.
/// </summary>
internal sealed class FunHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid ChannelB = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    public static readonly Guid ChannelC = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    public static readonly string[] AnswersA =
    [
        "It is certain",
        "Ask again later",
        "Very doubtful",
    ];
    public static readonly string[] AnswersB = ["Yes", "No"];

    private static readonly Dictionary<Guid, string[]> EightBall = new()
    {
        [ChannelA] = AnswersA,
        [ChannelB] = AnswersB,
    };

    private readonly Dictionary<string, string> _seeds;
    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public FunHelperFixture(
        int seed = 1,
        DateTimeOffset? now = null,
        string userLogin = "viewerone"
    )
    {
        _seeds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user.login"] = userLogin,
            ["user.display"] = "ViewerOne",
            ["target.display"] = "ViewerTwo",
        };
        IPickListService pickLists = Substitute.For<IPickListService>();
        pickLists
            .GetByNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Get(call.ArgAt<Guid>(0), call.ArgAt<string>(1))));

        _services = new ServiceCollection().AddSingleton(pickLists).BuildServiceProvider();
        FunTemplateHelpers provider = new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new Random(seed)
        );
        Registry = new TemplateHelperRegistryAggregate([provider, new IfTemplateHelpers()]);
        Provider = provider;
        _resolver = new TemplateResolver(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(now ?? new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero)),
            Registry
        );
    }

    public ITemplateHelperRegistry Registry { get; }

    public FunTemplateHelpers Provider { get; }

    public Task<string> RenderAsync(string template, Guid? broadcasterId = null) =>
        _resolver.ResolveAsync(template, _seeds, broadcasterId);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static Result<PickListDto> Get(Guid broadcasterId, string name) =>
        name == "8ball" && EightBall.TryGetValue(broadcasterId, out string[]? items)
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
}
