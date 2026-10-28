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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Lookups;

public sealed class LookupHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // Twitch users, streams and games (ViewerTwo is offline and played Minecraft; samplechannel is live).
            { "{lookup.twitch:viewertwo.game}", "Minecraft" },
            { "{lookup.twitch:viewertwo.title}", "Building castles" },
            { "{if:{lookup.twitch:viewertwo.isLive}|live|offline}", "offline" },
            { "{lookup.twitch:samplechannel.isLive}", "true" },
            { "{lookup.twitch:viewertwo.viewers}", "" },
            { "{lookup.twitch:samplechannel.viewers}", "42" },
            { "{lookup.twitch:samplechannel.followers}", "1,234" },
            { "{lookup.twitch:viewertwo.followers}", "{lookup.twitch:viewertwo.followers}" },
            { "{lookup.twitch:viewertwo.display}", "ViewerTwo" },
            {
                "{lookup.twitch:viewertwo.avatar}",
                "https://static-cdn.jtvnw.net/jtv_user_pictures/viewertwo.png"
            },
            { "{lookup.twitch:viewertwo.createdAt}", "2019-05-02" },
            { "{lookup.twitch:viewertwo.uptime}", "" },
            { "{lookup.twitch:samplechannel.uptime}", "2h 15m" },
            { "{lookup.twitch:viewertwo.lastGame}", "Minecraft" },
            { "{lookup.twitch:viewertwo.url}", "https://twitch.tv/viewertwo" },
            { "{lookup.twitch:@ViewerTwo.url}", "https://twitch.tv/viewertwo" },
            { "{lookup.twitch:viewertwo.followage:samplechannel}", "9 months" },
            {
                "{lookup.twitch:viewertwo.followage:otherchannel}",
                "{lookup.twitch:viewertwo.followage:otherchannel}"
            },
            { "{lookup.twitch:no-such.thing.game}", "{lookup.twitch:no-such.thing.game}" },
            {
                "{lookup.game:Minecraft.boxart}",
                "https://static-cdn.jtvnw.net/ttv-boxart/27471-285x380.jpg"
            },
            { "{lookup.game.top}", "Just Chatting" },
            // Weather (Amsterdam: 12.2 C, feels 9.8 C, code 61, wind 18.4 km/h from 225 degrees, 81%).
            { "{lookup.weather:Amsterdam}", "Amsterdam: 12 °C, light rain" },
            { "{lookup.weather:Amsterdam.temp}", "12 °C" },
            { "{lookup.weather:Amsterdam.temp.f}", "54 °F" },
            { "{lookup.weather:Amsterdam.feels}", "10 °C" },
            { "{lookup.weather:Amsterdam.condition}", "light rain" },
            { "{lookup.weather:Amsterdam.emoji}", "🌧️" },
            { "{lookup.weather:Amsterdam.wind}", "18 km/h SW" },
            { "{lookup.weather:Amsterdam.humidity}", "81%" },
            { "{lookup.weather:Amsterdam.tomorrow}", "9–14 °C, showers" },
            // Local time (the clock is 14:30Z on Monday 2026-10-26; the channel zone is CET, +1h).
            { "{lookup.time:Tokyo}", "23:30" },
            { "{lookup.time:Tokyo.date}", "Monday 26 October" },
            { "{lookup.time:Tokyo.offset}", "+8h from here" },
            { "{lookup.time:Amsterdam.offset}", "same time as here" },
            // Currency (Frankfurter EUR to USD: 1.162 on 2026-10-26).
            { "{lookup.currency:10|EUR|USD}", "10 EUR = 11.62 USD" },
            { "{lookup.currency:2.5|eur|usd}", "2.5 EUR = 2.91 USD" },
            { "{lookup.currency.rate:EUR|USD}", "1.162" },
            { "{lookup.currency.date}", "2026-10-26" },
            { "{lookup.currency.date:EUR|USD}", "2026-10-26" },
            // Translation (MyMemory).
            { "{lookup.translate:nl|good morning}", "goedemorgen" },
            { "{lookup.translate:de|en|guten Morgen}", "good morning" },
            // A bad argument leaves the token as written.
            { "{lookup.currency:abc|EUR|USD}", "{lookup.currency:abc|EUR|USD}" },
            { "{lookup.currency:10|EURO|USD}", "{lookup.currency:10|EURO|USD}" },
            { "{lookup.currency:-5|EUR|USD}", "{lookup.currency:-5|EUR|USD}" },
            { "{lookup.translate:nl}", "{lookup.translate:nl}" },
            { "{lookup.translate:nl!|hi}", "{lookup.translate:nl!|hi}" },
            { "{lookup.weather:}", "{lookup.weather:}" },
            { "{lookup.nope:x}", "{lookup.nope:x}" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Every_row_renders_the_exact_output_of_the_catalog_fixture(
        string template,
        string expected
    )
    {
        await using LookupHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task The_chat_translation_reads_the_command_text_from_the_args_seed()
    {
        await using LookupHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{lookup.translate.chat:nl}",
            new Dictionary<string, string> { ["args"] = "hello world" }
        );

        rendered.Should().Be("hallo wereld");
    }

    [Fact]
    public async Task The_chat_translation_without_a_trigger_text_stays_as_written()
    {
        await using LookupHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{lookup.translate.chat:nl}");

        rendered.Should().Be("{lookup.translate.chat:nl}");
    }
}
