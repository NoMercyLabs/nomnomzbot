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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Moderation.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation.MassBan;

/// <summary>
/// Proves <c>!allow massban</c> and <c>!disallow massban</c>: they sit at the lead-moderator floor, answer only
/// to the <c>massban</c> argument, decide for the channel the command came from, and reply with the slot and
/// count the decision produced.
/// </summary>
public sealed class MassBanBuiltinTests
{
    private static readonly Guid Channel = Guid.NewGuid();

    private static BuiltinCommandContext Context(string args) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "77",
            TriggeringUserDisplayName = "LeadMod",
            Args = args,
            CommandPrefix = "!",
        };

    private static IBuiltinResponseComposer EchoComposer()
    {
        IBuiltinResponseComposer composer = Substitute.For<IBuiltinResponseComposer>();
        composer
            .ComposeAsync(Arg.Any<BuiltinResponseRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                BuiltinResponseRequest r = call.Arg<BuiltinResponseRequest>();
                return $"{r.BuiltinKey}/{r.Slot}/{r.Variables!["massban.count"]}";
            });
        return composer;
    }

    [Fact]
    public void Both_commands_need_a_lead_moderator_and_carry_a_formal_text_for_every_tone()
    {
        IMassBanConsentService consent = Substitute.For<IMassBanConsentService>();
        AllowBuiltin allow = new(consent, EchoComposer());
        DisallowBuiltin disallow = new(consent, EchoComposer());

        allow.DefaultMinPermissionLevel.Should().Be(20);
        disallow.DefaultMinPermissionLevel.Should().Be(20);
        foreach (
            (string key, string slot) in new[]
            {
                (BuiltinResponseSlots.Allow.Key, BuiltinResponseSlots.Allow.Approved),
                (BuiltinResponseSlots.Disallow.Key, BuiltinResponseSlots.Disallow.Declined),
                (BuiltinResponseSlots.MassBan.Key, BuiltinResponseSlots.MassBan.Request),
                (BuiltinResponseSlots.MassBan.Key, BuiltinResponseSlots.MassBan.Completed),
            }
        )
        {
            string[] tones =
            [
                PersonalityTone.Informative,
                PersonalityTone.Friendly,
                PersonalityTone.Sassy,
                PersonalityTone.Hype,
                PersonalityTone.Chill,
            ];
            tones
                .SelectMany(tone => ToneTemplateCatalog.Get(tone, key, slot))
                .Distinct()
                .Should()
                .ContainSingle(
                    $"{key}/{slot} is a moderation notice and reads the same in every tone"
                );
        }
    }

    [Fact]
    public async Task Allow_without_massban_explains_the_usage_and_decides_nothing()
    {
        IMassBanConsentService consent = Substitute.For<IMassBanConsentService>();
        AllowBuiltin allow = new(consent, EchoComposer());

        Result<string> reply = await allow.ExecuteAsync(Context("everything"));

        reply.Value.Should().Be("allow/usage/0");
        await consent
            .DidNotReceive()
            .ApproveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Allow_massban_approves_this_channels_held_batches_and_reports_the_count()
    {
        IMassBanConsentService consent = Substitute.For<IMassBanConsentService>();
        consent
            .ApproveAsync(Channel, "LeadMod", Arg.Any<CancellationToken>())
            .Returns(
                new MassBanDecision(MassBanDecisionStatus.Approved, 120),
                new MassBanDecision(MassBanDecisionStatus.NothingPending, 0)
            );
        AllowBuiltin allow = new(consent, EchoComposer());

        Result<string> first = await allow.ExecuteAsync(Context(" MassBan "));
        Result<string> again = await allow.ExecuteAsync(Context("massban"));

        first.Value.Should().Be("allow/approved/120");
        again.Value.Should().Be("allow/nothingpending/0");
    }

    [Fact]
    public async Task Disallow_massban_declines_this_channels_held_batches_and_reports_the_count()
    {
        IMassBanConsentService consent = Substitute.For<IMassBanConsentService>();
        consent
            .DeclineAsync(Channel, "LeadMod", Arg.Any<CancellationToken>())
            .Returns(new MassBanDecision(MassBanDecisionStatus.Declined, 120));
        DisallowBuiltin disallow = new(consent, EchoComposer());

        Result<string> reply = await disallow.ExecuteAsync(Context("massban"));

        reply.Value.Should().Be("disallow/declined/120");
        await consent
            .DidNotReceive()
            .ApproveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
