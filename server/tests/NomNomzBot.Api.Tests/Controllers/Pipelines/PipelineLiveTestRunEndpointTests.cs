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

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Api.Tests.Authentication;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;
using ActionDefinition = NomNomzBot.Domain.Identity.Entities.ActionDefinition;

namespace NomNomzBot.Api.Tests.Controllers.Pipelines;

/// <summary>
/// The live mode of <c>POST pipelines/{id}/test-run</c> over the real auth stack: a person who cannot edit
/// pipelines gets 403 and the run never starts, a second live start is a 409 with a clear code, and the mode
/// the client sent reaches the service.
/// </summary>
public sealed class PipelineLiveTestRunEndpointTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-000000000f01");
    private static readonly Guid Viewer = Guid.Parse("0192b000-0000-7000-8000-000000000f03");
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-000000000f02");
    private static readonly Guid PipelineId = Guid.Parse("0192b000-0000-7000-8000-000000000f04");

    private readonly IPipelineTestRunService _testRun = Substitute.For<IPipelineTestRunService>();
    private ActAsTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ActAsTestHost.StartAsync(
            Guid.Parse("0192b000-0000-7000-8000-0000000000ff"),
            services =>
            {
                // Only the test-run service is under test; the controller's other collaborators are never reached.
                services.AddSingleton(_testRun);
                services.AddSingleton(Substitute.For<IPipelineService>());
                services.AddSingleton(Substitute.For<ICommandConfigValidator>());
                services.AddSingleton(Substitute.For<IPlatformDefaultRestoreService>());
            }
        );
        await _host.SeedAsync(SeedAsync);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpResponseMessage> PostLiveAsync(Guid user)
    {
        string token = (await _host.LoginAsync(user, ChannelId)).AccessToken;
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/api/v1/channels/{ChannelId}/pipelines/{PipelineId}/test-run"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(
            new { variables = new Dictionary<string, string> { ["viewers"] = "42" }, mode = "live" }
        );
        return await _host.Client.SendAsync(request);
    }

    [Fact]
    public async Task A_person_who_cannot_edit_pipelines_gets_403_and_no_live_run_starts()
    {
        HttpResponseMessage response = await PostLiveAsync(Viewer);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["action"]!.GetValue<string>().Should().Be("pipelines:write");
        await _testRun
            .DidNotReceiveWithAnyArgs()
            .RunAsync(default, default!, CancellationToken.None);
    }

    [Fact]
    public async Task A_second_live_start_while_one_runs_is_a_409_with_the_run_in_progress_code()
    {
        _testRun
            .RunAsync(PipelineId, Arg.Any<PipelineTestRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TestRunResultDto>(
                    "A live run of this pipeline is already running. Wait for it to finish.",
                    "LIVE_RUN_IN_PROGRESS"
                )
            );

        HttpResponseMessage response = await PostLiveAsync(Owner);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["code"]!.GetValue<string>().Should().Be("LIVE_RUN_IN_PROGRESS");
        body["message"]!.GetValue<string>().Should().Contain("already running");
    }

    [Fact]
    public async Task The_live_mode_and_start_values_reach_the_service_and_the_result_carries_the_warnings()
    {
        _testRun
            .RunAsync(PipelineId, Arg.Any<PipelineTestRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TestRunResultDto(true, null, 5, 0, [], [], [], null, null)
                    {
                        Mode = "live",
                        RealSteps = ["play_sound"],
                        Warnings = ["Step 0 (play_sound) shows on a browser source."],
                    }
                )
            );

        HttpResponseMessage response = await PostLiveAsync(Owner);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        data["mode"]!.GetValue<string>().Should().Be("live");
        data["realSteps"]![0]!.GetValue<string>().Should().Be("play_sound");
        data["warnings"]![0]!.GetValue<string>().Should().Contain("browser source");
        await _testRun
            .Received(1)
            .RunAsync(
                PipelineId,
                Arg.Is<PipelineTestRunRequest>(r =>
                    r.Mode == PipelineTestRunModes.Live && r.Variables["viewers"] == "42"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Users.AddRange(User(Owner, "owner_f"), User(Viewer, "viewer_f"));
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                OwnerUserId = Owner,
                Name = "owner_f",
                NameNormalized = "owner_f",
                TwitchChannelId = ChannelId.ToString("N")[^10..],
                ExternalChannelId = ChannelId.ToString("N")[^12..],
                IsOnboarded = true,
            }
        );
        db.ActionDefinitions.Add(
            new ActionDefinition
            {
                ActionKey = "pipelines:write",
                Plane = AuthPlane.Management,
                DefaultLevel = 30,
                FloorLevel = 10,
                FloorTier = DangerTier.Low,
            }
        );
        await Task.CompletedTask;
    }

    private static User User(Guid id, string username) =>
        new()
        {
            Id = id,
            TwitchUserId = id.ToString("N")[^10..],
            Username = username,
            UsernameNormalized = username,
            DisplayName = username,
            Color = "#1E90FF",
        };
}
