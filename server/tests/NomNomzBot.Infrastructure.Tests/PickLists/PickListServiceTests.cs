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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.PickLists;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.PickLists;

/// <summary>
/// Behavior tests for the generic pick-list service. Each proves a consequence of an action — the row that
/// lands with its items, the event emitted, the soft-delete that hides-but-retains, the random pick that is
/// genuinely a member of the list — not merely that a call returned non-null. Runs on real SQLite so the
/// filtered unique <c>(BroadcasterId, Name)</c> index and the JSON <c>Items</c> column are actually exercised.
/// </summary>
public sealed class PickListServiceTests
{
    private static PickListService NewService(PickListTestDbContext db, RecordingEventBus bus) =>
        new(db, bus);

    private static async Task<Guid> SeedChannelAsync(PickListSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
        await using PickListTestDbContext db = database.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    [Fact]
    public async Task CreateAsync_PersistsListWithItems_AndPublishesCreatedEvent()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        PickListDto created;
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            Result<PickListDto> result = await service.CreateAsync(
                channel,
                new CreatePickListRequest(
                    "fight_moves",
                    "Fight lines",
                    ["{user} bonks {target}", "swings wildly"]
                )
            );

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            created = result.Value;
        }

        // The DTO carries the full shape, items in order.
        created.Name.Should().Be("fight_moves");
        created.Description.Should().Be("Fight lines");
        created.Items.Should().Equal("{user} bonks {target}", "swings wildly");

        // The persisted row carries the items as stored data, not just an id.
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickList stored = await db
                .PickLists.IgnoreQueryFilters()
                .SingleAsync(p => p.BroadcasterId == channel);
            stored.Id.Should().Be(created.Id);
            stored.Name.Should().Be("fight_moves");
            stored.Description.Should().Be("Fight lines");
            stored.Items.Should().Equal("{user} bonks {target}", "swings wildly");
            stored.DeletedAt.Should().BeNull();
        }

        // A ChannelConfigChangedEvent fired for the picklists domain so the dashboard live-syncs.
        ChannelConfigChangedEvent published = bus
            .Published.OfType<ChannelConfigChangedEvent>()
            .Single();
        published.Domain.Should().Be("picklists");
        published.Action.Should().Be("created");
        published.EntityId.Should().Be(created.Id.ToString());
        published.BroadcasterId.Should().Be(channel);
    }

    [Fact]
    public async Task CreateAsync_DuplicateActiveName_FailsAlreadyExists()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            await service.CreateAsync(
                channel,
                new CreatePickListRequest("greetings", null, ["hi"])
            );
        }

        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            Result<PickListDto> duplicate = await service.CreateAsync(
                channel,
                new CreatePickListRequest("greetings", null, ["yo"])
            );

            duplicate.IsFailure.Should().BeTrue();
            duplicate.ErrorCode.Should().Be("ALREADY_EXISTS");
        }

        // The original list is untouched — the duplicate never overwrote it.
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickList stored = await db
                .PickLists.IgnoreQueryFilters()
                .SingleAsync(p => p.BroadcasterId == channel && p.Name == "greetings");
            stored.Items.Should().Equal("hi");
        }
    }

    [Fact]
    public async Task CreateAsync_RevivesSoftDeletedNamesake_InPlace()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        Guid originalId;
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            Result<PickListDto> first = await service.CreateAsync(
                channel,
                new CreatePickListRequest("seasonal", "v1", ["old"])
            );
            originalId = first.Value.Id;
            await service.DeleteAsync(channel, originalId);
        }

        PickListDto revived;
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            Result<PickListDto> second = await service.CreateAsync(
                channel,
                new CreatePickListRequest("seasonal", "v2", ["fresh", "new"])
            );
            second.IsSuccess.Should().BeTrue(second.ErrorMessage);
            revived = second.Value;
        }

        // The soft-deleted namesake was revived in place: same id, new content, and exactly ONE row survives.
        revived.Id.Should().Be(originalId);
        revived.Description.Should().Be("v2");
        revived.Items.Should().Equal("fresh", "new");

        await using (PickListTestDbContext db = database.NewContext())
        {
            List<PickList> rows = await db
                .PickLists.IgnoreQueryFilters()
                .Where(p => p.BroadcasterId == channel && p.Name == "seasonal")
                .ToListAsync();
            rows.Should().ContainSingle();
            rows[0].DeletedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task UpdateAsync_ReplacesNameDescriptionAndItems()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        Guid id;
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            id = (
                await service.CreateAsync(channel, new CreatePickListRequest("orig", "old", ["a"]))
            )
                .Value
                .Id;
        }

        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            Result<PickListDto> updated = await service.UpdateAsync(
                channel,
                id,
                new UpdatePickListRequest("renamed", "new", ["b", "c"])
            );
            updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
            updated.Value.Name.Should().Be("renamed");
            updated.Value.Items.Should().Equal("b", "c");
        }

        await using (PickListTestDbContext db = database.NewContext())
        {
            PickList stored = await db.PickLists.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
            stored.Name.Should().Be("renamed");
            stored.Description.Should().Be("new");
            stored.Items.Should().Equal("b", "c");
        }
    }

    [Fact]
    public async Task UpdateAsync_RenameToAnExistingName_FailsAlreadyExists()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        Guid idA;
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            idA = (
                await service.CreateAsync(channel, new CreatePickListRequest("aaa", null, ["1"]))
            )
                .Value
                .Id;
            await service.CreateAsync(channel, new CreatePickListRequest("bbb", null, ["2"]));
        }

        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            Result<PickListDto> clash = await service.UpdateAsync(
                channel,
                idA,
                new UpdatePickListRequest("bbb", null, ["1"])
            );
            clash.IsFailure.Should().BeTrue();
            clash.ErrorCode.Should().Be("ALREADY_EXISTS");
        }

        // The rename was rejected — "aaa" still exists and "bbb" is unchanged.
        await using (PickListTestDbContext db = database.NewContext())
        {
            (await db.PickLists.IgnoreQueryFilters().AnyAsync(p => p.Id == idA && p.Name == "aaa"))
                .Should()
                .BeTrue();
        }
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes_HiddenFromReadsButRetainedInTheDatabase()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        Guid id;
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            id = (
                await service.CreateAsync(channel, new CreatePickListRequest("temp", null, ["x"]))
            )
                .Value
                .Id;
        }

        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            Result deleted = await service.DeleteAsync(channel, id);
            deleted.IsSuccess.Should().BeTrue(deleted.ErrorMessage);
        }

        // The service no longer returns it...
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            Result<PickListDto> gone = await service.GetAsync(channel, id);
            gone.IsFailure.Should().BeTrue();
            gone.ErrorCode.Should().Be("NOT_FOUND");
        }

        // ...but the row is retained with DeletedAt stamped (a true soft-delete, not a physical delete).
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickList row = await db.PickLists.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
            row.DeletedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task GetByNameAsync_ReturnsTheMatchingList()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            await service.CreateAsync(channel, new CreatePickListRequest("alpha", null, ["a1"]));
            await service.CreateAsync(
                channel,
                new CreatePickListRequest("beta", null, ["b1", "b2"])
            );
        }

        await using PickListTestDbContext readDb = database.NewContext();
        PickListService reader = NewService(readDb, bus);

        Result<PickListDto> result = await reader.GetByNameAsync(channel, "beta");
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Name.Should().Be("beta");
        result.Value.Items.Should().Equal("b1", "b2");
    }

    [Fact]
    public async Task PickRandomAsync_ReturnsAnEntryThatIsAMemberOfTheList()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        string[] entries = ["punch", "kick", "headbutt"];
        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            await service.CreateAsync(
                channel,
                new CreatePickListRequest("moves", null, [.. entries])
            );
        }

        await using PickListTestDbContext readDb = database.NewContext();
        PickListService reader = NewService(readDb, bus);

        // Every one of many draws is a genuine member of the list — never an empty or foreign value.
        for (int i = 0; i < 30; i++)
        {
            Result<string> pick = await reader.PickRandomAsync(channel, "moves");
            pick.IsSuccess.Should().BeTrue(pick.ErrorMessage);
            entries.Should().Contain(pick.Value);
        }
    }

    [Fact]
    public async Task PickRandomAsync_MissingList_FailsNotFound()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using PickListTestDbContext db = database.NewContext();
        PickListService service = NewService(db, bus);

        Result<string> pick = await service.PickRandomAsync(channel, "does_not_exist");

        pick.IsFailure.Should().BeTrue();
        pick.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task PickRandomAsync_EmptyList_FailsPicklistEmpty()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            await service.CreateAsync(channel, new CreatePickListRequest("empty", null, []));
        }

        await using PickListTestDbContext readDb = database.NewContext();
        PickListService reader = NewService(readDb, bus);

        Result<string> pick = await reader.PickRandomAsync(channel, "empty");

        pick.IsFailure.Should().BeTrue();
        pick.ErrorCode.Should().Be("PICKLIST_EMPTY");
    }

    [Fact]
    public async Task CreateAsync_NameWithIllegalCharacters_FailsValidation()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using PickListTestDbContext db = database.NewContext();
        PickListService service = NewService(db, bus);

        // A name is used verbatim as the {list.pick.<name>} key, so spaces/braces are rejected.
        Result<PickListDto> bad = await service.CreateAsync(
            channel,
            new CreatePickListRequest("has spaces!", null, ["x"])
        );

        bad.IsFailure.Should().BeTrue();
        bad.ErrorCode.Should().Be("VALIDATION_FAILED");

        // Nothing was persisted for the rejected name.
        (await db.PickLists.IgnoreQueryFilters().AnyAsync(p => p.BroadcasterId == channel))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task Lists_WithTheSameName_CoexistAcrossChannels_AndPickIsTenantScoped()
    {
        using PickListSqliteTestDatabase database = PickListSqliteTestDatabase.Open();
        Guid channelA = await SeedChannelAsync(database);
        Guid channelB = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using (PickListTestDbContext db = database.NewContext())
        {
            PickListService service = NewService(db, bus);
            await service.CreateAsync(
                channelA,
                new CreatePickListRequest("shared", null, ["a-one"])
            );
            await service.CreateAsync(
                channelB,
                new CreatePickListRequest("shared", null, ["b-one"])
            );
        }

        await using PickListTestDbContext readDb = database.NewContext();
        PickListService reader = NewService(readDb, bus);

        // The filtered unique index is per-broadcaster, so the same name lives on both channels, and each
        // channel only ever draws its own entry.
        Result<string> a = await reader.PickRandomAsync(channelA, "shared");
        Result<string> b = await reader.PickRandomAsync(channelB, "shared");
        a.Value.Should().Be("a-one");
        b.Value.Should().Be("b-one");
    }
}
