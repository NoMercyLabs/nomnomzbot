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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The enforcement arm (spam-defense.md §L5). This is the only code in the stack that can hurt somebody,
/// so these tests are about what it REFUSES to do at least as much as what it does.
///
/// <para>Every "nothing happened" assertion is written with <c>DidNotReceive</c>, so it is a checked fact
/// rather than the absence of a check — the difference between proving the account was untouched and
/// merely not having looked.</para>
/// </summary>
public class SpamEnforcementExecutorTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000e1");
    private static readonly Guid Owner = Guid.Parse("0199c000-0000-7000-8000-0000000000e2");

    private readonly SqliteConnection _connection;
    private readonly IModerationService _moderation = Substitute.For<IModerationService>();
    private readonly ITwitchModerationApi _twitch = Substitute.For<ITwitchModerationApi>();
    private int _heatTimeoutSeconds = 600;

    public SpamEnforcementExecutorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "chan-ext",
                Name = "chan",
                NameNormalized = "chan",
            }
        );
        db.SaveChanges();

        _twitch
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult(Result.Success()));

        _moderation
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult(Result.Success(new ModerationActionResult(true, null))));

        _moderation
            .GetAutomodConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                Task.FromResult(
                    Result.Success(
                        new AutomodConfigDto(
                            new AutomodLinkFilterDto(false, []),
                            new AutomodCapsFilterDto(false, 0),
                            new AutomodBannedPhrasesDto(false, []),
                            new AutomodEmoteSpamDto(false, 0),
                            HeatTimeoutSeconds: _heatTimeoutSeconds
                        )
                    )
                )
            );
    }

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private async Task<SpamEnforcementOutcome> ExecuteAsync(
        SpamDecision decision,
        string provider = AuthEnums.Platform.Twitch
    )
    {
        using AppDbContext db = NewDbContext();
        SpamEnforcementExecutor executor = new(
            db,
            _moderation,
            _twitch,
            NullLogger<SpamEnforcementExecutor>.Instance
        );

        return await executor.ExecuteAsync(
            Channel,
            provider,
            "msg-1",
            "viewer-1",
            decision,
            CancellationToken.None
        );
    }

    private async Task AssertNoTimeoutIssued() =>
        await _moderation
            .DidNotReceive()
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );

    private async Task AssertNoDeleteIssued() =>
        await _twitch
            .DidNotReceive()
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );

    // ---- What it refuses to do -------------------------------------------------------------------

    [Fact]
    public async Task DryRunTouchesNothing_EvenWhenTheVerdictWouldHaveEscalated()
    {
        // The whole promise of the observation week. The decision already returns None in dry run; this
        // is the second lock on the door, because the cost of the two disagreeing is somebody actioned
        // during the week they were told nothing would happen.
        SpamEnforcementOutcome outcome = await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.High, SpamTrustTier.Untrusted, dryRun: true)
        );

        outcome.DeletedMessage.Should().BeFalse();
        outcome.TimedOutAccount.Should().BeFalse();
        outcome.Skipped.Should().Be("dry run");
        await AssertNoDeleteIssued();
        await AssertNoTimeoutIssued();
    }

    [Fact]
    public async Task AnEstablishedViewer_IsNeverDeletedAndNeverTimedOut_AtAnyConfidence()
    {
        // SD8 carried all the way to the action. The ceiling is enforced in the decision; this proves
        // nothing downstream quietly reinterprets a Flag as something worse.
        foreach (SpamConfidence confidence in Enum.GetValues<SpamConfidence>())
        {
            SpamEnforcementOutcome outcome = await ExecuteAsync(
                SpamEnforcement.Decide(confidence, SpamTrustTier.Established, dryRun: false)
            );

            outcome.TimedOutAccount.Should().BeFalse($"{confidence} must not action a regular");
            outcome
                .DeletedMessage.Should()
                .BeFalse($"{confidence} must not delete a regular's message");
        }

        await AssertNoDeleteIssued();
        await AssertNoTimeoutIssued();
    }

    [Fact]
    public async Task ASemiTrustedViewer_LosesTheMessageButNeverTheAccount()
    {
        // SD11's ceiling, at the point where it would actually cost somebody something.
        SpamEnforcementOutcome outcome = await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.High, SpamTrustTier.SemiTrusted, dryRun: false)
        );

        outcome.DeletedMessage.Should().BeTrue();
        outcome.TimedOutAccount.Should().BeFalse();
        await AssertNoTimeoutIssued();
    }

    [Fact]
    public async Task ANonTwitchMessage_IsNotActedOn_AndSaysSoRatherThanImplyingCover()
    {
        // A streamer believing the room is covered when it is not would be the most dangerous bug this
        // feature could have.
        SpamEnforcementOutcome outcome = await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.High, SpamTrustTier.Untrusted, dryRun: false),
            provider: "kick"
        );

        outcome.DeletedMessage.Should().BeFalse();
        outcome.TimedOutAccount.Should().BeFalse();
        outcome.Skipped.Should().Contain("kick");
        await AssertNoDeleteIssued();
        await AssertNoTimeoutIssued();
    }

    [Fact]
    public async Task MediumConfidence_DeletesTheMessageAndLeavesTheAccountAlone()
    {
        SpamEnforcementOutcome outcome = await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.Medium, SpamTrustTier.Untrusted, dryRun: false)
        );

        outcome.DeletedMessage.Should().BeTrue();
        outcome.TimedOutAccount.Should().BeFalse();
        await AssertNoTimeoutIssued();
    }

    // ---- What it does ----------------------------------------------------------------------------

    [Fact]
    public async Task HighConfidenceAgainstAnUntrustedAccount_DeletesTheExactMessageAndTimesOut()
    {
        // The system still has to work: if nothing ever reached an action it would be theatre. Asserting
        // the exact message id matters — deleting "a" message is not deleting THE message.
        SpamEnforcementOutcome outcome = await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.High, SpamTrustTier.Untrusted, dryRun: false)
        );

        outcome.DeletedMessage.Should().BeTrue();
        outcome.TimedOutAccount.Should().BeTrue();
        await _twitch
            .Received(1)
            .DeleteChatMessageAsync(Channel, "msg-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheTimeoutNamesTheRightChannel_TheRightViewer_AndIsIssuedAsTheBroadcaster()
    {
        // Three distinctions that a bare "a timeout happened" assertion would let collapse: the wrong
        // channel, the wrong viewer, or the wrong operator — the last of which decides whose token signs
        // it, so getting it wrong means the action silently fails in production.
        await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.High, SpamTrustTier.Untrusted, dryRun: false)
        );

        await _moderation
            .Received(1)
            .TimeoutAsync(
                Channel.ToString(),
                Owner,
                "viewer-1",
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheTimeoutCarriesTheDecisionsOwnExplanation()
    {
        // SD7 at the sharpest end: the viewer reading their timeout reason sees what the system saw, not
        // "automated action".
        SpamDecision decision = SpamEnforcement.Decide(
            SpamConfidence.High,
            SpamTrustTier.Untrusted,
            dryRun: false
        );

        await ExecuteAsync(decision);

        decision.Reason.Should().NotBeNullOrWhiteSpace();
        await _moderation
            .Received(1)
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                decision.Reason,
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheAccountActionGoesThroughTheServiceThatFeedsHeat_NotHelixDirectly()
    {
        // The defect this avoids is the existing auto-mod handler's: it calls ITwitchModerationApi
        // directly, so its own bans never reach the projection and contribute no heat — the escalation
        // ladder then never sees the offences automod itself acted on.
        await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.High, SpamTrustTier.Untrusted, dryRun: false)
        );

        await _moderation
            .Received(1)
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
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
    public async Task TheChannelsOwnTimeoutLengthIsUsed_RatherThanASecondKnobNobodyTunes()
    {
        _heatTimeoutSeconds = 45;

        await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.High, SpamTrustTier.Untrusted, dryRun: false)
        );

        await _moderation
            .Received(1)
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                45,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AnUnconfiguredChannelFallsBackToTenMinutes_RatherThanZeroSeconds()
    {
        // A stored 0 predates the field. Passing it through would issue a zero-second timeout — an
        // action that looks like it worked and does nothing.
        _heatTimeoutSeconds = 0;

        await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.High, SpamTrustTier.Untrusted, dryRun: false)
        );

        await _moderation
            .Received(1)
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                600,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AFailedDeleteIsReportedAsFailed_NotAsSuccess()
    {
        // "It spoke with nothing attached" — an enforcement path that reports success when the platform
        // refused is how an operator believes the room is covered when it is not.
        _twitch
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult(Result.Failure("nope")));

        SpamEnforcementOutcome outcome = await ExecuteAsync(
            SpamEnforcement.Decide(SpamConfidence.Medium, SpamTrustTier.Untrusted, dryRun: false)
        );

        outcome.DeletedMessage.Should().BeFalse();
    }

    public void Dispose() => _connection.Dispose();
}
