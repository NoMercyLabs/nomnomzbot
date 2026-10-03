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
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// A script that several triggers run may read the keys of any of them behind a branch, so its <c>NnzVarKey</c> is
/// the union of every trigger's keys. A key no trigger sets stays a type error.
/// </summary>
public sealed class SdkTriggerUnionTypesTests
{
    private static readonly TriggerSample Follow = new(
        "channel.follow",
        "channel.follow",
        "42",
        "Viewer",
        new Dictionary<string, string> { ["user"] = "Viewer", ["followed_at"] = "t" }
    );

    private static SdkTypeEmitter Emitter() => new(new EventCatalog(), new OneSample());

    [Fact]
    public void The_union_of_command_and_follow_holds_the_keys_of_both_and_not_a_typo()
    {
        Result<string> result = Emitter()
            .EmitTypeScript(SdkContext.Script, ["command", "channel.follow"]);

        result.IsSuccess.Should().BeTrue();
        string line = result.Value.Split('\n').Single(l => l.StartsWith("type NnzVarKey"));
        line.Should().Contain("'args.count'");
        line.Should().Contain("'followed_at'");
        line.Should().Contain("'user'");
        line.Should().NotContain("typo");
    }

    [Fact]
    public void One_unknown_trigger_in_the_list_fails_so_no_wrong_narrow_type_is_served()
    {
        Result<string> result = Emitter()
            .EmitTypeScript(SdkContext.Script, ["command", "channel.mystery"]);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("UNKNOWN_TRIGGER");
    }

    [Fact]
    public void A_one_element_list_equals_the_single_key_result()
    {
        Emitter()
            .EmitTypeScript(SdkContext.Script, ["channel.follow"])
            .Value.Should()
            .Be(Emitter().EmitTypeScript(SdkContext.Script, "channel.follow").Value);
    }

    private sealed class OneSample : ITriggerSampleCatalog
    {
        public IReadOnlyList<TriggerSample> List() => [Follow];

        public TriggerSample? Find(string id) => id == Follow.Id ? Follow : null;
    }
}
