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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// A script test run could only seed hand-typed variables, so testing "what does my script do on a follow"
/// meant guessing the keys a follow sets. Every alert handler now offers a sample of its own event, and the
/// variables come from the handler's live builder, so a test run sees the keys a live event sets.
/// </summary>
public sealed class TriggerSampleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static List<TriggerSample> AllSamples()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=trigger_sample_test;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = false }
        );
        using IServiceScope scope = provider.CreateScope();
        return
        [
            .. scope
                .ServiceProvider.GetServices<ITriggerSampleSource>()
                .Select(source => source.Sample(Now)),
        ];
    }

    [Fact]
    public void Every_alert_handler_offers_a_sample()
    {
        int handlers = typeof(TwitchAlertHandlerBase<>)
            .Assembly.GetTypes()
            .Count(type =>
                !type.IsAbstract
                && type.BaseType is { IsGenericType: true } baseType
                && baseType.GetGenericTypeDefinition() == typeof(TwitchAlertHandlerBase<>)
            );

        List<TriggerSample> samples = AllSamples();

        handlers.Should().BeGreaterThan(20);
        samples.Should().HaveCount(handlers);
        samples.Select(sample => sample.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_follow_sample_carries_the_variables_a_live_follow_sets()
    {
        TriggerSample follow = AllSamples().Single(sample => sample.Id == "FollowEvent");

        follow.ResponseKey.Should().Be("channel.follow");
        follow.Variables["user"].Should().Be(follow.UserDisplayName);
        follow.Variables["user.id"].Should().Be(follow.UserId);
        follow.Variables["followed_at"].Should().Be(Now.ToString("O"));
        // The live trigger aliases target to the user; the sample goes through the same path.
        follow.Variables["target"].Should().Be(follow.UserDisplayName);
    }

    [Fact]
    public void A_ban_and_a_timeout_share_a_response_but_stay_separate_samples()
    {
        List<TriggerSample> samples = AllSamples();
        TriggerSample ban = samples.Single(sample => sample.Id == "UserBannedEvent");
        TriggerSample timeout = samples.Single(sample => sample.Id == "UserTimedOutEvent");

        ban.ResponseKey.Should().Be(timeout.ResponseKey);
        timeout.Variables["duration"].Should().Be("10 minutes");
        ban.Variables["duration"].Should().Be("permanent");
    }
}
