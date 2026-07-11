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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Giveaways;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Giveaways;

/// <summary>
/// Proves the prize-code SECRET custody (giveaways.md D6) and the code fulfillment leg: intake seals
/// every code (ciphertext at rest, tenant-bound context — assert no plaintext in the row); list/detail
/// reads are MASKED and never contain the plaintext; the broadcaster reveal is the ONE plaintext path;
/// fulfillment claims a unique previously-available code per winner (delivered on whisper success,
/// assigned + WhisperDelivered=false on failure — never lost), never reuses a code, and flags
/// exhaustion by leaving the winner un-coded.
/// </summary>
public sealed class GiveawayCodeCustodyTests
{
    private static readonly Guid Tenant = Guid.Parse("0193b000-0000-7000-8000-0000000000a1");

    /// <summary>Reversible fake protector: "enc({subject}):{plain}" — context-bound like the real one,
    /// so a wrong-tenant read fails to open exactly as AEAD would.</summary>
    private sealed class FakeProtector : ITokenProtector
    {
        public Task<string> ProtectAsync(
            string plaintext,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult($"enc({context.SubjectId}):{plaintext}");

        public Task<string?> TryUnprotectAsync(
            string? ciphertext,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        )
        {
            string prefix = $"enc({context.SubjectId}):";
            return Task.FromResult(
                ciphertext is not null && ciphertext.StartsWith(prefix, StringComparison.Ordinal)
                    ? ciphertext[prefix.Length..]
                    : null
            );
        }
    }

    private static (GiveawayCodePoolService Pools, AuthDbContext Db) BuildPools()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new GiveawayCodePoolService(db, new FakeProtector(), TimeProvider.System), db);
    }

    [Fact]
    public async Task Codes_are_ciphertext_at_rest_and_reads_are_masked()
    {
        (GiveawayCodePoolService pools, AuthDbContext db) = BuildPools();
        Result<CodePoolDto> pool = await pools.CreatePoolAsync(
            Tenant,
            new CreateCodePoolRequest("Steam Keys")
        );
        await pools.AddCodesAsync(
            Tenant,
            pool.Value.Id,
            new AddCodesRequest([new CodeInput("AAAA-BBBB-CCCC")])
        );

        // At rest: exactly the protector's output under the TENANT-BOUND context — the real
        // implementation is AES-GCM, so this proves the code went through the sealed-custody path
        // (a wrong-tenant open fails, as the reveal test below shows), never stored raw.
        GiveawayCode row = db.GiveawayCodes.Single();
        row.CodeCipher.Should().Be($"enc({Tenant}):AAAA-BBBB-CCCC");

        // Reads: masked — serialize the whole detail DTO and assert the plaintext appears NOWHERE.
        Result<CodePoolDetailDto> detail = await pools.GetPoolAsync(Tenant, pool.Value.Id);
        string serialized = JsonConvert.SerializeObject(detail.Value);
        serialized.Should().NotContain("AAAA-BBBB");
        detail
            .Value.Codes.Single()
            .Label.Should()
            .Be("…CCCC", "the masked tail keeps pools auditable");
    }

    [Fact]
    public async Task The_broadcaster_reveal_is_the_one_plaintext_path()
    {
        (GiveawayCodePoolService pools, AuthDbContext db) = BuildPools();
        Result<CodePoolDto> pool = await pools.CreatePoolAsync(
            Tenant,
            new CreateCodePoolRequest("Keys")
        );
        await pools.AddCodesAsync(
            Tenant,
            pool.Value.Id,
            new AddCodesRequest([new CodeInput("SECRET-1")])
        );
        GiveawayCode code = db.GiveawayCodes.Single();
        GiveawayWinner winner = new()
        {
            BroadcasterId = Tenant,
            GiveawayId = Guid.CreateVersion7(),
            ViewerUserId = Guid.CreateVersion7(),
            ViewerTwitchUserId = "111",
            DrawnAt = DateTime.UtcNow,
            AssignedCodeId = code.Id,
        };
        db.GiveawayWinners.Add(winner);
        db.SaveChanges();

        Result<string> revealed = await pools.RevealAssignedCodeAsync(Tenant, winner.Id);
        Result<string> wrongTenant = await pools.RevealAssignedCodeAsync(
            Guid.CreateVersion7(),
            winner.Id
        );

        revealed.IsSuccess.Should().BeTrue(revealed.ErrorMessage);
        revealed.Value.Should().Be("SECRET-1");
        wrongTenant.IsFailure.Should().BeTrue("another tenant can never open this channel's codes");
    }

    [Fact]
    public async Task A_pool_backing_an_active_giveaway_cannot_be_deleted()
    {
        (GiveawayCodePoolService pools, AuthDbContext db) = BuildPools();
        Result<CodePoolDto> pool = await pools.CreatePoolAsync(
            Tenant,
            new CreateCodePoolRequest("Keys")
        );
        db.Giveaways.Add(
            new Giveaway
            {
                BroadcasterId = Tenant,
                Title = "Live",
                EntryMode = GiveawayEntryMode.Keyword,
                Keyword = "!win",
                PrizeMode = GiveawayPrizeMode.CodePool,
                PrizeCodePoolId = pool.Value.Id,
                Status = GiveawayStatus.Open,
            }
        );
        db.SaveChanges();

        Result deleted = await pools.DeletePoolAsync(Tenant, pool.Value.Id);

        deleted.IsFailure.Should().BeTrue();
        deleted.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    // ── Fulfillment: the code-claim + whisper leg ──────────────────────────

    private static (
        GiveawayFulfillment Fulfillment,
        AuthDbContext Db,
        ITwitchWhispersApi Whispers
    ) BuildFulfillment(Result whisperOutcome)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITwitchWhispersApi whispers = Substitute.For<ITwitchWhispersApi>();
        whispers
            .SendWhisperAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(whisperOutcome);

        GiveawayFulfillment fulfillment = new(
            db,
            Substitute.For<ICurrencyAccountService>(),
            new ServiceCollection()
                .AddScoped<IPipelineEngine>(_ => Substitute.For<IPipelineEngine>())
                .BuildServiceProvider(),
            whispers,
            new FakeProtector(),
            TimeProvider.System,
            NullLogger<GiveawayFulfillment>.Instance
        );
        return (fulfillment, db, whispers);
    }

    private static (Giveaway Giveaway, GiveawayWinner Winner, GiveawayCode Code) SeedCodeDraw(
        AuthDbContext db,
        string plaintext = "KEY-1"
    )
    {
        GiveawayCodePool pool = new() { BroadcasterId = Tenant, Name = "Keys" };
        GiveawayCode code = new()
        {
            BroadcasterId = Tenant,
            CodePoolId = pool.Id,
            CodeCipher = $"enc({Tenant}):{plaintext}",
            Status = GiveawayCodeStatus.Available,
        };
        Giveaway giveaway = new()
        {
            BroadcasterId = Tenant,
            Title = "Key Drop",
            EntryMode = GiveawayEntryMode.Keyword,
            Keyword = "!win",
            PrizeMode = GiveawayPrizeMode.CodePool,
            PrizeCodePoolId = pool.Id,
        };
        GiveawayWinner winner = new()
        {
            BroadcasterId = Tenant,
            GiveawayId = giveaway.Id,
            ViewerUserId = Guid.CreateVersion7(),
            ViewerTwitchUserId = "111",
            DrawnAt = DateTime.UtcNow,
        };
        db.GiveawayCodePools.Add(pool);
        db.GiveawayCodes.Add(code);
        db.Giveaways.Add(giveaway);
        db.GiveawayWinners.Add(winner);
        db.SaveChanges();
        return (giveaway, winner, code);
    }

    [Fact]
    public async Task A_delivered_whisper_marks_the_code_delivered_with_the_plaintext_inside()
    {
        (GiveawayFulfillment fulfillment, AuthDbContext db, ITwitchWhispersApi whispers) =
            BuildFulfillment(Result.Success());
        (Giveaway giveaway, GiveawayWinner winner, GiveawayCode code) = SeedCodeDraw(db);

        await fulfillment.FulfillAsync(giveaway, winner);
        db.SaveChanges();

        winner.AssignedCodeId.Should().Be(code.Id);
        winner.WhisperDelivered.Should().BeTrue();
        code.Status.Should().Be(GiveawayCodeStatus.Delivered);
        code.AssignedWinnerId.Should().Be(winner.Id);
        await whispers
            .Received(1)
            .SendWhisperAsync(
                Tenant,
                "111",
                Arg.Is<string>(m => m.Contains("KEY-1")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_failed_whisper_leaves_the_code_assigned_for_broadcaster_reveal_never_lost()
    {
        (GiveawayFulfillment fulfillment, AuthDbContext db, _) = BuildFulfillment(
            Result.Failure("whispers restricted", "TWITCH_ERROR")
        );
        (Giveaway giveaway, GiveawayWinner winner, GiveawayCode code) = SeedCodeDraw(db);

        await fulfillment.FulfillAsync(giveaway, winner);
        db.SaveChanges();

        winner.AssignedCodeId.Should().Be(code.Id, "the code must never be lost");
        winner.WhisperDelivered.Should().BeFalse("the failure is flagged for the reveal fallback");
        code.Status.Should().Be(GiveawayCodeStatus.Assigned);
    }

    [Fact]
    public async Task An_exhausted_pool_leaves_the_winner_un_coded_and_flagged()
    {
        (GiveawayFulfillment fulfillment, AuthDbContext db, ITwitchWhispersApi whispers) =
            BuildFulfillment(Result.Success());
        (Giveaway giveaway, GiveawayWinner winner, GiveawayCode code) = SeedCodeDraw(db);
        code.Status = GiveawayCodeStatus.Delivered; // pool already spent
        db.SaveChanges();

        await fulfillment.FulfillAsync(giveaway, winner);
        db.SaveChanges();

        winner.AssignedCodeId.Should().BeNull("no code exists to assign — flagged, not faked");
        await whispers
            .DidNotReceiveWithAnyArgs()
            .SendWhisperAsync(default, default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_winners_never_share_a_code()
    {
        (GiveawayFulfillment fulfillment, AuthDbContext db, _) = BuildFulfillment(Result.Success());
        (Giveaway giveaway, GiveawayWinner first, _) = SeedCodeDraw(db, "KEY-1");
        GiveawayCode second = new()
        {
            BroadcasterId = Tenant,
            CodePoolId = giveaway.PrizeCodePoolId!.Value,
            CodeCipher = $"enc({Tenant}):KEY-2",
            Status = GiveawayCodeStatus.Available,
        };
        GiveawayWinner secondWinner = new()
        {
            BroadcasterId = Tenant,
            GiveawayId = giveaway.Id,
            ViewerUserId = Guid.CreateVersion7(),
            ViewerTwitchUserId = "222",
            DrawnAt = DateTime.UtcNow,
        };
        db.GiveawayCodes.Add(second);
        db.GiveawayWinners.Add(secondWinner);
        db.SaveChanges();

        await fulfillment.FulfillAsync(giveaway, first);
        db.SaveChanges();
        await fulfillment.FulfillAsync(giveaway, secondWinner);
        db.SaveChanges();

        first.AssignedCodeId.Should().NotBeNull();
        secondWinner.AssignedCodeId.Should().NotBeNull();
        secondWinner.AssignedCodeId!.Value.Should().NotBe(first.AssignedCodeId!.Value);
    }

    [Fact]
    public async Task The_pot_pays_the_summed_entry_costs()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        PostLedgerEntryCommand? posted = null;
        accounts
            .PostLedgerEntryAsync(
                Arg.Any<Guid>(),
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                posted = call.Arg<PostLedgerEntryCommand>();
                return Result.Failure<CurrencyLedgerEntryDto>("stubbed", "STUB");
            });
        GiveawayFulfillment fulfillment = new(
            db,
            accounts,
            new ServiceCollection()
                .AddScoped<IPipelineEngine>(_ => Substitute.For<IPipelineEngine>())
                .BuildServiceProvider(),
            Substitute.For<ITwitchWhispersApi>(),
            new FakeProtector(),
            TimeProvider.System,
            NullLogger<GiveawayFulfillment>.Instance
        );

        Giveaway giveaway = new()
        {
            BroadcasterId = Tenant,
            Title = "Pot",
            EntryMode = GiveawayEntryMode.Keyword,
            Keyword = "!win",
            PrizeMode = GiveawayPrizeMode.Currency,
            PrizeFromPot = true,
            EntryCost = 50,
            WinnerCount = 1,
        };
        db.Giveaways.Add(giveaway);
        // Three PAID entries (ledger-linked) + one free-looking row that must not count.
        for (int i = 0; i < 3; i++)
            db.GiveawayEntries.Add(
                new GiveawayEntry
                {
                    BroadcasterId = Tenant,
                    GiveawayId = giveaway.Id,
                    ViewerUserId = Guid.CreateVersion7(),
                    ViewerTwitchUserId = $"{i}",
                    EntryCostLedgerEntryId = i + 1,
                    EnteredAt = DateTime.UtcNow,
                }
            );
        db.GiveawayEntries.Add(
            new GiveawayEntry
            {
                BroadcasterId = Tenant,
                GiveawayId = giveaway.Id,
                ViewerUserId = Guid.CreateVersion7(),
                ViewerTwitchUserId = "free",
                EnteredAt = DateTime.UtcNow,
            }
        );
        GiveawayWinner winner = new()
        {
            BroadcasterId = Tenant,
            GiveawayId = giveaway.Id,
            ViewerUserId = Guid.CreateVersion7(),
            ViewerTwitchUserId = "111",
            DrawnAt = DateTime.UtcNow,
        };
        db.GiveawayWinners.Add(winner);
        db.SaveChanges();

        await fulfillment.FulfillAsync(giveaway, winner);

        posted.Should().NotBeNull();
        posted!.Amount.Should().Be(150, "the pot is the SUM of the paid entry costs (3 × 50)");
    }
}
