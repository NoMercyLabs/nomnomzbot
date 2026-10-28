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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The shared blast-radius guard: a command (custom or built-in) that any binding surface still runs
/// cannot be deleted or disabled, and the refusal carries the full usage list.
/// </summary>
public sealed class CommandBindingGuardTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e07");
    private const string BuiltinKey = "lurk";

    /// <summary>A surface source that reports a fixed list for any command, and records what it was asked.</summary>
    private sealed class FakeBindingUsageSource : IBindingUsageSource
    {
        private readonly List<BindingUsage> _usages;

        public FakeBindingUsageSource(params BindingUsage[] usages)
        {
            _usages = usages.ToList();
        }

        public List<(Guid Broadcaster, string TargetId)> Asked { get; } = [];

        public Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
            Guid broadcasterId,
            string commandTargetId,
            CancellationToken ct = default
        )
        {
            Asked.Add((broadcasterId, commandTargetId));
            return Task.FromResult<IReadOnlyList<BindingUsage>>(_usages);
        }
    }

    private static readonly BindingUsage RewardUsage = new("reward", "r-1", "Hydrate");

    private static CommandsTestDbContext NewDb()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "binding-guard-channel",
                NameNormalized = "binding-guard-channel",
            }
        );
        db.SaveChanges();
        return db;
    }

    private static CommandService BuildCommandService(
        CommandsTestDbContext db,
        FakeBindingUsageSource source
    ) =>
        new(
            db,
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([source])
        );

    private static BuiltinCommandService BuildBuiltinService(
        CommandsTestDbContext db,
        FakeBindingUsageSource source
    )
    {
        IBuiltinCommand command = Substitute.For<IBuiltinCommand>();
        command.BuiltinKey.Returns(BuiltinKey);
        command.IsReserved.Returns(false);
        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();
        catalog.Get(BuiltinKey).Returns(command);
        return new(
            catalog,
            db,
            new RecordingEventBus(),
            Substitute.For<IChannelRegistry>(),
            new CommandBindingGuard([source])
        );
    }

    private static async Task<CommandDto> CreateAsync(CommandService sut) =>
        (
            await sut.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "hydrate", TemplateResponse = "drink water" }
            )
        ).Value;

    [Fact]
    public async Task Delete_WhenASurfaceBindsTheCommand_IsRefusedWithTheListAndTheRowStays()
    {
        CommandsTestDbContext db = NewDb();
        FakeBindingUsageSource source = new(RewardUsage);
        CommandService sut = BuildCommandService(db, source);
        CommandDto created = await CreateAsync(sut);

        Result result = await sut.DeleteAsync(Channel.ToString(), "hydrate", "actor");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("COMMAND_IN_USE");
        CommandInUse data = result.ErrorData.Should().BeOfType<CommandInUse>().Subject;
        data.CommandName.Should().Be("hydrate");
        data.Usages.Should().Equal(RewardUsage);
        source.Asked.Should().ContainSingle().Which.Should().Be((Channel, created.Id.ToString()));
        (await db.Commands.CountAsync(c => c.Id == created.Id)).Should().Be(1);
        (await db.Records.CountAsync(r => r.RecordType == "command_action")).Should().Be(0);
    }

    [Fact]
    public async Task DeleteBlastRadius_CountsEachSurfaceThatRunsTheCommandAndNamesTheRows()
    {
        CommandsTestDbContext db = NewDb();
        CommandService sut = BuildCommandService(
            db,
            new FakeBindingUsageSource(
                RewardUsage,
                new("reward", "r-2", "Stretch"),
                new("timers", "t-1", "Follow reminder"),
                new("something-new", "x-1", "Mystery")
            )
        );
        CommandDto created = await CreateAsync(sut);

        Result<BlastRadiusDto> result = await sut.GetDeleteBlastRadiusAsync(
            Channel.ToString(),
            "hydrate"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsMinimum.Should().BeFalse();
        result
            .Value.Categories.Select(c => (c.CategoryKey, c.Count))
            .Should()
            .BeEquivalentTo([
                (BlastRadiusCategoryKeys.Rewards, 2),
                (BlastRadiusCategoryKeys.Timers, 1),
                (BlastRadiusCategoryKeys.OtherCommandBindings, 1),
            ]);
        result
            .Value.Categories.Single(c => c.CategoryKey == BlastRadiusCategoryKeys.Rewards)
            .Sample.Should()
            .BeEquivalentTo("Hydrate", "Stretch");
        (await db.Commands.CountAsync(c => c.Id == created.Id))
            .Should()
            .Be(1, "reading the blast radius deletes nothing");
    }

    [Fact]
    public async Task DeleteBlastRadius_WhenNothingBindsTheCommand_IsAnEmptyList()
    {
        CommandsTestDbContext db = NewDb();
        CommandService sut = BuildCommandService(db, new FakeBindingUsageSource());
        await CreateAsync(sut);

        Result<BlastRadiusDto> result = await sut.GetDeleteBlastRadiusAsync(
            Channel.ToString(),
            "hydrate"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteBlastRadius_ForAnUnknownCommand_IsNotFound()
    {
        CommandsTestDbContext db = NewDb();
        CommandService sut = BuildCommandService(db, new FakeBindingUsageSource(RewardUsage));

        Result<BlastRadiusDto> result = await sut.GetDeleteBlastRadiusAsync(
            Channel.ToString(),
            "nope"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Delete_WhenNothingBindsTheCommand_RemovesIt()
    {
        CommandsTestDbContext db = NewDb();
        CommandService sut = BuildCommandService(db, new FakeBindingUsageSource());
        CommandDto created = await CreateAsync(sut);

        Result result = await sut.DeleteAsync(Channel.ToString(), "hydrate", "actor");

        result.IsSuccess.Should().BeTrue();
        (await db.Commands.CountAsync(c => c.Id == created.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Disable_WhenASurfaceBindsTheCommand_IsRefusedWithTheListAndTheFlagIsUnchanged()
    {
        CommandsTestDbContext db = NewDb();
        CommandService sut = BuildCommandService(db, new FakeBindingUsageSource(RewardUsage));
        CommandDto created = await CreateAsync(sut);

        Result<CommandDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            "hydrate",
            new UpdateCommandDto { IsEnabled = false }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("COMMAND_IN_USE");
        result.ErrorData.Should().BeOfType<CommandInUse>().Which.Usages.Should().Equal(RewardUsage);
        (await db.Commands.SingleAsync(c => c.Id == created.Id)).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Update_ThatKeepsTheCommandEnabled_IsNotBlockedByABinding()
    {
        CommandsTestDbContext db = NewDb();
        CommandService sut = BuildCommandService(db, new FakeBindingUsageSource(RewardUsage));
        await CreateAsync(sut);

        Result<CommandDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            "hydrate",
            new UpdateCommandDto { Description = "new text", IsEnabled = true }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Description.Should().Be("new text");
    }

    [Fact]
    public async Task Disable_WhenNothingBindsTheCommand_DisablesIt()
    {
        CommandsTestDbContext db = NewDb();
        CommandService sut = BuildCommandService(db, new FakeBindingUsageSource());
        CommandDto created = await CreateAsync(sut);

        Result<CommandDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            "hydrate",
            new UpdateCommandDto { IsEnabled = false }
        );

        result.IsSuccess.Should().BeTrue();
        (await db.Commands.SingleAsync(c => c.Id == created.Id)).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task BuiltinDisable_WhenASurfaceBindsIt_IsRefusedAndNoToggleRowIsWritten()
    {
        CommandsTestDbContext db = NewDb();
        FakeBindingUsageSource source = new(RewardUsage);
        BuiltinCommandService sut = BuildBuiltinService(db, source);

        Result result = await sut.SetEnabledAsync(Channel.ToString(), BuiltinKey, false);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("COMMAND_IN_USE");
        result.ErrorData.Should().BeOfType<CommandInUse>().Which.Usages.Should().Equal(RewardUsage);
        source.Asked.Should().ContainSingle().Which.Should().Be((Channel, BuiltinKey));
        (await db.ChannelBuiltinCommands.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BuiltinEnable_WhenASurfaceBindsIt_Succeeds()
    {
        CommandsTestDbContext db = NewDb();
        BuiltinCommandService sut = BuildBuiltinService(
            db,
            new FakeBindingUsageSource(RewardUsage)
        );

        Result result = await sut.SetEnabledAsync(Channel.ToString(), BuiltinKey, true);

        result.IsSuccess.Should().BeTrue();
        (await db.ChannelBuiltinCommands.SingleAsync()).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Guard_GathersTheUsagesOfEverySource()
    {
        BindingUsage timer = new("timer", "t-1", "Every 10 min");
        CommandBindingGuard guard = new([
            new FakeBindingUsageSource(RewardUsage),
            new FakeBindingUsageSource(timer),
        ]);

        IReadOnlyList<BindingUsage> usages = await guard.FindUsagesAsync(Channel, "any");

        usages.Should().Equal(RewardUsage, timer);
    }
}
