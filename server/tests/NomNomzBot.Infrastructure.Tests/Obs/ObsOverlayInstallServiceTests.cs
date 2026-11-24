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

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Obs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves the one-call overlay install against a fake OBS that keeps its own scenes, inputs and scene items. Only
/// the OBS transport is faked: the real control service builds every request, so each case asserts the exact
/// request types and payloads OBS received, in order, and the per-overlay result the caller gets back.
/// </summary>
public sealed class ObsOverlayInstallServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000fa01");
    private const string Origin = "https://bot.example.test";
    private const string Nested = "NomNomzBot overlays";

    private sealed class FakeObs
    {
        public List<ObsRequest> Requests { get; } = [];
        public List<string> Scenes { get; } = ["Main"];
        public Dictionary<string, Dictionary<string, object?>> Inputs { get; } = [];
        public Dictionary<string, List<string>> Items { get; } = [];
        public string? FailInputNamed { get; set; }

        public ObsResponse Answer(ObsRequest request)
        {
            Requests.Add(request);
            IReadOnlyDictionary<string, object?> data =
                request.RequestData ?? new Dictionary<string, object?>();
            switch (request.RequestType)
            {
                case "GetSceneList":
                    return Ok(
                        ("currentProgramSceneName", "Main"),
                        (
                            "scenes",
                            JsonSerializer.Serialize(Scenes.Select(s => new { sceneName = s }))
                        )
                    );
                case "CreateScene":
                    Scenes.Add((string)data["sceneName"]!);
                    return Ok();
                case "GetInputList":
                    return Ok(
                        (
                            "inputs",
                            JsonSerializer.Serialize(
                                Inputs.Keys.Select(n => new
                                {
                                    inputName = n,
                                    inputKind = "browser_source",
                                })
                            )
                        )
                    );
                case "CreateInput":
                    if ((string)data["inputName"]! == FailInputNamed)
                        return new(false, null, "Input could not be created");
                    Inputs[(string)data["inputName"]!] =
                        (Dictionary<string, object?>)data["inputSettings"]!;
                    ItemsOf((string)data["sceneName"]!).Add((string)data["inputName"]!);
                    return Ok();
                case "SetInputSettings":
                    Inputs[(string)data["inputName"]!] =
                        (Dictionary<string, object?>)data["inputSettings"]!;
                    return Ok();
                case "GetSceneItemList":
                    return Ok(
                        (
                            "sceneItems",
                            JsonSerializer.Serialize(
                                ItemsOf((string)data["sceneName"]!)
                                    .Select(
                                        (n, i) =>
                                            new
                                            {
                                                sceneItemId = i + 1,
                                                sourceName = n,
                                                sceneItemEnabled = true,
                                                sceneItemLocked = false,
                                            }
                                    )
                            )
                        )
                    );
                case "CreateSceneItem":
                    ItemsOf((string)data["sceneName"]!).Add((string)data["sourceName"]!);
                    return Ok(("sceneItemId", 9d));
                default:
                    return new(false, null, $"unexpected {request.RequestType}");
            }
        }

        private List<string> ItemsOf(string scene)
        {
            if (!Items.TryGetValue(scene, out List<string>? list))
                Items[scene] = list = [];
            return list;
        }

        private static ObsResponse Ok(params (string Key, object? Value)[] data) =>
            new(true, data.ToDictionary(d => d.Key, d => d.Value), null);
    }

    private static (ObsOverlayInstallService Service, FakeObs Obs) Build(
        bool connected = true,
        FakeObs? obs = null
    )
    {
        FakeObs fake = obs ?? new FakeObs();
        IObsTransport transport = Substitute.For<IObsTransport>();
        transport
            .SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<ObsRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
                Task.FromResult(
                    connected
                        ? Result.Success(fake.Answer(ci.ArgAt<ObsRequest>(2)))
                        : Result.Failure<ObsResponse>("OBS is not connected.", "OBS_NOT_CONNECTED")
                )
            );

        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .EnsureSystemWidgetAsync(
                Channel.ToString(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                string key = ci.ArgAt<string>(1);
                ObsBrowserSource setup =
                    key == "audio" ? new(1, 1, true, false) : new(1920, 1080, false, false);
                return Task.FromResult(
                    Result.Success(
                        new WidgetDetail(
                            Guid.CreateVersion7(),
                            key == "audio" ? "Bot Audio" : "Alerts",
                            null,
                            "vue",
                            "",
                            true,
                            $"http://localhost:8080/overlay?widgetId={key}&token=SECRET-{key}",
                            null,
                            null,
                            [],
                            [],
                            null,
                            null,
                            DateTime.UtcNow,
                            DateTime.UtcNow,
                            false,
                            false,
                            false,
                            setup
                        )
                    )
                );
            });

        return (new ObsOverlayInstallService(new ObsControlService(transport), widgets), fake);
    }

    private static string Type(ObsRequest r) => r.RequestType;

    [Fact]
    public async Task First_install_creates_the_nested_scene_one_source_per_picked_overlay_and_adds_it_to_the_scene()
    {
        (ObsOverlayInstallService service, FakeObs obs) = Build();

        Result<ObsOverlayInstallResult> result = await service.InstallAsync(
            Channel,
            "Main",
            ["alerts", "audio"],
            Origin
        );

        result.IsSuccess.Should().BeTrue();
        obs.Requests.Select(Type)
            .Should()
            .Equal(
                "GetSceneList",
                "CreateScene",
                "GetInputList",
                "CreateInput",
                "GetInputList",
                "CreateInput",
                "GetSceneItemList",
                "CreateSceneItem"
            );
        obs.Requests[1]
            .RequestData.Should()
            .BeEquivalentTo(new Dictionary<string, object?> { ["sceneName"] = Nested });

        ObsRequest alerts = obs.Requests[3];
        alerts.RequestData!["sceneName"].Should().Be(Nested);
        alerts.RequestData["inputName"].Should().Be("NomNomzBot - Alerts");
        alerts.RequestData["inputKind"].Should().Be("browser_source");
        alerts
            .RequestData["inputSettings"]
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, object?>
                {
                    ["url"] = $"{Origin}/overlay?widgetId=alerts&token=SECRET-alerts",
                    ["width"] = 1920,
                    ["height"] = 1080,
                    ["reroute_audio"] = false,
                    ["shutdown"] = false,
                }
            );

        ObsRequest audio = obs.Requests[5];
        audio.RequestData!["inputName"].Should().Be("NomNomzBot - Bot Audio");
        audio
            .RequestData["inputSettings"]
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, object?>
                {
                    ["url"] = $"{Origin}/overlay?widgetId=audio&token=SECRET-audio",
                    ["width"] = 1,
                    ["height"] = 1,
                    ["reroute_audio"] = true,
                    ["shutdown"] = false,
                }
            );

        obs.Requests[7]
            .RequestData.Should()
            .BeEquivalentTo(
                new Dictionary<string, object?>
                {
                    ["sceneName"] = "Main",
                    ["sourceName"] = Nested,
                    ["sceneItemEnabled"] = true,
                }
            );

        result.Value.AddedToScene.Should().BeTrue();
        result
            .Value.Overlays.Select(o => (o.Key, o.SourceName, o.Outcome, o.Error))
            .Should()
            .Equal(
                ("alerts", "NomNomzBot - Alerts", ObsOverlayInstallOutcome.Created, null),
                ("audio", "NomNomzBot - Bot Audio", ObsOverlayInstallOutcome.Created, null)
            );
        JsonSerializer
            .Serialize(result.Value)
            .Should()
            .NotContain("SECRET")
            .And.NotContain("token");
    }

    [Fact]
    public async Task Second_install_updates_the_sources_and_creates_nothing_twice()
    {
        (ObsOverlayInstallService service, FakeObs obs) = Build();
        await service.InstallAsync(Channel, "Main", ["alerts"], Origin);
        obs.Requests.Clear();

        Result<ObsOverlayInstallResult> result = await service.InstallAsync(
            Channel,
            "Main",
            ["alerts"],
            Origin
        );

        obs.Requests.Select(Type)
            .Should()
            .Equal("GetSceneList", "GetInputList", "SetInputSettings", "GetSceneItemList");
        obs.Requests[2].RequestData!["inputName"].Should().Be("NomNomzBot - Alerts");
        obs.Requests[2]
            .RequestData!["inputSettings"]
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, object?>
                {
                    ["url"] = $"{Origin}/overlay?widgetId=alerts&token=SECRET-alerts",
                    ["width"] = 1920,
                    ["height"] = 1080,
                    ["reroute_audio"] = false,
                    ["shutdown"] = false,
                }
            );
        result.Value.AddedToScene.Should().BeFalse();
        result.Value.Overlays.Single().Outcome.Should().Be(ObsOverlayInstallOutcome.Updated);
        obs.Inputs.Keys.Should().Equal("NomNomzBot - Alerts");
    }

    [Fact]
    public async Task Only_the_picked_overlays_are_installed()
    {
        (ObsOverlayInstallService service, FakeObs obs) = Build();

        await service.InstallAsync(Channel, "Main", ["audio"], Origin);

        obs.Inputs.Keys.Should().Equal("NomNomzBot - Bot Audio");
    }

    [Fact]
    public async Task One_overlay_OBS_refuses_is_reported_failed_and_the_others_still_install()
    {
        FakeObs failing = new() { FailInputNamed = "NomNomzBot - Alerts" };
        (ObsOverlayInstallService service, FakeObs obs) = Build(obs: failing);

        Result<ObsOverlayInstallResult> result = await service.InstallAsync(
            Channel,
            "Main",
            ["alerts", "audio"],
            Origin
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Overlays[0].Outcome.Should().Be(ObsOverlayInstallOutcome.Failed);
        result.Value.Overlays[0].Error.Should().Be("Input could not be created");
        result.Value.Overlays[1].Outcome.Should().Be(ObsOverlayInstallOutcome.Created);
        obs.Inputs.Keys.Should().Equal("NomNomzBot - Bot Audio");
    }

    [Fact]
    public async Task An_unknown_overlay_key_fails_only_that_overlay()
    {
        (ObsOverlayInstallService service, FakeObs obs) = Build();

        Result<ObsOverlayInstallResult> result = await service.InstallAsync(
            Channel,
            "Main",
            ["nonsense"],
            Origin
        );

        result.Value.Overlays.Single().Outcome.Should().Be(ObsOverlayInstallOutcome.Failed);
        obs.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_target_scene_fails_before_anything_is_created()
    {
        (ObsOverlayInstallService service, FakeObs obs) = Build();

        Result<ObsOverlayInstallResult> result = await service.InstallAsync(
            Channel,
            "Nope",
            ["alerts"],
            Origin
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        obs.Requests.Select(Type).Should().Equal("GetSceneList");
    }

    [Fact]
    public async Task No_connection_gives_the_transport_failure_code()
    {
        (ObsOverlayInstallService service, FakeObs _) = Build(connected: false);

        Result<ObsOverlayInstallResult> result = await service.InstallAsync(
            Channel,
            "Main",
            ["alerts"],
            Origin
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("OBS_NOT_CONNECTED");
    }

    [Fact]
    public async Task Placements_put_each_source_in_its_own_scene_with_the_chosen_name_and_make_no_scene()
    {
        FakeObs fake = new();
        fake.Scenes.Add("Starting");
        (ObsOverlayInstallService service, FakeObs obs) = Build(obs: fake);

        Result<ObsOverlayInstallResult> result = await service.PlaceAsync(
            Channel,
            [new("alerts", "Main", "My alerts"), new("audio", "Starting", null)],
            Origin
        );

        result.IsSuccess.Should().BeTrue();
        obs.Requests.Select(Type).Should().NotContain("CreateScene");
        obs.Scenes.Should().Equal("Main", "Starting");
        obs.Items["Main"].Should().Equal("My alerts");
        obs.Items["Starting"].Should().Equal("Bot Audio");
        result.Value.NestedSceneName.Should().BeNull();
        result.Value.AddedToScene.Should().BeFalse();
        result
            .Value.Overlays.Select(o => (o.Key, o.SourceName, o.Outcome))
            .Should()
            .Equal(
                ("alerts", "My alerts", ObsOverlayInstallOutcome.Created),
                ("audio", "Bot Audio", ObsOverlayInstallOutcome.Created)
            );
    }

    [Fact]
    public async Task A_placed_source_that_already_exists_is_updated_and_added_to_the_chosen_scene_once()
    {
        FakeObs fake = new();
        fake.Scenes.Add("Starting");
        (ObsOverlayInstallService service, FakeObs obs) = Build(obs: fake);
        await service.PlaceAsync(Channel, [new("alerts", "Main", "Alerts")], Origin);

        await service.PlaceAsync(Channel, [new("alerts", "Starting", "Alerts")], Origin);
        Result<ObsOverlayInstallResult> again = await service.PlaceAsync(
            Channel,
            [new("alerts", "Starting", "Alerts")],
            Origin
        );

        obs.Inputs.Keys.Should().Equal("Alerts");
        obs.Items["Main"].Should().Equal("Alerts");
        obs.Items["Starting"].Should().Equal("Alerts");
        again.Value.Overlays.Single().Outcome.Should().Be(ObsOverlayInstallOutcome.Updated);
    }

    [Fact]
    public async Task A_placement_into_a_scene_OBS_does_not_have_fails_only_that_overlay()
    {
        (ObsOverlayInstallService service, FakeObs obs) = Build();

        Result<ObsOverlayInstallResult> result = await service.PlaceAsync(
            Channel,
            [new("alerts", "Gone", null), new("audio", "Main", null)],
            Origin
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Overlays.Select(o => (o.Key, o.Outcome))
            .Should()
            .Equal(
                ("alerts", ObsOverlayInstallOutcome.Failed),
                ("audio", ObsOverlayInstallOutcome.Created)
            );
        obs.Items["Main"].Should().Equal("Bot Audio");
        obs.Inputs.Keys.Should().Equal("Bot Audio");
    }
}
