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
using NomNomzBot.Application.Commands.Services;
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
        // An alert handler's sample id is its event type name; other triggers (command, timer, ...) name their own.
        List<string> eventNames =
        [
            .. typeof(TwitchAlertHandlerBase<>)
                .Assembly.GetTypes()
                .Where(type =>
                    type is { IsAbstract: false, BaseType: { IsGenericType: true } baseType }
                    && baseType.GetGenericTypeDefinition() == typeof(TwitchAlertHandlerBase<>)
                )
                .Select(type => type.BaseType!.GetGenericArguments()[0].Name),
        ];

        List<TriggerSample> samples = AllSamples();

        eventNames.Count.Should().BeGreaterThan(20);
        samples
            .Select(sample => sample.Id)
            .Where(eventNames.Contains)
            .Should()
            .BeEquivalentTo(eventNames);
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

    [Fact]
    public void Every_variable_a_preset_advertises_is_set_by_its_live_trigger()
    {
        // The dashboard offers each preset's variables as placeholders. A placeholder the live trigger never
        // sets prints empty on stream: the live watch streak set none of {user}, {viewer.name} and
        // {engagement.streak}, which its preset advertises.
        Dictionary<string, IReadOnlyList<string>> advertised =
            EventResponsePresetCatalog.Presets.ToDictionary(
                preset => preset.EventType,
                preset => preset.Variables
            );

        List<string> unset =
        [
            .. AllSamples()
                .Where(sample => advertised.ContainsKey(sample.ResponseKey))
                .SelectMany(sample =>
                    advertised[sample.ResponseKey]
                        .Where(variable => !sample.Variables.ContainsKey(variable))
                        .Select(variable => $"{sample.Id}: {{{variable}}}")
                ),
        ];

        // Joined, so a failure names every gap (BeEmpty names only the first).
        string.Join(", ", unset).Should().BeEmpty();
    }
}
