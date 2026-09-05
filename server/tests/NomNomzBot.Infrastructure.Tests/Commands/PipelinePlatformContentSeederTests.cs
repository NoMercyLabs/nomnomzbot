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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using NomNomzBot.Infrastructure.Tests.Content;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S-ADMIN-2d: <see cref="PipelinePlatformContentSeeder"/> registers the three raid flows as
/// <c>Kind = "pipeline"</c> platform content, and backfills provenance onto tenant <see cref="Pipeline"/>
/// rows the raid seeders already built — the link that puts a real tenant row into the publish spine's
/// fan-out (the gap the widget kind first shipped with, S-ADMIN-2c, fixed S-ADMIN-2c-b).
/// </summary>
public sealed class PipelinePlatformContentSeederTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4c00-4444-7000-8000-000000000001");

    private static SeedTestDbContext BuildDb()
    {
        SeedTestDbContext db = SeedTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "pipeline-content-test",
                NameNormalized = "pipeline-content-test",
            }
        );
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Registers_all_three_raid_flows_as_published_pipeline_kind_definitions()
    {
        SeedTestDbContext db = BuildDb();

        await new PipelinePlatformContentSeeder(db).SeedAsync();

        List<PlatformContentDefinition> definitions = db
            .PlatformContentDefinitions.Where(d => d.Kind == PlatformContentKinds.Pipeline)
            .ToList();

        definitions
            .Select(d => d.Key)
            .Should()
            .BeEquivalentTo("raid_out", "raid_start", "raid_commit");
        definitions
            .Should()
            .OnlyContain(d => d.CurrentVersionId != null && d.LatestDraftVersionId != null);

        foreach (PlatformContentDefinition definition in definitions)
        {
            PlatformContentVersion v1 = db.PlatformContentVersions.Single(v =>
                v.Id == definition.CurrentVersionId
            );
            v1.Version.Should().Be(1);
            v1.PayloadJson.Should().Contain("\"steps\"");
            v1.ContentHash.Should().Be(PlatformContentHash.ComputeHash(v1.PayloadJson));
            v1.PublishedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Backfills_provenance_onto_the_tenants_raid_command_pipeline()
    {
        SeedTestDbContext db = BuildDb();
        await new RaidFlowSeeder(db).SeedAsync(Tenant);

        await new PipelinePlatformContentSeeder(db).SeedAsync();

        Command raidCommand = db.Commands.Single(c => c.NameNormalized == "raid");
        Pipeline pipeline = db.Pipelines.Single(p => p.Id == raidCommand.PipelineId);
        PlatformContentDefinition definition = db.PlatformContentDefinitions.Single(d =>
            d.Kind == PlatformContentKinds.Pipeline && d.Key == "raid_out"
        );

        pipeline.PlatformSourceDefinitionId.Should().Be(definition.Id);
        pipeline.PlatformSourceVersion.Should().Be(1);
        pipeline.PlatformSourceHash.Should().NotBeNull();
        pipeline.PlatformSourceSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Backfills_provenance_onto_the_tenants_raid_start_and_raid_commit_event_response_pipelines()
    {
        SeedTestDbContext db = BuildDb();
        // RaidStartFlowSeeder/RaidCommitFlowSeeder only wire up an EXISTING untouched EventResponse stub
        // (never create the row themselves) — EventResponseDefaultsSeeder (Order 81) is what normally
        // guarantees that stub exists before they run on a real boot.
        await new EventResponseDefaultsSeeder(db).SeedAsync(Tenant);
        await new RaidStartFlowSeeder(db).SeedAsync(Tenant);
        await new RaidCommitFlowSeeder(db).SeedAsync(Tenant);

        await new PipelinePlatformContentSeeder(db).SeedAsync();

        EventResponse startResponse = db.EventResponses.Single(r =>
            r.EventType == "channel.raid.start"
        );
        EventResponse commitResponse = db.EventResponses.Single(r =>
            r.EventType == "channel.raid.out"
        );

        Pipeline startPipeline = db.Pipelines.Single(p => p.Id == startResponse.PipelineId);
        Pipeline commitPipeline = db.Pipelines.Single(p => p.Id == commitResponse.PipelineId);

        PlatformContentDefinition startDefinition = db.PlatformContentDefinitions.Single(d =>
            d.Kind == PlatformContentKinds.Pipeline && d.Key == "raid_start"
        );
        PlatformContentDefinition commitDefinition = db.PlatformContentDefinitions.Single(d =>
            d.Kind == PlatformContentKinds.Pipeline && d.Key == "raid_commit"
        );

        startPipeline.PlatformSourceDefinitionId.Should().Be(startDefinition.Id);
        commitPipeline.PlatformSourceDefinitionId.Should().Be(commitDefinition.Id);
    }

    [Fact]
    public async Task Never_overwrites_a_pipeline_already_stamped_by_a_different_definition()
    {
        SeedTestDbContext db = BuildDb();
        await new RaidFlowSeeder(db).SeedAsync(Tenant);

        Command raidCommand = db.Commands.Single(c => c.NameNormalized == "raid");
        Pipeline pipeline = db.Pipelines.Single(p => p.Id == raidCommand.PipelineId);
        Guid alreadyStampedId = Guid.NewGuid();
        pipeline.PlatformSourceDefinitionId = alreadyStampedId;
        pipeline.PlatformSourceVersion = 7;
        pipeline.PlatformSourceHash = "pre-existing-hash";
        db.SaveChanges();

        await new PipelinePlatformContentSeeder(db).SeedAsync();

        Pipeline after = db.Pipelines.Single(p => p.Id == pipeline.Id);
        after.PlatformSourceDefinitionId.Should().Be(alreadyStampedId);
        after.PlatformSourceVersion.Should().Be(7);
        after.PlatformSourceHash.Should().Be("pre-existing-hash");
    }

    [Fact]
    public async Task Is_idempotent_across_repeated_boots()
    {
        SeedTestDbContext db = BuildDb();
        await new RaidFlowSeeder(db).SeedAsync(Tenant);

        PipelinePlatformContentSeeder seeder = new(db);
        await seeder.SeedAsync();
        int definitionCountAfterFirstRun = db.PlatformContentDefinitions.Count(d =>
            d.Kind == PlatformContentKinds.Pipeline
        );

        await seeder.SeedAsync();
        int definitionCountAfterSecondRun = db.PlatformContentDefinitions.Count(d =>
            d.Kind == PlatformContentKinds.Pipeline
        );

        definitionCountAfterSecondRun.Should().Be(definitionCountAfterFirstRun);
        definitionCountAfterFirstRun.Should().Be(3);
    }
}
