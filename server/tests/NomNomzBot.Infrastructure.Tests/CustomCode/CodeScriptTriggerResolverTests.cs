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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.Tests.Consequences;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// The editor types a script for the triggers that really run it: the resolver follows script -> run_code step ->
/// pipeline -> the command, the event response and the pipeline trigger rows that start that pipeline. Another
/// channel's pipeline never counts, and a script no pipeline runs has no triggers.
/// </summary>
public sealed class CodeScriptTriggerResolverTests
{
    private static readonly Guid Channel = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherChannel = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static CodeScriptTriggerResolver Build(BlastRadiusTestDbContext db)
    {
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);
        return new CodeScriptTriggerResolver(db, tenant);
    }

    private static Pipeline NewPipeline(Guid broadcaster, string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Name = name,
        };

    private static PipelineStep RunCode(Guid broadcaster, Guid pipelineId, Guid scriptId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            PipelineId = pipelineId,
            ActionType = "run_code",
            ConfigJson = "{}",
            CodeScriptId = scriptId,
            Order = 0,
        };

    private static CodeScript NewScript(Guid broadcaster, string name) =>
        new() { BroadcasterId = broadcaster, Name = name };

    private static Command NewCommand(Guid broadcaster, Guid pipelineId, string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Name = name,
            NameNormalized = name,
            PipelineId = pipelineId,
        };

    private static EventResponse NewEventResponse(
        Guid broadcaster,
        Guid pipelineId,
        string eventType
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            EventType = eventType,
            ResponseType = "pipeline",
            PipelineId = pipelineId,
        };

    private static PipelineTrigger NewTrigger(
        Guid pipelineId,
        string kind,
        string configJson,
        int order
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            PipelineId = pipelineId,
            Kind = kind,
            ConfigJson = configJson,
            Order = order,
        };

    private static async Task SeedChannelsAsync(BlastRadiusTestDbContext db)
    {
        foreach (
            (Guid id, string name) in new[] { (Channel, "streamer"), (OtherChannel, "someone") }
        )
        {
            db.Channels.Add(
                new()
                {
                    Id = id,
                    OwnerUserId = Guid.CreateVersion7(),
                    TwitchChannelId = id.ToString(),
                    Name = name,
                    NameNormalized = name,
                    OverlayToken = name + "-token",
                }
            );
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_script_run_by_a_command_and_a_follow_response_has_both_trigger_keys()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        CodeScript script = NewScript(Channel, "greet");
        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            await SeedChannelsAsync(seed);
            Pipeline viaCommand = NewPipeline(Channel, "hello");
            Pipeline viaFollow = NewPipeline(Channel, "thanks");
            seed.Pipelines.AddRange(viaCommand, viaFollow);
            seed.CodeScripts.Add(script);
            seed.PipelineSteps.AddRange(
                RunCode(Channel, viaCommand.Id, script.Id),
                RunCode(Channel, viaFollow.Id, script.Id)
            );
            seed.Commands.Add(NewCommand(Channel, viaCommand.Id, "hello"));
            seed.EventResponses.Add(NewEventResponse(Channel, viaFollow.Id, "channel.follow"));
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<IReadOnlyList<string>> result = await Build(db)
            .GetTriggerKeysAsync(script.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal("channel.follow", "command");
    }

    [Fact]
    public async Task A_pipeline_trigger_row_contributes_its_kind_key()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        CodeScript script = NewScript(Channel, "raid");
        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            await SeedChannelsAsync(seed);
            Pipeline pipeline = NewPipeline(Channel, "raids");
            seed.Pipelines.Add(pipeline);
            seed.CodeScripts.Add(script);
            seed.PipelineSteps.Add(RunCode(Channel, pipeline.Id, script.Id));
            seed.PipelineTriggers.AddRange(
                NewTrigger(pipeline.Id, "event", """{"EventType":"channel.raid"}""", 0),
                NewTrigger(pipeline.Id, "command", """{"Name":"raid"}""", 1),
                NewTrigger(pipeline.Id, "timer", """{"TimerId":"x"}""", 2)
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<IReadOnlyList<string>> result = await Build(db)
            .GetTriggerKeysAsync(script.Id, CancellationToken.None);

        result.Value.Should().Equal("channel.raid", "command");
    }

    [Fact]
    public async Task Another_channels_pipeline_using_the_same_script_id_is_not_included()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        CodeScript script = NewScript(Channel, "greet");
        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            await SeedChannelsAsync(seed);
            Pipeline mine = NewPipeline(Channel, "mine");
            Pipeline foreign = NewPipeline(OtherChannel, "theirs");
            seed.Pipelines.AddRange(mine, foreign);
            seed.CodeScripts.Add(script);
            seed.PipelineSteps.AddRange(
                RunCode(Channel, mine.Id, script.Id),
                RunCode(OtherChannel, foreign.Id, script.Id)
            );
            seed.EventResponses.Add(NewEventResponse(Channel, mine.Id, "channel.follow"));
            seed.EventResponses.Add(NewEventResponse(OtherChannel, foreign.Id, "channel.raid"));
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<IReadOnlyList<string>> result = await Build(db)
            .GetTriggerKeysAsync(script.Id, CancellationToken.None);

        result.Value.Should().Equal("channel.follow");
    }

    [Fact]
    public async Task A_script_no_pipeline_runs_has_no_trigger_keys()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        CodeScript script = NewScript(Channel, "lonely");
        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            await SeedChannelsAsync(seed);
            seed.CodeScripts.Add(script);
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<IReadOnlyList<string>> result = await Build(db)
            .GetTriggerKeysAsync(script.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_script_and_another_tenants_script_are_not_found()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        CodeScript foreign = NewScript(OtherChannel, "theirs");
        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            await SeedChannelsAsync(seed);
            seed.CodeScripts.Add(foreign);
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        CodeScriptTriggerResolver sut = Build(db);

        Result<IReadOnlyList<string>> unknown = await sut.GetTriggerKeysAsync(
            Guid.CreateVersion7(),
            CancellationToken.None
        );
        Result<IReadOnlyList<string>> other = await sut.GetTriggerKeysAsync(
            foreign.Id,
            CancellationToken.None
        );

        unknown.ErrorCode.Should().Be("NOT_FOUND");
        other.ErrorCode.Should().Be("NOT_FOUND");
    }
}
