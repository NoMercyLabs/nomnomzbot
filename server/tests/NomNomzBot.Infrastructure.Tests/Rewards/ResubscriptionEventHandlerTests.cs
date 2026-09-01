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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using NSubstitute.Core;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// S-OWN18 done-when: a resubscription carries the subscriber's own resub message through to the
/// event-response template as a "they also said: ..." addendum — matching the old bot's
/// resub/cheer/watch-streak convention (NoMercyBot.Services.Twitch.WatchStreakService,
/// NoMercyBot.Services.Twitch.EventHandlers.MonetizationEventHandler) — but ONLY when the subscriber
/// actually wrote one; a resub with no message must never render an empty "they also said:" fragment.
/// The template engine has no {{#if}} block syntax (TemplateResolver.VariablePattern is flat
/// substitution only), so <see cref="ResubscriptionEvent.Message"/> is threaded into a pre-formatted
/// `also_said` variable that already resolves to the full clause, or to an empty string when absent.
/// </summary>
public sealed class ResubscriptionEventHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d502");

    private sealed record Harness(
        ResubscriptionEventHandler Handler,
        IEventResponseExecutor Executor
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(executor)
            .BuildServiceProvider();

        ResubscriptionEventHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IPipelineEngine>(),
            NullLogger<ResubscriptionEventHandler>.Instance
        );

        return new(handler, executor);
    }

    [Fact]
    public async Task A_resub_with_a_message_renders_the_they_also_said_addendum()
    {
        Harness h = Build();
        ResubscriptionEvent resub = new()
        {
            BroadcasterId = Channel,
            UserId = "tw-321",
            UserDisplayName = "LoyalViewer",
            Tier = "1000",
            CumulativeMonths = 6,
            StreakMonths = 3,
            Message = "so hyped to be back!",
        };

        await h.Handler.HandleAsync(resub);

        Dictionary<string, string> variables = await CapturedVariablesAsync(h.Executor, Channel);

        // The consequence: {also_said} carries the full, pre-formatted clause — never the raw message
        // alone — so a default template of "...Thank you!{also_said}" reads naturally.
        variables["also_said"].Should().Be(" They also said: \"so hyped to be back!\"");
        variables["message"].Should().Be("so hyped to be back!");

        string rendered = new TemplateResolver(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        ).Resolve("{user} resubscribed for {months} months! Thank you!{also_said}", variables);
        rendered
            .Should()
            .Be(
                "LoyalViewer resubscribed for 6 months! Thank you! They also said: \"so hyped to be back!\""
            );
    }

    [Fact]
    public async Task A_resub_without_a_message_renders_the_thank_you_with_no_also_said_fragment()
    {
        Harness h = Build();
        ResubscriptionEvent resub = new()
        {
            BroadcasterId = Channel,
            UserId = "tw-654",
            UserDisplayName = "QuietRegular",
            Tier = "1000",
            CumulativeMonths = 2,
            StreakMonths = 2,
            Message = null,
        };

        await h.Handler.HandleAsync(resub);

        Dictionary<string, string> variables = await CapturedVariablesAsync(h.Executor, Channel);

        // No raw {also_said} token, no empty '"..."' placeholder — just an empty string to substitute.
        variables["also_said"].Should().BeEmpty();
        variables["message"].Should().BeEmpty();

        string rendered = new TemplateResolver(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        ).Resolve("{user} resubscribed for {months} months! Thank you!{also_said}", variables);
        rendered.Should().Be("QuietRegular resubscribed for 2 months! Thank you!");
        rendered.Should().NotContain("also said");
        rendered.Should().NotContain("They also said: \"\"");
    }

    private static Task<Dictionary<string, string>> CapturedVariablesAsync(
        IEventResponseExecutor executor,
        Guid channel
    )
    {
        ICall call = executor
            .ReceivedCalls()
            .Should()
            .ContainSingle(c =>
                c.GetMethodInfo().Name == nameof(IEventResponseExecutor.ExecuteAsync)
                && (Guid)c.GetArguments()[0]! == channel
                && (string)c.GetArguments()[1]! == "channel.subscription.message"
            )
            .Subject;
        return Task.FromResult((Dictionary<string, string>)call.GetArguments()[4]!);
    }
}
