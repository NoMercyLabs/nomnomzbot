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
using Microsoft.EntityFrameworkCore.Diagnostics;
using NomNomzBot.Application.Commands.Import;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Tests.Content;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A channel full of named commands whose pipelines had ZERO steps — they matched, they ran, and they said
/// nothing — is what this importer exists to repair. It must produce an ORDINARY pipeline of generic blocks
/// (if → pick_from_list → send_message), because the whole requirement is that the streamer can then open it
/// in the editor and rearrange it, not that a behaviour is hard-coded somewhere.
/// </summary>
public sealed class CommandFlowImporterTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-4444-7000-8000-000000000001");

    private static (CommandFlowImporter Importer, SeedTestDbContext Db) Build()
    {
        SeedTestDbContext db = new(
            new DbContextOptionsBuilder<SeedTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options
        );
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "flow-test-channel",
                NameNormalized = "flow-test-channel",
            }
        );
        db.SaveChanges();
        return (new CommandFlowImporter(db), db);
    }

    private static CommandFlowSpec HugSpec() =>
        new(
            Command: "hug",
            Description: "Hug someone.",
            Pools: new Dictionary<string, IReadOnlyList<string>>
            {
                ["no_target"] = ["{user} hugs the void. Try !hug @someone"],
                ["self"] = ["{user} hugs themselves. Tragic."],
                ["target"] = ["{user} bear-hugs {target}.", "{user} squeezes {target}."],
            },
            Branches:
            [
                new(new("{args.1}", "eq", ""), "no_target"),
                new(new("{args.1}", "eq", "{user.name}"), "self"),
                new(null, "target"),
            ]
        );

    private static List<PipelineStep> Steps(SeedTestDbContext db) =>
        db.PipelineSteps.OrderBy(s => s.Order).ToList();

    [Fact]
    public async Task An_empty_command_is_filled_with_generic_blocks_only()
    {
        (CommandFlowImporter importer, SeedTestDbContext db) = Build();
        Pipeline stub = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Tenant,
            Name = "Hug",
            TriggerKind = "command",
            IsEnabled = true,
        };
        db.Pipelines.Add(stub);
        db.Commands.Add(
            new Command
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                Name = "hug",
                NameNormalized = "hug",
                Tier = "pipeline",
                PipelineId = stub.Id,
                IsEnabled = true,
            }
        );
        db.SaveChanges();

        Result<CommandFlowImportReport> result = await importer.ImportAsync(Tenant, [HugSpec()]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Filled.Should().Contain("hug");

        List<PipelineStep> steps = Steps(db);
        steps.Should().NotBeEmpty("the command answered nothing before this");
        // Only blocks that already exist in the editor's palette — nothing bespoke.
        steps
            .Select(s => s.BlockKind ?? s.ActionType)
            .Should()
            .OnlyContain(kind =>
                kind == "if" || kind == "pick_from_list" || kind == "send_message"
            );
        // It fills the pipeline the command already points at, rather than orphaning it.
        steps.Should().OnlyContain(s => s.PipelineId == stub.Id);
    }

    [Fact]
    public async Task Every_branch_answers_from_its_own_pool_and_the_fallback_is_reachable()
    {
        (CommandFlowImporter importer, SeedTestDbContext db) = Build();

        await importer.ImportAsync(Tenant, [HugSpec()]);

        List<PipelineStep> steps = Steps(db);
        List<PipelineStep> ifs = [.. steps.Where(s => s.BlockKind == "if")];
        ifs.Should().HaveCount(2, "two conditional rules, then the fallback");

        // else-if nesting: the second rule lives in the first's else lane, so the rules are tried in order.
        ifs[1].ParentStepId.Should().Be(ifs[0].Id);
        ifs[1].Branch.Should().Be("else");

        // The fallback sits in the innermost else — without it a caller could match nothing and get silence.
        List<PipelineStep> fallback =
        [
            .. steps.Where(s => s.ParentStepId == ifs[1].Id && s.Branch == "else"),
        ];
        fallback.Should().HaveCount(2);
        fallback[0].ConfigJson.Should().Contain("hug.target");
        fallback[1].ConfigJson.Should().Contain("{{pick}}");

        // Each conditional arm draws from its OWN pool — the point of the branching.
        steps
            .Single(s =>
                s is { Branch: "then", ActionType: "pick_from_list" } && s.ParentStepId == ifs[0].Id
            )
            .ConfigJson.Should()
            .Contain("hug.no_target");
        steps
            .Single(s =>
                s is { Branch: "then", ActionType: "pick_from_list" } && s.ParentStepId == ifs[1].Id
            )
            .ConfigJson.Should()
            .Contain("hug.self");
    }

    [Fact]
    public async Task Each_pool_becomes_a_pick_list_the_streamer_can_edit()
    {
        (CommandFlowImporter importer, SeedTestDbContext db) = Build();

        await importer.ImportAsync(Tenant, [HugSpec()]);

        List<PickList> lists = db.PickLists.ToList();
        lists
            .Select(l => l.Name)
            .Should()
            .BeEquivalentTo(["hug.no_target", "hug.self", "hug.target"]);
        lists.Single(l => l.Name == "hug.target").Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_conditions_are_real_comparison_conditions_on_the_if_blocks()
    {
        (CommandFlowImporter importer, SeedTestDbContext db) = Build();

        await importer.ImportAsync(Tenant, [HugSpec()]);

        List<PipelineStep> ifs = [.. Steps(db).Where(s => s.BlockKind == "if")];
        List<PipelineStepCondition> conditions = db.PipelineStepConditions.ToList();

        conditions.Should().HaveCount(2);
        PipelineStepCondition first = conditions.Single(c => c.PipelineStepId == ifs[0].Id);
        first.ConditionType.Should().Be("comparison");
        first.LeftOperand.Should().Be("{args.1}");
        first.Operator.Should().Be("eq");
        // "no argument given" is an empty right-hand side, not a magic sentinel.
        first.RightOperand.Should().BeEmpty();
    }

    [Fact]
    public async Task A_command_the_streamer_already_built_is_never_rewritten()
    {
        (CommandFlowImporter importer, SeedTestDbContext db) = Build();
        Pipeline mine = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Tenant,
            Name = "My hug",
            TriggerKind = "command",
            IsEnabled = true,
        };
        db.Pipelines.Add(mine);
        db.PipelineSteps.Add(
            new PipelineStep
            {
                Id = Guid.CreateVersion7(),
                PipelineId = mine.Id,
                BroadcasterId = Tenant,
                ActionType = "send_message",
                ConfigJson = """{"message":"my own hug"}""",
                Order = 0,
                IsEnabled = true,
            }
        );
        db.Commands.Add(
            new Command
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                Name = "hug",
                NameNormalized = "hug",
                Tier = "pipeline",
                PipelineId = mine.Id,
                IsEnabled = true,
            }
        );
        db.SaveChanges();

        Result<CommandFlowImportReport> result = await importer.ImportAsync(Tenant, [HugSpec()]);

        result.Value.Skipped.Should().Contain("hug");
        Steps(db).Should().HaveCount(1);
        Steps(db)[0].ConfigJson.Should().Contain("my own hug");
        db.PickLists.Should().BeEmpty("a skipped command must not leave stray lists behind");
    }

    [Fact]
    public async Task A_channel_with_no_such_command_gets_one()
    {
        (CommandFlowImporter importer, SeedTestDbContext db) = Build();

        await importer.ImportAsync(Tenant, [HugSpec()]);

        Command created = db.Commands.Single(c => c.NameNormalized == "hug");
        created.Tier.Should().Be("pipeline");
        created.PipelineId.Should().NotBeNull();
        Steps(db).Should().OnlyContain(s => s.PipelineId == created.PipelineId!.Value);
    }

    [Fact]
    public async Task A_spec_whose_last_branch_is_conditional_is_refused_rather_than_shipping_a_silent_command()
    {
        (CommandFlowImporter importer, SeedTestDbContext db) = Build();
        CommandFlowSpec noFallback = HugSpec() with
        {
            Branches = [new(new("{args.1}", "eq", ""), "no_target")],
        };

        Result<CommandFlowImportReport> result = await importer.ImportAsync(Tenant, [noFallback]);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NO_FALLBACK");
        Steps(db).Should().BeEmpty("a refused import must not half-write a pipeline");
    }

    [Fact]
    public async Task A_branch_pointing_at_an_empty_pool_is_refused()
    {
        (CommandFlowImporter importer, SeedTestDbContext db) = Build();
        CommandFlowSpec emptyPool = HugSpec() with
        {
            Pools = new Dictionary<string, IReadOnlyList<string>>
            {
                ["no_target"] = [],
                ["self"] = ["x"],
                ["target"] = ["y"],
            },
        };

        Result<CommandFlowImportReport> result = await importer.ImportAsync(Tenant, [emptyPool]);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("EMPTY_POOL");
    }
}
