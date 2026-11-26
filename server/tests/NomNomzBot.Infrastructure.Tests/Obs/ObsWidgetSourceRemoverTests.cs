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

using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.Obs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

// Stoney, live 2026-11-26: "deleting should also delete it from obs btw".
public sealed class ObsWidgetSourceRemoverTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();

    [Fact]
    public async Task Deleting_a_widget_removes_only_its_own_browser_sources_from_OBS()
    {
        Guid deleted = Guid.NewGuid();
        IObsControlService obs = Substitute.For<IObsControlService>();
        List<ObsRequest> sent = [];
        obs.GetBrowserSourcesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ObsBrowserSourceDto>>([
                    new(
                        "Now Playing",
                        $"https://bot.test/overlay?widgetId={deleted}&token=t",
                        true,
                        true
                    ),
                    new(
                        "Now Playing 2",
                        $"https://bot.test/overlay?widgetId={deleted}&token=u",
                        true,
                        true
                    ),
                    new(
                        "Chat",
                        $"https://bot.test/overlay?widgetId={Guid.NewGuid()}&token=t",
                        true,
                        true
                    ),
                    new("Some site", "https://example.test/page", true, true),
                ])
            );
        obs.RequestAsync(Broadcaster, Arg.Do<ObsRequest>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ObsResponse>("unused"));

        await new ObsWidgetSourceRemover(obs).HandleAsync(Changed(deleted, "deleted"));

        sent.Select(r => r.RequestType).Should().AllBe("RemoveInput");
        sent.Select(r => r.RequestData!["inputName"])
            .Should()
            .BeEquivalentTo(["Now Playing", "Now Playing 2"]);
    }

    [Theory]
    [InlineData("widgets", "updated")]
    [InlineData("commands", "deleted")]
    public async Task Any_other_change_leaves_OBS_alone(string domain, string action)
    {
        IObsControlService obs = Substitute.For<IObsControlService>();

        await new ObsWidgetSourceRemover(obs).HandleAsync(Changed(Guid.NewGuid(), action, domain));

        await obs.DidNotReceiveWithAnyArgs().GetBrowserSourcesAsync(default, default);
    }

    private static ChannelConfigChangedEvent Changed(
        Guid widgetId,
        string action,
        string domain = "widgets"
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            Domain = domain,
            EntityId = widgetId.ToString(),
            Action = action,
        };
}
