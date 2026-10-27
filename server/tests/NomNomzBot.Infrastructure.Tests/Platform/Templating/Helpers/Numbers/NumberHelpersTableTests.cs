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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Numbers;

/// <summary>
/// Catalog section 7.4, <c>math</c>, <c>num</c> and <c>meter</c>: every Example renders to exactly its Output
/// cell through the real parser, registry and resolver. Bad arguments leave the token as written.
/// </summary>
public sealed class NumberHelpersTableTests
{
    private static readonly Guid DutchChannel = Guid.Parse("00000000-0000-0000-0000-0000000000d1");

    private static readonly Dictionary<string, string> Seeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["event.bits"] = "250",
        ["count.deaths"] = "12",
    };

    public static TheoryData<string, string> Rows =>
        new()
        {
            // math expression
            { "{math:{event.bits}/100}", "2.5" },
            { "{math:2+3*4}", "14" },
            { "{math:(2+3)*4}", "20" },
            { "{math:2^10}", "1024" },
            { "{math:17%5}", "2" },
            { "{math:{count.deaths}*2}", "24" },
            // math functions
            { "{math.sqrt:81}", "9" },
            { "{math.abs:-4}", "4" },
            { "{math.floor:2.7}", "2" },
            { "{math.ceil:2.1}", "3" },
            { "{math.pow:3|3}", "27" },
            { "{math.log:100|10}", "2" },
            { "{math.add:2|3}", "5" },
            { "{math.sub:5|3}", "2" },
            { "{math.mul:4|3}", "12" },
            { "{math.div:7|2}", "3.5" },
            { "{math.div.int:7|2}", "3" },
            { "{math.mod:7|2}", "1" },
            { "{math.sum:1|2|3}", "6" },
            { "{math.avg:1|2|6}", "3" },
            { "{math.median:1|9|3}", "3" },
            // num: bounds and rounding
            { "{num.min:4|9|2}", "2" },
            { "{num.max:4|9|2}", "9" },
            { "{num.clamp:120|0|100}", "100" },
            { "{num.round:2.456|2}", "2.46" },
            { "{num.round:2.5}", "3" },
            { "{num.truncate:2.9}", "2" },
            // num: percent and ratio
            { "{num.percent:1234|1500}", "82%" },
            { "{num.percent:1234|1500|1}", "82.3%" },
            { "{num.percentOf:10|250}", "25" },
            { "{num.ratio:3|12}", "1:4" },
            { "{num.diff:2800|2500}", "300" },
            // num: formatting (English channel)
            { "{num.format:1234}", "1,234" },
            { "{num.format:1234.5|2}", "1,234.50" },
            { "{num.compact:1234}", "1.2k" },
            { "{num.compact:3400000}", "3.4M" },
            { "{num.compact:2100000000}", "2.1B" },
            // num: words, ordinals, roman numerals
            { "{num.ordinal:3}", "3rd" },
            { "{num.ordinal.words:3}", "third" },
            { "{num.words:42}", "forty-two" },
            { "{num.roman:2026}", "MMXXVI" },
            { "{num.fromRoman:XIV}", "14" },
            // num: money, size, time
            { "{num.currency:5|USD}", "$5.00" },
            { "{num.currency.compact:1500|USD}", "$1.5k" },
            { "{num.bytes:1536}", "1.5 KB" },
            { "{num.duration:8100}", "2h 15m" },
            { "{num.duration.long:8100}", "2 hours, 15 minutes" },
            { "{num.duration.clock:83}", "1:23" },
            // num: small helpers
            { "{num.sign:5}", "+5" },
            { "{num.abs:-5}", "5" },
            { "{num.parity:7}", "odd" },
            // The catalog examples of isEven and between feed {if:...}, which is lane L2c: the boolean text is proven here.
            { "{num.between:5|1|10}", "true" },
            { "{num.parse:1.2k}", "1200" },
            { "{num.digits:12345}", "5" },
            { "{num.digitSum:123}", "6" },
            { "{num.inc:4}", "5" },
            { "{num.dec:4}", "3" },
            { "{num.hex:255}", "FF" },
            { "{num.binary:5}", "101" },
            // num: unit conversion
            { "{num.convert:10|km|mi}", "6.21" },
            { "{num.convert:20|c|f}", "68" },
            { "{num.convert:2|kg|lb}", "4.41" },
            { "{num.convert:180|cm|in}", "70.87" },
            { "{num.convert:180|cm|ft}", "5'11\"" },
            // bars
            { "{num.bar:6|10}", "██████░░░░" },
            { "{meter:60}", "██████░░░░ 60%" },
            { "{meter:60|5}", "███░░ 60%" },
            { "{meter:60|5|🟩|⬜}", "🟩🟩🟩⬜⬜ 60%" },
            { "{meter.plain:60}", "██████░░░░" },
            { "{meter.hearts:60}", "❤️❤️❤️🖤🖤" },
            { "{meter.stars:3|5}", "★★★☆☆" },
            // expression edge cases
            { "{math:2^3^2}", "512" },
            { "{math:-2^2}", "-4" },
            { "{math:2^-1}", "0.5" },
            { "{math:sqrt(16)+pow(2,3)}", "12" },
            { "{math:abs(-3)*floor(2.9)}", "6" },
            { "{math:ceil(1.1)+log(100,10)}", "4" },
            { "{math: 1 + 2 }", "3" },
            { "{math:10/4}", "2.5" },
            { "{math:1/3}", "0.3333333333" },
            { "{math:-7%3}", "-1" },
            { "{math:--3}", "3" },
            // more number rows
            { "{math.median:1|2|3|4}", "2.5" },
            { "{math.log:8|2}", "3" },
            { "{math.sum:5}", "5" },
            { "{num.round:-2.5}", "-3" },
            { "{num.round:2.4}", "2" },
            { "{num.min:4}", "4" },
            { "{num.percent:1|3|2}", "33.33%" },
            { "{num.ratio:6|4}", "3:2" },
            { "{num.ratio:0|5}", "0:1" },
            { "{num.diff:2500|2800}", "300" },
            { "{num.format:1234.5}", "1,234.5" },
            { "{num.format:1234567|0}", "1,234,567" },
            { "{num.compact:999}", "999" },
            { "{num.compact:999999}", "1M" },
            { "{num.compact:-1500}", "-1.5k" },
            { "{num.compact:1000}", "1k" },
            { "{num.ordinal:11}", "11th" },
            { "{num.ordinal:22}", "22nd" },
            { "{num.roman:4}", "IV" },
            { "{num.fromRoman:mmxxvi}", "2026" },
            { "{num.currency:1234.5|USD}", "$1,234.50" },
            { "{num.currency:5|EUR}", "€5.00" },
            { "{num.currency.compact:-1500|USD}", "-$1.5k" },
            { "{num.bytes:512}", "512 B" },
            { "{num.bytes:1048576}", "1 MB" },
            { "{num.duration:45}", "45s" },
            { "{num.duration:90}", "1m 30s" },
            { "{num.duration.long:90}", "1 minute, 30 seconds" },
            { "{num.duration.clock:3700}", "1:01:40" },
            { "{num.duration.clock:5}", "0:05" },
            { "{num.sign:-3}", "-3" },
            { "{num.sign:0}", "0" },
            { "{num.parity:8}", "even" },
            { "{num.isEven:7}", "false" },
            { "{num.isEven:8}", "true" },
            { "{num.between:11|1|10}", "false" },
            { "{num.between:10|10|1}", "true" },
            { "{num.parse:2.5M}", "2500000" },
            { "{num.parse:3B}", "3000000000" },
            { "{num.parse:42}", "42" },
            { "{num.digits:-120}", "3" },
            { "{num.digitSum:-99}", "18" },
            { "{num.hex:0}", "0" },
            { "{num.hex:-255}", "-FF" },
            { "{num.binary:8}", "1000" },
            { "{num.convert:1|mi|km}", "1.61" },
            { "{num.convert:32|f|c}", "0" },
            { "{num.convert:0|c|k}", "273.15" },
            { "{num.convert:1|lb|oz}", "16" },
            { "{num.convert:1|gal|l}", "3.79" },
            { "{num.convert:182.9|cm|ft}", "6'0\"" },
            { "{num.bar:10|10}", "██████████" },
            { "{num.bar:0|10}", "░░░░░░░░░░" },
            { "{meter:150}", "██████████ 100%" },
            { "{meter:-5}", "░░░░░░░░░░ 0%" },
            { "{meter:0}", "░░░░░░░░░░ 0%" },
            { "{meter:33.5|10}", "███░░░░░░░ 33.5%" },
            { "{meter.plain:100|4}", "████" },
            { "{meter.hearts:100}", "❤️❤️❤️❤️❤️" },
            { "{meter.stars:9|5}", "★★★★★" },
            { "{meter.stars:0|3}", "☆☆☆" },
            // bad arguments leave the token as written
            { "{num.round:abc}", "{num.round:abc}" },
            { "{num.round:1|-1}", "{num.round:1|-1}" },
            { "{num.round:}", "{num.round:}" },
            { "{math.div:1|0}", "{math.div:1|0}" },
            { "{math.div.int:1|0}", "{math.div.int:1|0}" },
            { "{math.mod:1|0}", "{math.mod:1|0}" },
            { "{math:1/0}", "{math:1/0}" },
            { "{math:1%0}", "{math:1%0}" },
            { "{math:2+}", "{math:2+}" },
            { "{math:(1+2}", "{math:(1+2}" },
            { "{math:1+2)}", "{math:1+2)}" },
            { "{math:2 3}", "{math:2 3}" },
            { "{math:foo(1)}", "{math:foo(1)}" },
            { "{math:x+1}", "{math:x+1}" },
            { "{math:sqrt(-4)}", "{math:sqrt(-4)}" },
            { "{math:}", "{math:}" },
            { "{math.sqrt:-4}", "{math.sqrt:-4}" },
            { "{math.log:0}", "{math.log:0}" },
            { "{math.log:8|1}", "{math.log:8|1}" },
            { "{math.sum:1|x}", "{math.sum:1|x}" },
            { "{math.add:1}", "{math.add:1}" },
            { "{num.currency:5}", "{num.currency:5}" },
            { "{num.currency:5|ZZZ}", "{num.currency:5|ZZZ}" },
            { "{num.currency:5|us}", "{num.currency:5|us}" },
            { "{num.roman:0}", "{num.roman:0}" },
            { "{num.roman:4000}", "{num.roman:4000}" },
            { "{num.roman:2.5}", "{num.roman:2.5}" },
            { "{num.fromRoman:ABC}", "{num.fromRoman:ABC}" },
            { "{num.ordinal:1.5}", "{num.ordinal:1.5}" },
            { "{num.convert:1|km|kg}", "{num.convert:1|km|kg}" },
            { "{num.convert:1|km|zz}", "{num.convert:1|km|zz}" },
            { "{num.convert:1|km}", "{num.convert:1|km}" },
            { "{num.percent:1|0}", "{num.percent:1|0}" },
            { "{num.ratio:2.5|5}", "{num.ratio:2.5|5}" },
            { "{num.ratio:0|0}", "{num.ratio:0|0}" },
            { "{num.clamp:5|10|0}", "{num.clamp:5|10|0}" },
            { "{num.parse:abc}", "{num.parse:abc}" },
            { "{num.parse:1.2x}", "{num.parse:1.2x}" },
            { "{num.parity:1.5}", "{num.parity:1.5}" },
            { "{num.duration:-5}", "{num.duration:-5}" },
            { "{num.duration.clock:abc}", "{num.duration.clock:abc}" },
            { "{num.bar:1|0}", "{num.bar:1|0}" },
            { "{meter:abc}", "{meter:abc}" },
            { "{meter:50|0}", "{meter:50|0}" },
            { "{meter:50|500}", "{meter:50|500}" },
            { "{meter.stars:1|0}", "{meter.stars:1|0}" },
            { "{meter.stars:1|99}", "{meter.stars:1|99}" },
            { "{num.bytes:-1}", "{num.bytes:-1}" },
            { "{num.words:1.5}", "{num.words:1.5}" },
        };

    public static TheoryData<string, string> DutchRows =>
        new()
        {
            { "{num.format:1234}", "1.234" },
            { "{num.ordinal:3}", "3e" },
            { "{num.words:42}", "tweeënveertig" },
            { "{num.parity:7}", "oneven" },
            { "{num.parity:8}", "even" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_example_renders_to_exactly_the_output_cell(
        string template,
        string expected
    )
    {
        string rendered = await RenderAsync(template, channel: null);

        rendered.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(DutchRows))]
    public async Task A_dutch_channel_renders_number_words_and_formats_in_dutch(
        string template,
        string expected
    )
    {
        string rendered = await RenderAsync(template, DutchChannel);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task An_expression_longer_than_the_limit_stays_as_written()
    {
        string template = "{math:" + string.Join('+', Enumerable.Repeat("1", 150)) + "}";

        string rendered = await RenderAsync(template, channel: null);

        rendered.Should().Be(template);
    }

    [Fact]
    public async Task An_expression_nested_deeper_than_the_limit_stays_as_written()
    {
        string template = "{math:" + new string('(', 100) + "1" + new string(')', 100) + "}";

        string rendered = await RenderAsync(template, channel: null);

        rendered.Should().Be(template);
    }

    [Fact]
    public async Task An_expression_that_overflows_the_number_range_stays_as_written()
    {
        string template = "{math:99999999999999999999*99999999999999999999*99999999999}";

        string rendered = await RenderAsync(template, channel: null);

        rendered.Should().Be(template);
    }

    internal static async Task<string> RenderAsync(string template, Guid? channel)
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        return await BuildResolver(services.GetRequiredService<ITemplateHelperRegistry>())
            .ResolveAsync(template, Seeds, channel);
    }

    internal static TemplateResolver BuildResolver(ITemplateHelperRegistry registry)
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels
            .Get(DutchChannel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = DutchChannel,
                    TwitchChannelId = "123456",
                    ChannelName = "samplechannel",
                    Language = "nl",
                }
            );
        return new TemplateResolver(
            new ServiceCollection()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            channels,
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero)),
            registry
        );
    }
}
