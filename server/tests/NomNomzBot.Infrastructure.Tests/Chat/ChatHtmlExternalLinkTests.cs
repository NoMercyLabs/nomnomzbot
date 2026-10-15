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
using NomNomzBot.Domain.Moderation.ChatFilters;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Guards the link rule of the chat-HTML sanitiser: an <c>&lt;a&gt;</c> to a host off the channel's link allowlist is
/// unwrapped to its content (the words stay, the link goes) unless the sender may link anywhere; an allowlisted host
/// (or a sub-domain of one) keeps its link for everyone.
/// </summary>
public sealed class ChatHtmlExternalLinkTests
{
    private static readonly LinkPolicy Allowlist = new() { AllowedDomains = ["nomercy.tv"] };

    private static Predicate<string> KeepOnlyAllowlisted => host => Allowlist.AllowsHost(host);

    [Fact]
    public void An_external_link_from_a_sender_without_the_grant_is_unwrapped_to_its_text()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"see <a href=""https://evil.example/x""><b>click</b> me</a>!",
            KeepOnlyAllowlisted
        );

        clean.Should().Be("see <b>click</b> me!");
    }

    [Fact]
    public void An_external_link_from_a_sender_with_the_grant_is_kept()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"see <a href=""https://evil.example/x"">click</a>",
            _ => true
        );

        clean.Should().Be(@"see <a href=""https://evil.example/x"">click</a>");
    }

    [Theory]
    [InlineData("https://nomercy.tv/live")]
    [InlineData("https://www.nomercy.tv/live")]
    public void An_allowlisted_host_keeps_its_link_for_everyone(string url)
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            $@"<a href=""{url}"">site</a>",
            KeepOnlyAllowlisted
        );

        clean.Should().Be($@"<a href=""{url}"">site</a>");
    }

    [Fact]
    public void A_look_alike_host_is_not_on_the_allowlist()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<a href=""https://evilnomercy.tv/"">site</a>",
            KeepOnlyAllowlisted
        );

        clean.Should().Be("site");
    }

    [Fact]
    public void Only_the_external_link_goes_when_a_message_mixes_both()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<a href=""https://nomercy.tv/"">ok</a> <a href=""https://evil.example/"">bad</a>",
            KeepOnlyAllowlisted
        );

        clean.Should().Be(@"<a href=""https://nomercy.tv/"">ok</a> bad");
    }
}
