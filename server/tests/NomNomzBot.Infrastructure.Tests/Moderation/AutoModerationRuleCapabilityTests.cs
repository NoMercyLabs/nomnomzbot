// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The two capabilities that existed only in the never-called <c>AutoModerationEngine</c>: a link
/// allow-list and regex banned phrases.
///
/// <para>The engine was a second, parallel auto-mod design — DI-registered, tested, and reachable from
/// no production code, while the live handler re-implemented the same three checks beside it. Keeping
/// both is how two implementations drift into disagreeing about what the channel's rules mean, so the
/// engine is gone and the capabilities it alone had now live where rules actually run.</para>
///
/// <para>Its slow mode is deliberately NOT ported: Twitch has native slow mode via chat settings, and
/// a bot-side cooldown imitating it would be a second source of truth for something the platform
/// already enforces.</para>
/// </summary>
public sealed class AutoModerationRuleCapabilityTests
{
    private static readonly Guid Channel = Guid.Parse("0192d200-0000-7000-8000-0000000000c1");
    private static readonly Guid OwnerUserId = Guid.Parse("0192d200-0000-7000-8000-0000000000c2");
    private const string OffenderTwitchId = "900888";

    private static async Task<(
        AutoModerationHandler Handler,
        IModerationService Moderation
    )> BuildAsync(string ruleJson)
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = "700888",
                OwnerUserId = OwnerUserId,
                Name = "c",
                NameNormalized = "c",
            }
        );
        db.Records.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = OwnerUserId.ToString(),
                RecordType = "moderation_rule",
                Data = ruleJson,
            }
        );
        await db.SaveChangesAsync();

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, null)));

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(moderation);
        services.AddSingleton(Substitute.For<ITwitchModerationApi>());
        ServiceProvider provider = services.BuildServiceProvider();

        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        AutoModerationHandler handler = new(
            scopeFactory,
            new AutoModRuleCache(
                scopeFactory,
                TimeProvider.System,
                NullLogger<AutoModRuleCache>.Instance
            ),
            NullLogger<AutoModerationHandler>.Instance
        );
        return (handler, moderation);
    }

    private static ChatMessageReceivedEvent Message(string text) =>
        new()
        {
            MessageId = "m-1",
            BroadcasterId = Channel,
            TwitchBroadcasterId = "700888",
            UserId = OffenderTwitchId,
            UserDisplayName = "Chatter",
            UserLogin = "chatter",
            Message = text,
            Fragments = [],
            IsBroadcaster = false,
            IsModerator = false,
            IsVip = false,
            IsSubscriber = false,
            Badges = [],
        };

    private static async Task<bool> ActionedAsync(string ruleJson, string message)
    {
        (AutoModerationHandler handler, IModerationService moderation) = await BuildAsync(ruleJson);

        await handler.HandleAsync(Message(message), CancellationToken.None);

        return moderation
            .ReceivedCalls()
            .Any(call => call.GetMethodInfo().Name == nameof(IModerationService.TimeoutAsync));
    }

    private const string LinksWithAllowList =
        "{ \"Name\": \"no links\", \"Type\": \"links\", \"Action\": \"timeout\", \"IsEnabled\": true, "
        + "\"Settings\": { \"allowed_domains\": [\"example.com\", \"twitch.tv\"] } }";

    private const string LinksWithNoAllowList =
        "{ \"Name\": \"no links\", \"Type\": \"links\", \"Action\": \"timeout\", \"IsEnabled\": true }";

    [Fact]
    public async Task WithNoAllowListEveryLinkTrips()
    {
        Assert.True(await ActionedAsync(LinksWithNoAllowList, "see https://example.com"));
    }

    [Fact]
    public async Task AnAllowedDomainPasses()
    {
        Assert.False(await ActionedAsync(LinksWithAllowList, "clips at https://example.com/x"));
    }

    [Fact]
    public async Task ASubdomainOfAnAllowedDomainPasses()
    {
        // Allowing "example.com" without allowing "www.example.com" would be a rule nobody could
        // configure correctly on the first try.
        Assert.False(await ActionedAsync(LinksWithAllowList, "see https://www.example.com/x"));
    }

    [Fact]
    public async Task ADomainOutsideTheAllowListTrips()
    {
        Assert.True(await ActionedAsync(LinksWithAllowList, "see https://free-nitro.example/x"));
    }

    [Fact]
    public async Task OneDisallowedLinkTripsEvenWhenTheOthersAreAllowed()
    {
        // The padding attack: wrap the payload in permitted domains and walk through.
        Assert.True(
            await ActionedAsync(
                LinksWithAllowList,
                "https://twitch.tv/me and https://free-nitro.example/x"
            )
        );
    }

    [Fact]
    public async Task ALinkWithNoParseableHostTrips()
    {
        // Unparseable is not the same as allowed. Failing open here would make the allow-list a way
        // THROUGH the filter rather than a narrow exception to it.
        Assert.True(await ActionedAsync(LinksWithAllowList, "see https://:8080/path"));
    }

    private const string RegexPhrases =
        "{ \"Name\": \"no gambling\", \"Type\": \"banned_phrases\", \"Action\": \"timeout\", \"IsEnabled\": true, "
        + "\"Settings\": { \"use_regex\": true, \"phrases\": [\"fr[e3]{2} ?n[i1]tr[o0]\"] } }";

    private const string LiteralPhrases =
        "{ \"Name\": \"no gambling\", \"Type\": \"banned_phrases\", \"Action\": \"timeout\", \"IsEnabled\": true, "
        + "\"Settings\": { \"phrases\": [\"fr[e3]{2} ?n[i1]tr[o0]\"] } }";

    [Fact]
    public async Task RegexPhrasesMatchTheEvasionsALiteralWouldMiss()
    {
        Assert.True(await ActionedAsync(RegexPhrases, "get fr33 n1tr0 here"));
    }

    [Fact]
    public async Task WithoutTheRegexFlagThePatternIsTreatedLiterally()
    {
        // Opt-in matters: an operator who typed a phrase containing brackets must not have it silently
        // reinterpreted as a pattern that matches far more than they meant.
        Assert.False(await ActionedAsync(LiteralPhrases, "get fr33 n1tr0 here"));
        Assert.True(await ActionedAsync(LiteralPhrases, "literally fr[e3]{2} ?n[i1]tr[o0] here"));
    }

    [Fact]
    public async Task AnInvalidPatternFallsBackToALiteralMatchRatherThanDisarming()
    {
        const string broken =
            "{ \"Name\": \"broken\", \"Type\": \"banned_phrases\", \"Action\": \"timeout\", \"IsEnabled\": true, "
            + "\"Settings\": { \"use_regex\": true, \"phrases\": [\"unclosed[\"] } }";

        Assert.True(await ActionedAsync(broken, "this contains unclosed[ exactly"));
    }
}
