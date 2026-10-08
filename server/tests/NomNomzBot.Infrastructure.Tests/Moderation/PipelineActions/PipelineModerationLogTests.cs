// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.PipelineActions;
using NSubstitute;
using Record = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Moderation.PipelineActions;

/// <summary>
/// S-MOD-PIPELINE-ACTIONS: a pipeline ban/timeout used to call the chat provider and nothing else, so the
/// dashboard's moderation log never saw it. Every test here drives the real <see cref="BanAction"/> /
/// <see cref="TimeoutAction"/> through the real <see cref="PipelineModerationService"/> (and, on a Twitch
/// tenant, the real <see cref="ModerationService"/>) and asserts the PERSISTED <c>moderation_action</c> row, the
/// operator the Twitch call was signed as, and which platform call was routed — never merely "did not throw".
/// </summary>
public sealed class PipelineModerationLogTests
{
    private const string ActionRecordType = "moderation_action";
    private const string BroadcasterTwitchId = "1001";
    private const string ViewerId = "5005";
    private static readonly Guid Tenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8d");
    private static readonly Guid Owner = Guid.Parse("019f2802-5c77-7dc8-b6f6-000000000777");

    private sealed record Harness(
        ModerationServiceTestDbContext Db,
        ITwitchModerationApi Twitch,
        IChatProvider Chat,
        BanAction Ban,
        TimeoutAction Timeout
    );

    private static async Task<Harness> NewHarnessAsync(string provider, Guid? ownerUserId = null)
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Tenant,
                TwitchChannelId = BroadcasterTwitchId,
                OwnerUserId = ownerUserId ?? Owner,
                Name = "c",
                NameNormalized = "c",
                Provider = provider,
            }
        );
        await db.SaveChangesAsync();

        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();
        IChatProvider chat = Substitute.For<IChatProvider>();
        ModerationService moderation = new(
            db,
            twitch,
            Substitute.For<ITwitchModeratorsApi>(),
            Substitute.For<IChannelRegistry>(),
            TimeProvider.System,
            NullLogger<ModerationService>.Instance,
            Substitute.For<IEventBus>()
        );
        PipelineModerationService pipelineModeration = new(db, moderation, chat);
        return new(db, twitch, chat, new(pipelineModeration), new(pipelineModeration));
    }

    private static Result<TwitchBanResult> TwitchAccepted() =>
        Result.Success(
            new TwitchBanResult(
                BroadcasterTwitchId,
                Owner.ToString(),
                ViewerId,
                DateTimeOffset.UtcNow,
                EndTime: null
            )
        );

    private static void TwitchAcceptsBanAndTimeout(ITwitchModerationApi twitch)
    {
        twitch
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(TwitchAccepted());
        twitch
            .TimeoutAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(TwitchAccepted());
    }

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Tenant,
            TriggeredByUserId = "viewer-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "msg-1",
            RawMessage = "spam",
        };

    private static ActionDefinition BanDefinition(string reason) =>
        new()
        {
            Type = "ban",
            Parameters = new()
            {
                ["user_id"] = JsonSerializer.SerializeToElement(ViewerId),
                ["reason"] = JsonSerializer.SerializeToElement(reason),
            },
        };

    private static ActionDefinition TimeoutDefinition(int seconds, string reason) =>
        new()
        {
            Type = "timeout",
            Parameters = new()
            {
                ["user_id"] = JsonSerializer.SerializeToElement(ViewerId),
                ["duration"] = JsonSerializer.SerializeToElement(seconds),
                ["reason"] = JsonSerializer.SerializeToElement(reason),
            },
        };

    private static async Task<List<Record>> ActionRowsAsync(ModerationServiceTestDbContext db) =>
        await db.Records.Where(r => r.RecordType == ActionRecordType).ToListAsync();

    private static void RowShouldDescribe(
        Record row,
        string action,
        string reason,
        int? durationSeconds
    )
    {
        row.BroadcasterId.Should().Be(Tenant);
        // The dashboard attributes an action with no moderator to the tenant (the channel itself).
        row.UserId.Should().Be(Tenant.ToString());

        using JsonDocument data = JsonDocument.Parse(row.Data);
        JsonElement root = data.RootElement;
        root.GetProperty("Action").GetString().Should().Be(action);
        root.GetProperty("TargetUserId").GetString().Should().Be(ViewerId);
        root.GetProperty("Reason").GetString().Should().Be(reason);
        if (durationSeconds is null)
            root.GetProperty("DurationSeconds").ValueKind.Should().Be(JsonValueKind.Null);
        else
            root.GetProperty("DurationSeconds").GetInt32().Should().Be(durationSeconds.Value);
    }

    // ─── Twitch tenant ───────────────────────────────────────────────────────

    [Fact]
    public async Task Ban_OnATwitchTenant_WritesTheModLogRowAndSignsTheCallAsTheChannelOwner()
    {
        Harness h = await NewHarnessAsync(AuthEnums.Platform.Twitch);
        await using ModerationServiceTestDbContext _ = h.Db;
        TwitchAcceptsBanAndTimeout(h.Twitch);

        ActionResult result = await h.Ban.ExecuteAsync(Context(), BanDefinition("link spam"));

        result.Succeeded.Should().BeTrue();
        await h
            .Twitch.Received(1)
            .BanAsOperatorAsync(
                Owner,
                BroadcasterTwitchId,
                ViewerId,
                "link spam",
                Arg.Any<CancellationToken>()
            );
        List<Record> rows = await ActionRowsAsync(h.Db);
        rows.Should().HaveCount(1);
        RowShouldDescribe(rows[0], "ban", "link spam", durationSeconds: null);
    }

    [Fact]
    public async Task Timeout_OnATwitchTenant_WritesTheModLogRowWithTheDurationAndTheOwnerAsOperator()
    {
        Harness h = await NewHarnessAsync(AuthEnums.Platform.Twitch);
        await using ModerationServiceTestDbContext _ = h.Db;
        TwitchAcceptsBanAndTimeout(h.Twitch);

        ActionResult result = await h.Timeout.ExecuteAsync(
            Context(),
            TimeoutDefinition(600, "caps")
        );

        result.Succeeded.Should().BeTrue();
        await h
            .Twitch.Received(1)
            .TimeoutAsOperatorAsync(
                Owner,
                BroadcasterTwitchId,
                ViewerId,
                600,
                "caps",
                Arg.Any<CancellationToken>()
            );
        List<Record> rows = await ActionRowsAsync(h.Db);
        rows.Should().HaveCount(1);
        RowShouldDescribe(rows[0], "timeout", "caps", durationSeconds: 600);
    }

    [Fact]
    public async Task Ban_WhenTwitchRejectsIt_FailsTheActionAndWritesNoRow()
    {
        Harness h = await NewHarnessAsync(AuthEnums.Platform.Twitch);
        await using ModerationServiceTestDbContext _ = h.Db;
        h.Twitch.BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchBanResult>("The user is already banned.", "TWITCH_ERROR")
            );

        ActionResult result = await h.Ban.ExecuteAsync(Context(), BanDefinition("spam"));

        result.Succeeded.Should().BeFalse();
        (await ActionRowsAsync(h.Db)).Should().BeEmpty();
    }

    [Fact]
    public async Task Ban_OnATwitchTenantWithoutAnOwner_FailsWithoutCallingTwitchOrWritingARow()
    {
        Harness h = await NewHarnessAsync(AuthEnums.Platform.Twitch, ownerUserId: Guid.Empty);
        await using ModerationServiceTestDbContext _ = h.Db;
        TwitchAcceptsBanAndTimeout(h.Twitch);

        ActionResult result = await h.Ban.ExecuteAsync(Context(), BanDefinition("spam"));

        result.Succeeded.Should().BeFalse();
        await h
            .Twitch.DidNotReceiveWithAnyArgs()
            .BanAsOperatorAsync(default, default!, default!, default, default);
        (await ActionRowsAsync(h.Db)).Should().BeEmpty();
    }

    // ─── Kick tenant: still routed to the tenant's own platform, still logged ──

    [Fact]
    public async Task Ban_OnAKickTenant_RoutesToTheChatPlatformNotTwitchAndWritesTheModLogRow()
    {
        Harness h = await NewHarnessAsync(AuthEnums.Platform.Kick);
        await using ModerationServiceTestDbContext _ = h.Db;
        h.Chat.BanUserAsync(Tenant, ViewerId, "link spam", Arg.Any<CancellationToken>())
            .Returns(true);

        ActionResult result = await h.Ban.ExecuteAsync(Context(), BanDefinition("link spam"));

        result.Succeeded.Should().BeTrue();
        await h
            .Chat.Received(1)
            .BanUserAsync(Tenant, ViewerId, "link spam", Arg.Any<CancellationToken>());
        await h
            .Twitch.DidNotReceiveWithAnyArgs()
            .BanAsOperatorAsync(default, default!, default!, default, default);
        List<Record> rows = await ActionRowsAsync(h.Db);
        rows.Should().HaveCount(1);
        RowShouldDescribe(rows[0], "ban", "link spam", durationSeconds: null);
    }

    [Fact]
    public async Task Timeout_OnAKickTenant_RoutesToTheChatPlatformAndWritesTheModLogRowWithTheDuration()
    {
        Harness h = await NewHarnessAsync(AuthEnums.Platform.Kick);
        await using ModerationServiceTestDbContext _ = h.Db;
        h.Chat.TimeoutUserAsync(Tenant, ViewerId, 90, "caps", Arg.Any<CancellationToken>())
            .Returns(true);

        ActionResult result = await h.Timeout.ExecuteAsync(
            Context(),
            TimeoutDefinition(90, "caps")
        );

        result.Succeeded.Should().BeTrue();
        await h
            .Chat.Received(1)
            .TimeoutUserAsync(Tenant, ViewerId, 90, "caps", Arg.Any<CancellationToken>());
        await h
            .Twitch.DidNotReceiveWithAnyArgs()
            .TimeoutAsOperatorAsync(default, default!, default!, default, default, default);
        List<Record> rows = await ActionRowsAsync(h.Db);
        rows.Should().HaveCount(1);
        RowShouldDescribe(rows[0], "timeout", "caps", durationSeconds: 90);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Action_WhenTheKickPlatformRefusesIt_FailsAndWritesNoSucceededRow(bool isBan)
    {
        Harness h = await NewHarnessAsync(AuthEnums.Platform.Kick);
        await using ModerationServiceTestDbContext _ = h.Db;
        // NSubstitute's default for Task<bool> is a completed false — the platform's "could not apply" answer.

        ActionResult result = isBan
            ? await h.Ban.ExecuteAsync(Context(), BanDefinition("spam"))
            : await h.Timeout.ExecuteAsync(Context(), TimeoutDefinition(60, "spam"));

        result.Succeeded.Should().BeFalse();
        (await ActionRowsAsync(h.Db)).Should().BeEmpty();
    }
}
