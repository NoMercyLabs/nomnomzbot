// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Consequences;

/// <summary>
/// The channel reset preview and the reset itself read ONE scope (<see cref="ChannelResetScope"/>). These tests
/// run both over real SQL and prove the preview lists exactly the rows the reset then removes, per area, and
/// that nothing outside the scope (another channel, global rows, other tables) is touched.
/// </summary>
public class ChannelResetPreviewTests
{
    private static readonly Guid Channel = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherChannel = Guid.Parse("66666666-6666-6666-6666-666666666666");

    // One key per area, two for chat, plus keys no area claims (the remainder).
    private static readonly string[] OwnKeys =
    [
        "chat.settings",
        "render.chat",
        "tts:config",
        "ban:123",
        "shield.mode",
        "music:config",
        "spotify.premium",
        "raids:scoring-rules",
        "youtube.api_key",
        "feather.holder",
        "something.unknown",
    ];

    private static Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.CreateVersion7(),
            TwitchChannelId = id.ToString(),
            Name = name,
            NameNormalized = name,
            OverlayToken = name + "-token",
        };

    private static Configuration NewConfig(Guid? broadcaster, string key) =>
        new()
        {
            BroadcasterId = broadcaster,
            Key = key,
            Value = "v",
        };

    private static ChannelDeletePreviewService NewService(BlastRadiusTestDbContext db) =>
        new(db, new FakeTimeProvider(DateTimeOffset.UnixEpoch), []);

    private static async Task SeedAsync(BlastRadiusSqliteTestDatabase database)
    {
        await using BlastRadiusTestDbContext db = database.NewContext();
        db.Channels.AddRange(NewChannel(Channel, "mine"), NewChannel(OtherChannel, "theirs"));
        db.Configurations.AddRange(OwnKeys.Select(key => NewConfig(Channel, key)));
        db.Configurations.Add(NewConfig(OtherChannel, "chat.settings"));
        db.Configurations.Add(NewConfig(null, "platform:support_contact_url"));
        db.Commands.Add(
            new Command
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Name = "hug",
                NameNormalized = "hug",
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Preview_CountsEachAreaFromTheChannelsOwnConfigurationRows_AndNothingElse()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await SeedAsync(database);

        await using BlastRadiusTestDbContext read = database.NewContext();
        Result<ChannelResetPreviewDto> result = await NewService(read)
            .PreviewResetAsync(Channel.ToString());

        Assert.True(result.IsSuccess);
        Assert.Equal("mine", result.Value.ChannelName);
        Assert.False(result.Value.BlastRadius.IsMinimum);
        Assert.Equal(
            [
                (BlastRadiusCategoryKeys.ResetChat, 2),
                (BlastRadiusCategoryKeys.ResetTts, 1),
                (BlastRadiusCategoryKeys.ResetModeration, 2),
                (BlastRadiusCategoryKeys.ResetMusic, 2),
                (BlastRadiusCategoryKeys.ResetRaids, 1),
                (BlastRadiusCategoryKeys.ResetCredentials, 1),
                (BlastRadiusCategoryKeys.ResetOther, 2),
            ],
            result.Value.BlastRadius.Categories.Select(c => (c.CategoryKey, c.Count)).ToList()
        );
        Assert.Equal(OwnKeys.Length, result.Value.BlastRadius.TotalReferences);
    }

    [Fact]
    public async Task Reset_RemovesExactlyWhatThePreviewListed_AndLeavesEverythingElse()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await SeedAsync(database);

        Dictionary<string, int> previewed;
        await using (BlastRadiusTestDbContext read = database.NewContext())
        {
            Result<ChannelResetPreviewDto> preview = await NewService(read)
                .PreviewResetAsync(Channel.ToString());
            previewed = preview.Value.BlastRadius.Categories.ToDictionary(
                c => c.CategoryKey,
                c => c.Count
            );
        }

        List<string> removedKeys;
        await using (BlastRadiusTestDbContext write = database.NewContext())
        {
            removedKeys = await ChannelResetScope
                .Rows(write, Channel)
                .Select(c => c.Key)
                .ToListAsync();
            int removed = await ChannelResetScope.RemoveAsync(
                write,
                Channel,
                CancellationToken.None
            );

            Assert.Equal(previewed.Values.Sum(), removed);
        }

        Dictionary<string, int> removedByArea = removedKeys
            .GroupBy(ChannelResetScope.CategoryOf)
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(previewed, removedByArea);

        await using BlastRadiusTestDbContext after = database.NewContext();
        Assert.Empty(
            await after.Configurations.Where(c => c.BroadcasterId == Channel).ToListAsync()
        );
        Assert.Equal(
            ["chat.settings"],
            await after
                .Configurations.Where(c => c.BroadcasterId == OtherChannel)
                .Select(c => c.Key)
                .ToListAsync()
        );
        Assert.Equal(1, await after.Configurations.CountAsync(c => c.BroadcasterId == null));
        Assert.Equal(1, await after.Commands.CountAsync(c => c.BroadcasterId == Channel));
    }

    [Fact]
    public async Task Preview_OfAChannelWithNoConfiguration_IsAnEmptyListNotAFailure()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await using (BlastRadiusTestDbContext db = database.NewContext())
        {
            db.Channels.Add(NewChannel(Channel, "mine"));
            await db.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext read = database.NewContext();
        Result<ChannelResetPreviewDto> result = await NewService(read)
            .PreviewResetAsync(Channel.ToString());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.BlastRadius.Categories);
    }

    [Fact]
    public async Task Preview_OfAnUnknownChannel_FailsInsteadOfReportingZero()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await using BlastRadiusTestDbContext read = database.NewContext();

        Result<ChannelResetPreviewDto> result = await NewService(read)
            .PreviewResetAsync(Guid.NewGuid().ToString());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void EveryReportedArea_IsADeclaredBlastRadiusCategory_AndOtherComesLast()
    {
        Assert.Equal(BlastRadiusCategoryKeys.ResetOther, ChannelResetScope.CategoryOrder[^1]);
        Assert.Equal(
            ChannelResetScope.CategoryOrder.Count,
            ChannelResetScope.CategoryOrder.Distinct().Count()
        );
        Assert.All(
            OwnKeys,
            key =>
                Assert.Contains(ChannelResetScope.CategoryOf(key), ChannelResetScope.CategoryOrder)
        );
    }
}
