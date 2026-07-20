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
using NomNomzBot.Infrastructure.Platform;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Platform;

// Guards the fix for the "custom command with a single {list.pick.<name>} response does not respond in chat" bug.
// A command authored as one response persists that response in the singular Command.TemplateResponse while the
// plural TemplateResponses array stays empty; the chat hot path (ChatMessageHandler.PickResponse) reads ONLY the
// plural array, so before the fix such a command resolved to an empty message that Twitch silently dropped.
// ChannelRegistry now promotes the singular response into the array at load time — this proves that promotion.
public class ChannelRegistryResponseNormalizationTests
{
    [Fact]
    public void Single_response_command_promotes_the_singular_field_into_the_array()
    {
        // The exact production shape: plural empty, the real response (a pick-list token) in the singular field.
        string[] result = ChannelRegistry.NormalizeResponses([], "{list.pick.notADesigner}");

        result.Should().ContainSingle().Which.Should().Be("{list.pick.notADesigner}");
    }

    [Fact]
    public void A_null_responses_array_still_promotes_the_singular_field()
    {
        string[] result = ChannelRegistry.NormalizeResponses(null, "{list.pick.notADev}");

        result.Should().ContainSingle().Which.Should().Be("{list.pick.notADev}");
    }

    [Fact]
    public void A_populated_responses_array_is_used_verbatim_and_the_singular_is_ignored()
    {
        // Random-responses commands persist their list in the plural array; that must win untouched.
        string[] responses = ["first", "second", "third"];

        string[] result = ChannelRegistry.NormalizeResponses(responses, "ignored-singular");

        result.Should().Equal("first", "second", "third");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_response_in_either_field_yields_an_empty_array(string? singular)
    {
        // Empty on both sides must stay empty so the chat path falls through to the builtin catalog rather than
        // fabricating a response.
        string[] result = ChannelRegistry.NormalizeResponses([], singular);

        result.Should().BeEmpty();
    }
}
