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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using RecordEntity = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// A network block stays true after it is applied: a channel onboarded later bans the actor at onboarding,
/// a channel where the actor first chats later bans them at chat ingest, every late leg is reversed by the
/// lift, and a channel that already existed at apply is never widened into by the onboarding backfill.
/// </summary>
public sealed class NetworkBlockEnforcementTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("0199e000-0000-7000-8000-0000000001a1");
    private static readonly Guid LateTenant = Guid.Parse("0199e000-0000-7000-8000-0000000001f1");
    private static readonly Guid OldQuietTenant = Guid.Parse(
        "0199e000-0000-7000-8000-0000000001f2"
    );
    private static readonly Guid OperatorId = Guid.Parse("0199e000-0000-7000-8000-000000000d11");
    private static readonly Guid TargetUserId = Guid.Parse("0199e000-0000-7000-8000-000000000e11");
    private const string TargetTwitchId = "troll-77";
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Why = "Ticket #9002 — cross-channel harassment.";

    private readonly SqliteConnection _connection;
    private readonly ITwitchModerationApi _twitch = Substitute.For<ITwitchModerationApi>();

    public NetworkBlockEnforcementTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

        // Tenant A holds the actor's standing at apply; OldQuietTenant existed at apply but the actor never
        // showed up there, so the apply fan-out never reached it.
        AddChannel(db, TenantA, "channel-a", Now.UtcDateTime.AddDays(-30));
        AddChannel(db, OldQuietTenant, "channel-quiet", Now.UtcDateTime.AddDays(-10));

        db.Users.Add(
            new User
            {
                Id = TargetUserId,
                TwitchUserId = TargetTwitchId,
                Username = "Troll77",
                UsernameNormalized = "troll77",
                DisplayName = "Troll77",
            }
        );
        db.ChannelCommunityStandings.Add(
            new ChannelCommunityStanding
            {
                BroadcasterId = TenantA,
                UserId = TargetUserId,
                Standing = CommunityStanding.Everyone,
            }
        );
        SeedOperator(db);
        db.SaveChanges();

        _twitch
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchBanResult("b", "b", TargetTwitchId, DateTimeOffset.UnixEpoch, null)
                )
            );
        _twitch
            .UnbanUserAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static void AddChannel(AppDbContext db, Guid id, string name, DateTime createdAt) =>
        db.Channels.Add(
            new Channel
            {
                Id = id,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = $"ext-{name}",
                Name = name,
                NameNormalized = name,
                CreatedAt = createdAt,
            }
        );

    private static void SeedOperator(AppDbContext db)
    {
        Guid roleId = Guid.NewGuid();
        Guid permissionId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
            {
                Id = OperatorId,
                PrincipalType = IamPrincipalType.Employee,
                Name = "operator",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = $"role-{roleId}" });
        db.IamPermissions.Add(
            new()
            {
                Id = permissionId,
                Key = IamPermissionKeys.NetworkBlockManage,
                Category = IamCategory.Tenant,
            }
        );
        db.IamRolePermissions.Add(new() { RoleId = roleId, PermissionId = permissionId });
        db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = OperatorId,
                RoleId = roleId,
                AssignedByPrincipalId = OperatorId,
            }
        );
    }

    private NetworkBlockService NewBlockDesk(AppDbContext db) =>
        new(
            db,
            new PlatformIamService(
                db,
                new RecordingEventBus(),
                TimeProvider.System,
                new(DeploymentMode.Saas)
            ),
            _twitch,
            new FakeTimeProvider(Now),
            NullLogger<NetworkBlockService>.Instance
        );

    private NetworkBlockEnforcementService NewEnforcer(AppDbContext db) =>
        new(db, _twitch, NullLogger<NetworkBlockEnforcementService>.Instance);

    private async Task<NetworkBlockDto> ApplyBlockAsync()
    {
        using AppDbContext db = NewDbContext();
        Result<NetworkBlockDto> applied = await NewBlockDesk(db)
            .ApplyAsync(OperatorId, new ApplyNetworkBlockRequest(TargetTwitchId, "raid", Why, 1));
        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        _twitch.ClearReceivedCalls();
        return applied.Value;
    }

    private void OnboardLateTenant()
    {
        using AppDbContext db = NewDbContext();
        AddChannel(db, LateTenant, "channel-late", Now.UtcDateTime.AddHours(2));
        db.SaveChanges();
    }

    private async Task<List<Guid>> LegChannelsAsync(Guid blockId)
    {
        using AppDbContext db = NewDbContext();
        string marker = blockId.ToString();
        List<RecordEntity> legs = await db
            .Records.IgnoreQueryFilters()
            .Where(r => r.RecordType == "moderation_action" && r.Data.Contains(marker))
            .ToListAsync();
        return legs.Select(r => r.BroadcasterId).ToList();
    }

    [Fact]
    public async Task ChannelOnboardedAfterTheBlock_BansTheActorThere_AndTheLiftReversesThatLegToo()
    {
        NetworkBlockDto block = await ApplyBlockAsync();
        OnboardLateTenant();

        using (AppDbContext db = NewDbContext())
        {
            Result<int> enforced = await NewEnforcer(db)
                .EnforceForOnboardedChannelAsync(LateTenant);
            enforced.Value.Should().Be(1);
        }

        await _twitch
            .Received(1)
            .BanUserAsync(LateTenant, TargetTwitchId, "raid", Arg.Any<CancellationToken>());
        (await LegChannelsAsync(block.Id)).Should().BeEquivalentTo([TenantA, LateTenant]);

        using (AppDbContext db = NewDbContext())
        {
            NetworkBlock row = await db.NetworkBlocks.SingleAsync(b => b.Id == block.Id);
            row.ChannelCount.Should().Be(2);

            Result<NetworkBlockDto> lifted = await NewBlockDesk(db)
                .LiftAsync(OperatorId, block.Id, "Appeal upheld.");
            lifted.Value.Status.Should().Be(NetworkBlockStatus.Lifted);
        }

        await _twitch
            .Received(1)
            .UnbanUserAsync(LateTenant, TargetTwitchId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnboardingBackfill_ForAChannelThatExistedAtApply_DoesNotWidenTheBlock()
    {
        NetworkBlockDto block = await ApplyBlockAsync();

        using AppDbContext db = NewDbContext();
        Result<int> enforced = await NewEnforcer(db)
            .EnforceForOnboardedChannelAsync(OldQuietTenant);

        enforced.Value.Should().Be(0);
        await _twitch
            .DidNotReceive()
            .BanUserAsync(
                OldQuietTenant,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        (await LegChannelsAsync(block.Id)).Should().BeEquivalentTo([TenantA]);
    }

    [Fact]
    public async Task BlockedActorChatting_InAChannelTheApplyNeverReached_IsBannedOnce_AtChatIngest()
    {
        NetworkBlockDto block = await ApplyBlockAsync();
        NetworkBlockChatEnforcementHandler handler = NewChatHandler();

        await handler.HandleAsync(ChatLine(OldQuietTenant, TargetTwitchId));
        await handler.HandleAsync(ChatLine(OldQuietTenant, TargetTwitchId));

        await _twitch
            .Received(1)
            .BanUserAsync(OldQuietTenant, TargetTwitchId, "raid", Arg.Any<CancellationToken>());
        (await LegChannelsAsync(block.Id)).Should().BeEquivalentTo([TenantA, OldQuietTenant]);
    }

    [Fact]
    public async Task AnUnblockedChatter_IsLeftAlone_AtChatIngest()
    {
        await ApplyBlockAsync();
        NetworkBlockChatEnforcementHandler handler = NewChatHandler();

        await handler.HandleAsync(ChatLine(OldQuietTenant, "regular-viewer"));

        await _twitch
            .DidNotReceive()
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OnceALiftWasAttempted_NoNewLegIsAdded_EvenWhileAPartialLiftKeepsTheDeny()
    {
        NetworkBlockDto block = await ApplyBlockAsync();
        _twitch
            .UnbanUserAsync(TenantA, TargetTwitchId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("token revoked", "UNAUTHORIZED"));
        using (AppDbContext db = NewDbContext())
        {
            Result<NetworkBlockDto> lifted = await NewBlockDesk(db)
                .LiftAsync(OperatorId, block.Id, "Appeal upheld.");
            lifted.Value.Status.Should().Be(NetworkBlockStatus.Partial);
        }

        await NewChatHandler().HandleAsync(ChatLine(OldQuietTenant, TargetTwitchId));

        await _twitch
            .DidNotReceive()
            .BanUserAsync(
                OldQuietTenant,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    private NetworkBlockChatEnforcementHandler NewChatHandler()
    {
        ServiceCollection services = new();
        services.AddScoped<IApplicationDbContext>(_ => NewDbContext());
        services.AddSingleton(_twitch);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<INetworkBlockEnforcementService, NetworkBlockEnforcementService>();
        ServiceProvider provider = services.BuildServiceProvider();
        return new NetworkBlockChatEnforcementHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NetworkBlockChatEnforcementHandler>.Instance
        );
    }

    private static ChatMessageReceivedEvent ChatLine(Guid channelId, string twitchUserId) =>
        new()
        {
            BroadcasterId = channelId,
            MessageId = Guid.NewGuid().ToString(),
            TwitchBroadcasterId = $"ext-{channelId}",
            UserId = twitchUserId,
            UserDisplayName = twitchUserId,
            UserLogin = twitchUserId,
            Message = "hello",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };
}
