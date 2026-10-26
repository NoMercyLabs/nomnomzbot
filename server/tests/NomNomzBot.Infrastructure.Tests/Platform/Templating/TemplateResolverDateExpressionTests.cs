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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves the <c>{date:&lt;source&gt;|&lt;step&gt;|…}</c> expression (commands-pipelines.md, date and time
/// expressions) renders the exact chat text. The clock is fixed at 2026-10-26T14:30:00Z, a Monday, which is
/// 15:30 in Europe/Amsterdam (CET, one day after the 2026 autumn clock change).
/// </summary>
public sealed class TemplateResolverDateExpressionTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000d002");
    private static readonly DateTimeOffset FixedUtc = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);

    private static (TemplateResolver Resolver, ListLogger Logger) Build(
        string? timezone = "Europe/Amsterdam",
        string? language = "en",
        DateTimeOffset? wentLiveAt = null
    )
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry
            .Get(Channel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Channel,
                    TwitchChannelId = "999",
                    ChannelName = "stoney_eagle",
                    Timezone = timezone,
                    Language = language,
                    IsLive = wentLiveAt is not null,
                    WentLiveAt = wentLiveAt,
                }
            );

        ListLogger logger = new();
        TemplateResolver resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            logger,
            new FixedTimeProvider(FixedUtc)
        );
        return (resolver, logger);
    }

    private static async Task<string> Render(
        string template,
        string? language = "en",
        Dictionary<string, string>? seeds = null,
        DateTimeOffset? wentLiveAt = null
    )
    {
        (TemplateResolver resolver, ListLogger _) = Build(
            language: language,
            wentLiveAt: wentLiveAt
        );
        return await resolver.ResolveAsync(template, seeds ?? [], Channel);
    }

    // ── Sources ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("{date:now|format:yyyy-MM-dd HH:mm}", "2026-10-26 15:30")]
    [InlineData("{date:utc|format:HH:mm}", "14:30")]
    [InlineData("{date:2026-12-25|format:dd/MM/yyyy HH:mm}", "25/12/2026 00:00")]
    [InlineData("{date:2026-12-25T20:15|format:HH:mm}", "20:15")]
    [InlineData("{date:2026-12-25T20:15:00Z|format:HH:mm}", "21:15")]
    [InlineData("{date:now|+1d}", "2026-10-27 15:30")]
    public async Task Each_source_renders_in_the_channel_zone(string template, string expected)
    {
        (await Render(template)).Should().Be(expected);
    }

    [Fact]
    public async Task A_variable_that_holds_a_timestamp_is_a_source()
    {
        string resolved = await Render(
            "{date:{stream.startedAt}|format:HH:mm} / {date:{followed_at}|format:d MMM yyyy}",
            seeds: new Dictionary<string, string> { ["followed_at"] = "2025-09-10T18:00:00Z" },
            wentLiveAt: new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero)
        );

        resolved.Should().Be("13:00 / 10 Sep 2025");
    }

    // ── Calculations ────────────────────────────────────────────────────

    [Theory]
    [InlineData("{date:now|+1y|format:yyyy-MM-dd}", "2027-10-26")]
    [InlineData("{date:now|-2mo|format:yyyy-MM-dd}", "2026-08-26")]
    [InlineData("{date:now|+3w|format:yyyy-MM-dd}", "2026-11-16")]
    [InlineData("{date:now|-4d|format:yyyy-MM-dd}", "2026-10-22")]
    [InlineData("{date:now|+5h|format:HH:mm:ss}", "20:30:00")]
    [InlineData("{date:now|-6m|format:HH:mm:ss}", "15:24:00")]
    [InlineData("{date:now|+7s|format:HH:mm:ss}", "15:30:07")]
    public async Task Each_unit_adds_or_subtracts(string template, string expected)
    {
        (await Render(template)).Should().Be(expected);
    }

    [Fact]
    public async Task Calculations_chain_left_to_right_in_one_step_and_across_steps()
    {
        string resolved = await Render(
            "{date:now|+1y-2mo|+3w|+4d|+5h-6m+7s|format:yyyy-MM-dd HH:mm:ss}"
        );

        resolved.Should().Be("2027-09-20 20:24:07");
    }

    [Theory]
    [InlineData("{date:2026-01-31|+1mo|format:yyyy-MM-dd}", "2026-02-28")]
    [InlineData("{date:2024-01-31|+1mo|format:yyyy-MM-dd}", "2024-02-29")]
    [InlineData("{date:2028-02-29|+1y|format:yyyy-MM-dd}", "2029-02-28")]
    [InlineData("{date:2028-02-29|-4y|format:yyyy-MM-dd}", "2024-02-29")]
    public async Task Month_end_and_leap_year_clamp_to_the_last_real_day(
        string template,
        string expected
    )
    {
        (await Render(template)).Should().Be(expected);
    }

    [Theory]
    // 2026-03-29 02:00 CET jumps to 03:00 CEST in Europe/Amsterdam.
    [InlineData(
        "{date:2026-03-28T12:00|+1d|format:yyyy-MM-dd HH:mm zzz}",
        "2026-03-29 12:00 +02:00"
    )]
    [InlineData(
        "{date:2026-03-28T12:00|+24h|format:yyyy-MM-dd HH:mm zzz}",
        "2026-03-29 13:00 +02:00"
    )]
    [InlineData("{date:2026-03-29T00:00|diff:2026-03-30T00:00:hours}", "23 hours")]
    public async Task A_day_keeps_the_wall_clock_and_hours_count_real_time_across_daylight_saving(
        string template,
        string expected
    )
    {
        (await Render(template)).Should().Be(expected);
    }

    [Theory]
    [InlineData("{date:now|start:day|format:yyyy-MM-dd HH:mm:ss}", "2026-10-26 00:00:00")]
    [InlineData("{date:now|end:day|format:yyyy-MM-dd HH:mm:ss}", "2026-10-26 23:59:59")]
    [InlineData("{date:2026-10-29|start:week|format:yyyy-MM-dd ddd}", "2026-10-26 Mon")]
    [InlineData("{date:now|end:week|format:yyyy-MM-dd HH:mm:ss}", "2026-11-01 23:59:59")]
    [InlineData("{date:now|start:month|format:yyyy-MM-dd HH:mm}", "2026-10-01 00:00")]
    [InlineData("{date:2028-02-10|end:month|format:yyyy-MM-dd HH:mm:ss}", "2028-02-29 23:59:59")]
    [InlineData("{date:now|start:year|format:yyyy-MM-dd HH:mm}", "2026-01-01 00:00")]
    [InlineData("{date:now|end:year|format:yyyy-MM-dd HH:mm:ss}", "2026-12-31 23:59:59")]
    public async Task Start_and_end_snap_to_the_unit_boundary(string template, string expected)
    {
        (await Render(template)).Should().Be(expected);
    }

    [Theory]
    [InlineData("{date:now|next:friday|format:yyyy-MM-dd HH:mm}", "2026-10-30 15:30")]
    [InlineData("{date:now|next:mon|format:yyyy-MM-dd}", "2026-11-02")]
    [InlineData("{date:now|prev:monday|format:yyyy-MM-dd}", "2026-10-19")]
    [InlineData("{date:now|prev:sat|start:day|format:yyyy-MM-dd HH:mm}", "2026-10-24 00:00")]
    public async Task Next_and_previous_weekday_are_strictly_after_or_before(
        string template,
        string expected
    )
    {
        (await Render(template)).Should().Be(expected);
    }

    // ── Differences ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("{date:2025-09-10|diff:2026-10-26:days}", "411 days")]
    [InlineData("{date:2025-09-10|diff:2026-10-26:weeks}", "58 weeks")]
    [InlineData("{date:2025-09-10|diff:2026-10-26:months}", "13 months")]
    [InlineData("{date:2025-09-10|diff:2026-10-26:years}", "1 year")]
    [InlineData("{date:2025-09-10|diff:2026-10-26:breakdown}", "1 year, 1 month, 16 days")]
    [InlineData("{date:2026-10-26|diff:2025-09-10:breakdown}", "1 year, 1 month, 16 days")]
    [InlineData("{date:2026-10-26T10:00|diff:2026-10-26T12:30:minutes}", "150 minutes")]
    public async Task Diff_gives_a_total_or_a_calendar_breakdown(string template, string expected)
    {
        (await Render(template)).Should().Be(expected);
    }

    [Theory]
    [InlineData("{date:2026-12-25|until:days}", "59 days")]
    [InlineData("{date:2026-12-25|until:breakdown}", "1 month, 28 days, 8 hours, 30 minutes")]
    [InlineData(
        "{date:2025-09-10T20:00|since:breakdown}",
        "1 year, 1 month, 15 days, 19 hours, 30 minutes"
    )]
    [InlineData("{date:now|-90m|since:minutes}", "90 minutes")]
    [InlineData("{date:now|+2h+15m|until}", "2 hours, 15 minutes")]
    public async Task Until_and_since_count_from_now(string template, string expected)
    {
        (await Render(template)).Should().Be(expected);
    }

    // ── Formatting ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("{date:now|-3d|relative}", "3 days ago")]
    [InlineData("{date:now|+2h|relative}", "2 hours from now")]
    [InlineData("{date:now|ordinal}", "26th")]
    [InlineData("{date:2026-10-01|ordinal}", "1st")]
    [InlineData("{date:now|format:dddd 'the' [o] 'of' MMMM}", "Monday the 26th of October")]
    [InlineData("{date:now|format:yyyy-MM-dd'T'HH:mm:ssK}", "2026-10-26T15:30:00+01:00")]
    [InlineData("{date:now|tz:America/New_York|format:HH:mm}", "10:30")]
    [InlineData("{date:now|tz:Asia/Tokyo|format:yyyy-MM-dd HH:mm}", "2026-10-26 23:30")]
    [InlineData("{date:now|format:s}", "2026-10-26T15:30:00")]
    [InlineData(
        "{date:now|next:friday|start:day|tz:America/New_York|format:ddd HH:mm}",
        "Thu 19:00"
    )]
    public async Task Each_output_form_renders_the_exact_text(string template, string expected)
    {
        (await Render(template)).Should().Be(expected);
    }

    [Theory]
    [InlineData("{date:now|format:dddd d MMMM yyyy}", "maandag 26 oktober 2026")]
    [InlineData("{date:now|format:D}", "maandag 26 oktober 2026")]
    [InlineData("{date:now|-3d|relative}", "3 dagen geleden")]
    [InlineData("{date:now|ordinal}", "26e")]
    [InlineData("{date:2025-09-10|diff:2026-10-26:breakdown}", "1 jaar, 1 maand, 16 dagen")]
    [InlineData("{date:now|lang:en|format:MMMM}", "October")]
    public async Task A_Dutch_channel_gets_Dutch_month_and_day_names(
        string template,
        string expected
    )
    {
        (await Render(template, language: "nl")).Should().Be(expected);
    }

    // ── Invalid expressions and the old helpers ─────────────────────────

    [Theory]
    [InlineData("{date:now|+1x}")]
    [InlineData("{date:not-a-date|format:yyyy}")]
    [InlineData("{date:now|tz:Mars/Olympus_Mons}")]
    [InlineData("{date:now|next:someday}")]
    [InlineData("{date:now|format:yyyy|+1d}")]
    [InlineData("{date:now|diff:2026-01-01:fortnights}")]
    public async Task An_invalid_expression_renders_as_written_and_logs_one_warning(string template)
    {
        (TemplateResolver resolver, ListLogger logger) = Build();

        string resolved = await resolver.ResolveAsync(
            $"before {template} after",
            new Dictionary<string, string>(),
            Channel
        );

        resolved.Should().Be($"before {template} after");
        logger.Entries.Count(e => e.Level == LogLevel.Warning).Should().Be(1);
    }

    [Fact]
    public async Task The_old_time_utc_and_date_helpers_are_unchanged()
    {
        (await Render("{time} | {time.utc} | {date}"))
            .Should()
            .Be("15:30:00 | 14:30:00 UTC | 2026-10-26");
    }

    [Fact]
    public void The_plain_resolve_path_renders_date_expressions_in_UTC_English()
    {
        (TemplateResolver resolver, ListLogger _) = Build();

        string resolved = resolver.Resolve(
            "{date:now|format:dddd HH:mm}",
            new Dictionary<string, string>()
        );

        resolved.Should().Be("Monday 14:30");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ListLogger : ILogger<TemplateResolver>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
