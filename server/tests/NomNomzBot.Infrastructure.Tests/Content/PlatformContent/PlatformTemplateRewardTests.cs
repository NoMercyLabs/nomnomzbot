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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using NomNomzBot.Infrastructure.Rewards;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// The <c>reward</c> platform-template kind, end to end: author and publish, then install through the real
/// <see cref="RewardService"/> with Twitch push on. Proves the reward is created on the INSTALLING channel's
/// Twitch, the local row mirrors what Twitch confirmed plus provenance, a duplicate title is refused, and a
/// bound pipeline must belong to the installing channel.
/// </summary>
public sealed class PlatformTemplateRewardTests : IAsyncDisposable
{
    private const string HydrateTemplate =
        """{"title":"Hydrate","cost":500,"prompt":"Make me drink water","response":"{user} made me drink!","isUserInputRequired":false,"backgroundColor":"#00AAFF","maxPerStream":10,"globalCooldownSeconds":300,"timerDurationSeconds":60}""";

    private readonly PlatformTemplateHarness _h = new();
    private readonly ITwitchChannelPointsApi _channelPoints =
        Substitute.For<ITwitchChannelPointsApi>();
    private readonly List<(Guid Broadcaster, CreateCustomRewardRequest Request)> _twitchCreates =
    [];
    private readonly RewardTemplateInstaller _installer;

    public PlatformTemplateRewardTests()
    {
        TwitchHasRewards();
        _channelPoints
            .CreateCustomRewardAsync(
                Arg.Any<Guid>(),
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                CreateCustomRewardRequest request = call.ArgAt<CreateCustomRewardRequest>(1);
                _twitchCreates.Add((call.ArgAt<Guid>(0), request));
                return Result.Success(ConfirmedByTwitch(request));
            });
        RewardService rewards = new(
            _h.Db,
            _channelPoints,
            TimeProvider.System,
            NullLogger<RewardService>.Instance
        );
        _installer = new(_h.Db, rewards);
    }

    private void TwitchHasRewards(params string[] titles) =>
        _channelPoints
            .GetCustomRewardsAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchCustomReward>>([.. titles.Select(Existing)])
            );

    private static TwitchCustomReward ConfirmedByTwitch(CreateCustomRewardRequest request) =>
        new(
            "b",
            "b",
            "b",
            "twitch-reward-1",
            request.Title,
            request.Prompt ?? string.Empty,
            request.Cost,
            null,
            new("1", "2", "4"),
            request.BackgroundColor ?? "#9146FF",
            true,
            request.IsUserInputRequired ?? false,
            new(request.IsMaxPerStreamEnabled ?? false, request.MaxPerStream ?? 0),
            new(request.IsMaxPerUserPerStreamEnabled ?? false, request.MaxPerUserPerStream ?? 0),
            new(request.IsGlobalCooldownEnabled ?? false, request.GlobalCooldownSeconds ?? 0),
            false,
            true,
            false,
            null,
            null
        );

    private static TwitchCustomReward Existing(string title) =>
        ConfirmedByTwitch(new(Title: title, Cost: 1)) with
        {
            Id = "existing-" + title,
        };

    [Fact]
    public async Task Install_creates_the_reward_on_the_installing_channels_twitch_with_provenance()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(_installer, "hydrate", HydrateTemplate);

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (Guid broadcaster, CreateCustomRewardRequest sent) = _twitchCreates
            .Should()
            .ContainSingle()
            .Subject;
        broadcaster.Should().Be(channel.Id);
        sent.Title.Should().Be("Hydrate");
        sent.Cost.Should().Be(500);
        sent.Prompt.Should().Be("Make me drink water");
        sent.BackgroundColor.Should().Be("#00AAFF");
        sent.MaxPerStream.Should().Be(10);
        sent.GlobalCooldownSeconds.Should().Be(300);

        Reward row = await _h.Db.Rewards.SingleAsync();
        row.Id.Should().Be(result.Value.EntityId);
        row.BroadcasterId.Should().Be(channel.Id);
        row.TwitchRewardId.Should().Be("twitch-reward-1");
        row.Title.Should().Be("Hydrate");
        row.Cost.Should().Be(500);
        row.Description.Should().Be("Make me drink water");
        row.Response.Should().Be("{user} made me drink!");
        row.MaxPerStream.Should().Be(10);
        row.MaxPerUserPerStream.Should().BeNull();
        row.GlobalCooldownSeconds.Should().Be(300);
        row.TimerDurationSeconds.Should().Be(60);
        row.IsManageable.Should().BeTrue();
        row.PlatformSourceDefinitionId.Should().Be(definitionId);
        row.PlatformSourceVersion.Should().Be(1);
        row.PlatformSourceHash.Should().Be(RewardTemplatePayload.FromEntity(row).ComputeHash());
    }

    [Fact]
    public async Task Install_refuses_a_title_the_channel_already_has_on_twitch_and_writes_nothing()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(_installer, "hydrate", HydrateTemplate);
        TwitchHasRewards("hydrate");

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.ErrorCode.Should().Be("ALREADY_EXISTS");
        _twitchCreates.Should().BeEmpty();
        (await _h.Db.Rewards.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_bound_pipeline_must_belong_to_the_installing_channel()
    {
        Channel channelA = await _h.AddChannelAsync("streamer-a");
        Channel channelB = await _h.AddChannelAsync("streamer-b");
        Pipeline pipelineOfA = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channelA.Id,
            Name = "a-flow",
        };
        Pipeline pipelineOfB = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channelB.Id,
            Name = "b-flow",
        };
        _h.Db.Pipelines.AddRange(pipelineOfA, pipelineOfB);
        await _h.Db.SaveChangesAsync();
        Guid definitionId = await _h.PublishTemplateAsync(_installer, "hydrate", HydrateTemplate);
        PlatformTemplateCatalogService catalog = _h.Catalog(_installer);

        Result<InstalledPlatformTemplateDto> foreign = await catalog.InstallAsync(
            _h.CallerUserId,
            channelB.Id,
            definitionId,
            new(pipelineOfA.Id)
        );

        foreign.ErrorCode.Should().Be(PipelineOwnership.ErrorCode);
        _twitchCreates.Should().BeEmpty();
        (await _h.Db.Rewards.CountAsync()).Should().Be(0);

        Result<InstalledPlatformTemplateDto> own = await catalog.InstallAsync(
            _h.CallerUserId,
            channelB.Id,
            definitionId,
            new(pipelineOfB.Id)
        );

        own.IsSuccess.Should().BeTrue(own.ErrorMessage);
        Reward row = await _h.Db.Rewards.SingleAsync();
        row.BroadcasterId.Should().Be(channelB.Id);
        row.PipelineId.Should().Be(pipelineOfB.Id);
    }

    [Theory]
    [InlineData("""{"title":"","cost":10}""")]
    [InlineData("""{"title":"A title that is far too long for a Twitch reward title","cost":10}""")]
    [InlineData("""{"title":"x","cost":0}""")]
    [InlineData("""{"title":"x","cost":10,"backgroundColor":"blue"}""")]
    [InlineData("""{"title":"x","cost":10,"maxPerStream":0}""")]
    [InlineData("""{"title":"x","cost":10,"globalCooldownSeconds":604801}""")]
    [InlineData("""{"title":"x","cost":10,"timerDurationSeconds":0}""")]
    public async Task Authoring_rejects_a_bad_payload_and_stores_nothing(string payload)
    {
        Result<PlatformContentDefinitionDto> created = await _h.AdminService(_installer)
            .CreateDefinitionAsync(
                _h.ActingPrincipalId,
                new(PlatformContentKinds.Reward, "bad", "Bad", null, payload)
            );

        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await _h.Db.PlatformContentDefinitions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Install_is_denied_without_the_reward_manage_action()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(_installer, "hydrate", HydrateTemplate);
        _h.AllowChannelAction(false);

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.ErrorCode.Should().Be("FORBIDDEN");
        await _h
            .Authorization.Received()
            .AuthorizeActionAsync(
                _h.CallerUserId,
                channel.Id,
                "reward:manage",
                Arg.Any<CancellationToken>()
            );
        _twitchCreates.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();
}
