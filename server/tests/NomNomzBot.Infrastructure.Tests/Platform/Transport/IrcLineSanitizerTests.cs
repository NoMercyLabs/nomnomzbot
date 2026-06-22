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
using NomNomzBot.Infrastructure.Platform.Transport;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport;

/// <summary>
/// Proves outgoing IRC text cannot carry line-control characters that would inject extra IRC commands. The
/// transport builds <c>PRIVMSG #chan :{message}\r\n</c>, so a message containing CRLF must not be able to
/// terminate the line early and append a second command (e.g. a ban).
/// </summary>
public sealed class IrcLineSanitizerTests
{
    [Fact]
    public void Message_strips_CRLF_so_a_second_IRC_command_cannot_be_injected()
    {
        string malicious = "hi there\r\nPRIVMSG #victim :/ban someone";

        string sanitized = IrcLineSanitizer.Message(malicious);

        sanitized.Should().NotContain("\r");
        sanitized.Should().NotContain("\n");
        // The injected command text survives only as inert message content on a single line.
        sanitized.Should().Be("hi there  PRIVMSG #victim :/ban someone");
    }

    [Fact]
    public void Message_strips_null_bytes()
    {
        IrcLineSanitizer.Message("a\0b").Should().Be("a b");
    }

    [Fact]
    public void Message_truncates_to_the_twitch_length_cap()
    {
        string longText = new('x', IrcLineSanitizer.MaxMessageLength + 50);

        IrcLineSanitizer.Message(longText).Length.Should().Be(IrcLineSanitizer.MaxMessageLength);
    }

    [Fact]
    public void Message_leaves_ordinary_text_untouched()
    {
        IrcLineSanitizer
            .Message("Thanks for the follow, @viewer!")
            .Should()
            .Be("Thanks for the follow, @viewer!");
    }

    [Fact]
    public void TagValue_removes_separators_that_would_break_an_irc_tag()
    {
        // A crafted reply-id must not be able to inject a space (ending the tag) + a new command.
        string malicious = "abc 123;evil\r\nPRIVMSG";

        string sanitized = IrcLineSanitizer.TagValue(malicious);

        sanitized.Should().Be("abc123evilPRIVMSG");
        sanitized.Should().NotContainAny(" ", ";", "\r", "\n");
    }

    [Fact]
    public void TagValue_preserves_a_normal_uuid_message_id()
    {
        string id = "0192a000-0000-7000-8000-000000000abc";

        IrcLineSanitizer.TagValue(id).Should().Be(id);
    }
}
