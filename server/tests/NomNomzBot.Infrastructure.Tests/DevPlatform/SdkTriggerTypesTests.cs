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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// A script is written for one trigger, and the trigger decides which variables exist. With a trigger the emitted
/// <c>bot.getVar</c> takes only that trigger's keys, so the editor flags a typo; a dynamic overload keeps keys set
/// by <c>setVar</c> or earlier pipeline steps legal. A script has no event bus, so it gets no <c>NnzEventMap</c>.
/// </summary>
public sealed class SdkTriggerTypesTests
{
    private static readonly TriggerSample Follow = new(
        "channel.follow",
        "channel.follow",
        "42",
        "Viewer",
        new Dictionary<string, string>
        {
            ["user"] = "Viewer",
            ["user.id"] = "42",
            ["followed_at"] = "2026-10-03T12:00:00Z",
        }
    );

    private static SdkTypeEmitter Emitter() =>
        new(new EventCatalog(), new FakeTriggerSampleCatalog(Follow));

    [Fact]
    public void The_script_dts_has_no_event_map_and_the_widget_dts_keeps_it()
    {
        SdkTypeEmitter emitter = Emitter();

        emitter.EmitTypeScript(SdkContext.Script).Should().NotContain("interface NnzEventMap");
        emitter.EmitTypeScript(SdkContext.Widget).Should().Contain("interface NnzEventMap {");
    }

    [Fact]
    public void A_trigger_types_getVar_with_exactly_the_keys_that_trigger_sets()
    {
        Result<string> result = Emitter().EmitTypeScript(SdkContext.Script, "channel.follow");

        result.IsSuccess.Should().BeTrue();
        string ts = result.Value;
        ts.Should().Contain("type NnzVarKey = 'followed_at' | 'user' | 'user.id';");
        ts.Should().Contain("  getVar(key: NnzVarKey): string | null;");
        ts.Should().Contain("  getVar(key: string, dynamic: true): string | null;");
        ts.Should().NotContain("  getVar(key: string): string | null;");
        ts.Should().NotContain("interface NnzEventMap");
    }

    [Fact]
    public void The_union_holds_every_key_the_sample_catalog_lists_for_the_trigger()
    {
        Result<string> result = Emitter().EmitTypeScript(SdkContext.Script, Follow.ResponseKey);

        foreach (string key in Follow.Variables.Keys)
            result.Value.Should().Contain($"'{key}'");
    }

    [Fact]
    public void Two_samples_of_one_response_key_share_one_union_of_their_keys()
    {
        TriggerSample ban = Follow with
        {
            Id = "ban",
            ResponseKey = "channel.ban",
            Variables = new Dictionary<string, string> { ["reason"] = "x", ["user"] = "a" },
        };
        TriggerSample timeout = ban with
        {
            Id = "timeout",
            Variables = new Dictionary<string, string> { ["duration"] = "60", ["user"] = "a" },
        };
        SdkTypeEmitter emitter = new(
            new EventCatalog(),
            new FakeTriggerSampleCatalog(ban, timeout)
        );

        emitter
            .EmitTypeScript(SdkContext.Script, "channel.ban")
            .Value.Should()
            .Contain("type NnzVarKey = 'duration' | 'reason' | 'user';");
    }

    [Fact]
    public void A_chat_command_types_its_keys_and_the_one_based_args_as_a_template_literal()
    {
        string ts = Emitter().EmitTypeScript(SdkContext.Script, "command").Value;

        ts.Should().Contain("'user.role'");
        ts.Should().Contain("`args.${number}`");
        ts.Should().Contain("'args.count'");
    }

    [Fact]
    public void The_chat_command_keys_match_the_variables_the_live_handler_seeds()
    {
        ChatMessageReceivedEvent chat = new()
        {
            BroadcasterId = Guid.Parse("0198a000-0000-7000-8000-00000000e001"),
            MessageId = "m",
            TwitchBroadcasterId = "tw",
            UserId = "u",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = "!roll 20 6",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

        List<string> live =
        [
            .. ChatMessageHandler
                .BuildInitialVariables(chat, "20 6")
                .Keys.Select(key =>
                    key.StartsWith("args.", StringComparison.Ordinal) && key != "args.count"
                        ? "args.${number}"
                        : key
                )
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal),
        ];

        ChatCommandVariableKeys.Keys.Should().Equal(live);
    }

    [Fact]
    public void An_unknown_trigger_is_a_failure_with_its_own_code()
    {
        Result<string> result = Emitter().EmitTypeScript(SdkContext.Script, "nope");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("UNKNOWN_TRIGGER");
    }

    private sealed class FakeTriggerSampleCatalog(params TriggerSample[] samples)
        : ITriggerSampleCatalog
    {
        public IReadOnlyList<TriggerSample> List() => samples;

        public TriggerSample? Find(string id) => samples.FirstOrDefault(s => s.Id == id);
    }
}
