// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Infrastructure.Obs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves how the browser-source read is built from OBS-WS answers: only inputs of kind <c>browser_source</c> are
/// asked for, each source reports the address from its settings, and "loaded" follows OBS's own rule (a source
/// that does not shut down while hidden is always running; one that does is running only while it shows).
/// </summary>
public sealed class ObsBrowserSourceReadTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f911");

    private sealed record SourceState(string Name, string SettingsJson, bool VideoActive);

    private sealed class Rig
    {
        public required ObsControlService Service { get; init; }
        public required List<ObsRequest> Requests { get; init; }
    }

    private static Rig Build(params (string Name, string Settings, bool Active)[] declared) =>
        BuildWithHidden([], declared);

    // Every declared source sits in the one scene "Main"; the names in hidden have their scene item switched off.
    private static Rig BuildWithHidden(
        HashSet<string> hidden,
        params (string Name, string Settings, bool Active)[] declared
    )
    {
        SourceState[] sources =
        [
            .. declared.Select(d => new SourceState(d.Name, d.Settings, d.Active)),
        ];
        List<ObsRequest> requests = [];
        Dictionary<string, SourceState> byName = sources.ToDictionary(s => s.Name);
        IObsTransport transport = Substitute.For<IObsTransport>();
        transport
            .SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<ObsRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                ObsRequest request = call.ArgAt<ObsRequest>(2);
                requests.Add(request);
                return Task.FromResult(Result.Success(Answer(request, sources, byName, hidden)));
            });
        transport
            .SendBatchAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<ObsRequestBatch>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                ObsRequestBatch batch = call.ArgAt<ObsRequestBatch>(2);
                List<ObsResponse> answers = [];
                foreach (ObsRequest request in batch.Requests)
                {
                    requests.Add(request);
                    answers.Add(Answer(request, sources, byName, hidden));
                }
                return Task.FromResult(Result.Success<IReadOnlyList<ObsResponse>>(answers));
            });
        return new() { Service = new(transport), Requests = requests };
    }

    private static ObsResponse Answer(
        ObsRequest request,
        SourceState[] sources,
        Dictionary<string, SourceState> byName,
        HashSet<string> hidden
    )
    {
        string inputName = request.RequestData?.GetValueOrDefault("inputName") as string ?? "";
        string sourceName = request.RequestData?.GetValueOrDefault("sourceName") as string ?? "";
        switch (request.RequestType)
        {
            case "GetInputList":
                string entries = string.Join(
                    ",",
                    sources.Select(s =>
                        $$"""{"inputName":"{{s.Name}}","inputKind":"browser_source"}"""
                    )
                );
                return new(
                    true,
                    new Dictionary<string, object?> { ["inputs"] = $"[{entries}]" },
                    null
                );
            case "GetSceneList":
                return new(
                    true,
                    new Dictionary<string, object?>
                    {
                        ["currentProgramSceneName"] = "Main",
                        ["scenes"] = """[{"sceneName":"Main","sceneIndex":0}]""",
                    },
                    null
                );
            case "GetSceneItemList":
                string items = string.Join(
                    ",",
                    sources.Select(
                        (s, index) =>
                            $$"""{"sceneItemId":{{index + 1}},"sourceName":"{{s.Name}}","sceneItemEnabled":{{(hidden.Contains(s.Name) ? "false" : "true")}},"sceneItemLocked":false}"""
                    )
                );
                return new(
                    true,
                    new Dictionary<string, object?> { ["sceneItems"] = $"[{items}]" },
                    null
                );
            case "GetInputSettings":
                return new(
                    true,
                    new Dictionary<string, object?>
                    {
                        ["inputSettings"] = byName[inputName].SettingsJson,
                    },
                    null
                );
            case "GetSourceActive":
                return new(
                    true,
                    new Dictionary<string, object?>
                    {
                        ["videoActive"] = byName[sourceName].VideoActive,
                        ["videoShowing"] = byName[sourceName].VideoActive,
                    },
                    null
                );
            default:
                return new(false, null, "unexpected request");
        }
    }

    [Fact]
    public async Task Asks_OBS_only_for_browser_source_inputs()
    {
        Rig rig = Build(
            ("Alerts", """{"url":"https://bot.example.test/overlay?widgetId=1"}""", true)
        );

        await rig.Service.GetBrowserSourcesAsync(Channel);

        ObsRequest list = rig
            .Requests.Should()
            .Contain(r => r.RequestType == "GetInputList")
            .Subject;
        list.RequestData!["inputKind"].Should().Be("browser_source");
    }

    [Fact]
    public async Task Reports_the_address_each_source_loads()
    {
        Rig rig = Build(
            ("Alerts", """{"url":"https://bot.example.test/overlay?widgetId=1&token=t"}""", true),
            ("Website", """{"url":"https://example.test/page"}""", true)
        );

        Result<IReadOnlyList<ObsBrowserSourceDto>> result =
            await rig.Service.GetBrowserSourcesAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Select(s => (s.Name, s.Url))
            .Should()
            .Equal(
                ("Alerts", "https://bot.example.test/overlay?widgetId=1&token=t"),
                ("Website", "https://example.test/page")
            );
    }

    [Fact]
    public async Task A_source_that_stays_loaded_while_hidden_counts_as_loaded()
    {
        Rig rig = Build(
            ("Always on", """{"url":"https://a.test/overlay","shutdown":false}""", false)
        );

        Result<IReadOnlyList<ObsBrowserSourceDto>> result =
            await rig.Service.GetBrowserSourcesAsync(Channel);

        result.Value.Should().ContainSingle().Which.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task A_source_that_shuts_down_while_hidden_is_loaded_only_while_it_shows()
    {
        Rig rig = Build(
            ("Showing", """{"url":"https://a.test/overlay","shutdown":true}""", true),
            ("Hidden", """{"url":"https://b.test/overlay","shutdown":true}""", false)
        );

        Result<IReadOnlyList<ObsBrowserSourceDto>> result =
            await rig.Service.GetBrowserSourcesAsync(Channel);

        result
            .Value.Select(s => (s.Name, s.IsLoaded))
            .Should()
            .Equal(("Showing", true), ("Hidden", false));
    }

    [Fact]
    public async Task A_source_whose_scene_item_is_switched_off_is_reported_as_not_enabled_in_a_scene()
    {
        Rig rig = BuildWithHidden(
            ["Eye closed"],
            ("Eye open", """{"url":"https://a.test/overlay"}""", true),
            ("Eye closed", """{"url":"https://b.test/overlay"}""", true)
        );

        Result<IReadOnlyList<ObsBrowserSourceDto>> result =
            await rig.Service.GetBrowserSourcesAsync(Channel);

        result
            .Value.Select(s => (s.Name, s.IsEnabledInScene))
            .Should()
            .Equal(("Eye open", true), ("Eye closed", false));
    }

    [Fact]
    public async Task When_the_scenes_cannot_be_read_the_whole_read_fails_instead_of_guessing()
    {
        IObsTransport transport = Substitute.For<IObsTransport>();
        transport
            .SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<ObsRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                ObsRequest request = call.ArgAt<ObsRequest>(2);
                ObsResponse answer = request.RequestType switch
                {
                    "GetInputList" => new(
                        true,
                        new Dictionary<string, object?>
                        {
                            ["inputs"] =
                                """[{"inputName":"Alerts","inputKind":"browser_source"}]""",
                        },
                        null
                    ),
                    _ => new(false, null, "scene list refused"),
                };
                return Task.FromResult(Result.Success(answer));
            });
        transport
            .SendBatchAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<ObsRequestBatch>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                ObsRequestBatch batch = call.ArgAt<ObsRequestBatch>(2);
                List<ObsResponse> answers =
                [
                    .. batch.Requests.Select(r =>
                        r.RequestType == "GetInputSettings"
                            ? new ObsResponse(
                                true,
                                new Dictionary<string, object?>
                                {
                                    ["inputSettings"] = """{"url":"https://a.test/overlay"}""",
                                },
                                null
                            )
                            : new ObsResponse(
                                true,
                                new Dictionary<string, object?> { ["videoActive"] = true },
                                null
                            )
                    ),
                ];
                return Task.FromResult(Result.Success<IReadOnlyList<ObsResponse>>(answers));
            });

        Result<IReadOnlyList<ObsBrowserSourceDto>> result = await new ObsControlService(
            transport
        ).GetBrowserSourcesAsync(Channel);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_source_without_an_address_is_not_listed()
    {
        Rig rig = Build(("Local file", """{"is_local_file":true}""", true));

        Result<IReadOnlyList<ObsBrowserSourceDto>> result =
            await rig.Service.GetBrowserSourcesAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_input_list_is_returned_as_the_failure()
    {
        IObsTransport transport = Substitute.For<IObsTransport>();
        transport
            .SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<ObsRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    Result.Failure<ObsResponse>("OBS is not connected.", "OBS_NOT_CONNECTED")
                )
            );
        ObsControlService service = new(transport);

        Result<IReadOnlyList<ObsBrowserSourceDto>> result = await service.GetBrowserSourcesAsync(
            Channel
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("OBS_NOT_CONNECTED");
    }
}
