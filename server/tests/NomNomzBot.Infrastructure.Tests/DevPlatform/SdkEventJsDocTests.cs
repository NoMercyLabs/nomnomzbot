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
/// Hovering an event or one of its fields in the script or widget editor shows what it is. The emitter writes the
/// C# summary of every generated event type and field as a JSDoc line directly above it.
/// </summary>
public sealed class SdkEventJsDocTests
{
    private static readonly TriggerSample Follow = new(
        "channel.follow",
        "channel.follow",
        "42",
        "Viewer",
        new Dictionary<string, string> { ["user"] = "Viewer" }
    );

    [Fact]
    public void The_follow_event_summary_sits_above_its_interface_and_a_field_summary_above_its_field()
    {
        SdkTypeEmitter emitter = new(new EventCatalog(), new SingleSampleCatalog(Follow));

        Result<string> result = emitter.EmitTypeScript(SdkContext.Script, Follow.ResponseKey);

        result.IsSuccess.Should().BeTrue();
        string nl = Environment.NewLine;
        result
            .Value.Should()
            .Contain(
                "/** Published when a new user follows the channel — Twitch (EventSub channel.follow) or Kick "
                    + "(webhook channel.followed); Provider names the source (supporter-events.md §4.1). */"
                    + nl
                    + "interface NnzFollow {"
            );
        result
            .Value.Should()
            .Contain(
                "  /** The login name of the viewer (lowercase). */" + nl + "  userLogin: string;"
            );
    }

    private sealed class SingleSampleCatalog(TriggerSample sample) : ITriggerSampleCatalog
    {
        public IReadOnlyList<TriggerSample> List() => [sample];

        public TriggerSample? Find(string id) => id == sample.Id ? sample : null;
    }
}
