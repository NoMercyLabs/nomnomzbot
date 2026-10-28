// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Chat;

public sealed class ChatHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // Limits and fitting: Twitch allows 500 characters; short text is never touched.
            { "{chat.limit}", "500" },
            { "{chat.fit:short text}", "short text" },
            { "{chat.fit:short text|(more on the page)}", "short text" },
            { "{chat.fit.words:short text}", "short text" },
            // Mentions: an @ in front, none, or a zero-width space that keeps the ping away.
            { "{chat.mention:ViewerTwo}", "@ViewerTwo" },
            { "{chat.mention:@ViewerTwo}", "@ViewerTwo" },
            { "{chat.mention.off:ViewerTwo}", "ViewerTwo" },
            { "{chat.mention.off:@ViewerTwo}", "ViewerTwo" },
            { "{chat.mention.soft:ViewerTwo}", "V​iewerTwo" },
            { "{chat.mention.user}", "@ViewerOne" },
            { "{chat.me:dances}", "/me dances" },
            // The trigger message.
            { "{chat.message}", "hello big world" },
            { "{chat.message.length}", "15" },
            { "{chat.message.noCommand}", "hello big world" },
            { "{chat.message.bits}", "0" },
            // Shared chat (the channel is not in a session).
            { "{chat.isShared}", "false" },
            { "{if:{chat.isShared}|shared chat|}", "" },
            { "{chat.shared.channels}", "" },
            // Chat modes and the pinned line.
            { "{chat.settings.slow}", "3s" },
            { "{chat.settings.followersOnly}", "10m" },
            { "{chat.settings.subOnly}", "false" },
            { "{if:{chat.settings.subOnly}|sub-only|open}", "open" },
            { "{if:{chat.settings.emoteOnly}|emote-only|}", "" },
            { "{chat.pinned}", "Welcome to the stream" },
            // Speech-ready text: no emotes, links or @; fancy letters folded; numbers spoken.
            {
                "{tts.plain:hi @ViewerTwo KEKW https://a.b \U0001D421\U0001D422}",
                "hi ViewerTwo hi"
            },
            { "{tts.plain.keepEmotes:hi KEKW}", "hi KEKW" },
            { "{tts.plain.keepEmotes:hi @ViewerTwo KEKW https://a.b}", "hi ViewerTwo KEKW" },
            { "{tts.numbers:I have 3 cats}", "I have three cats" },
            { "{tts.numbers:I have 21 cats}", "I have twenty-one cats" },
            { "{tts.numbers:it costs 3.5 euro}", "it costs 3.5 euro" },
            { "{tts.lexicon:gg}", "good game" },
            { "{tts.lexicon:gg everyone}", "good game everyone" },
            // Click-to-post links.
            {
                "{link.x:Watch {channel.url}}",
                "https://x.com/intent/post?text=Watch%20https%3A%2F%2Ftwitch.tv%2Fsamplechannel"
            },
            { "{link.bluesky:Live now}", "https://bsky.app/intent/compose?text=Live%20now" },
            { "{link.threads:Live now}", "https://www.threads.net/intent/post?text=Live%20now" },
            {
                "{link.mastodon:mastodon.social|Live now}",
                "https://mastodon.social/share?text=Live%20now"
            },
            // A nested token feeds an argument.
            { "{chat.mention:{target}}", "@ViewerTwo" },
            { "{chat.me:{text.upper:dances}}", "/me DANCES" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Every_row_renders_the_exact_output_of_the_fixture(
        string template,
        string expected
    )
    {
        await using ChatHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }
}
