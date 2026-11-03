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

using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Commands.Jobs;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;
using Timer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// The surface matrix: every helper the registry declares for a surface is rendered through the real
/// <see cref="TemplateResolver"/>, with the seed variables the production caller of that surface builds, and
/// a token that comes back as written is a failed cell. A cell is (surface, registry key).
///
/// Seeds come from the production code where it can be called (<c>ChatMessageHandler.BuildInitialVariables</c>,
/// <c>TemplateSubject</c>, <c>TimerService.BuildVariables</c>, the Discord link keys). Where a caller cannot
/// be called from a test (the Discord go-live handler keeps its seed in a private method), the keys are read
/// from the production source and named in the comment of that surface. Fakes stand in only for the outside
/// world (Twitch, music, the network); the database is in memory and the clock is fixed.
///
/// A helper that takes arguments has no argument example in the registry, so its token is tried bare and then
/// with one, two and three placeholder arguments; the cell passes when any shape renders. A key that is not
/// valid on a surface fails every shape.
///
/// Two lists keep the gate honest. <see cref="DataAbsent"/> holds the cells that cannot render because the
/// surface has no source for the data. <see cref="KnownGap"/> holds the cells that are failures today: each is
/// work for a later slice, and the test fails when a row starts to render so the row is deleted with the fix.
/// </summary>
public sealed class TemplateSurfaceMatrixTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000f002");
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The argument shapes tried for a token: bare, then every mix of text ("a") and number ("2") for one to
    /// three arguments. The registry carries no argument example, and helpers differ in which position takes
    /// the number (<c>text.repeat:n|t</c>, <c>text.truncate:t|n</c>).
    /// </summary>
    private static readonly string[] ArgumentShapes =
    [
        "",
        .. new[] { ":a", ":2" },
        .. new[] { "a", "2" }.SelectMany(x => new[] { "a", "2" }.Select(y => $":{x}|{y}")),
        .. new[] { "a", "2" }
            .SelectMany(x => new[] { "a", "2" }.Select(y => (x, y)))
            .SelectMany(p => new[] { "a", "2" }.Select(z => $":{p.x}|{p.y}|{z}")),
    ];

    /// <summary>The cells that have no source for the data on that surface. Every row names why.</summary>
    private static readonly (
        TemplateHelperContext Surface,
        string Key,
        string Reason
    )[] DataAbsent =
    [
        (
            TemplateHelperContext.Discord,
            "user",
            "The go-live post has no viewer; a follow-style trigger seeds it through DiscordTriggerNotificationHandler.ViewerData."
        ),
        (
            TemplateHelperContext.Discord,
            "user.name",
            "The go-live post has no viewer; a follow-style trigger seeds it through DiscordTriggerNotificationHandler.ViewerData."
        ),
        (
            TemplateHelperContext.Discord,
            "user.id",
            "The go-live post has no viewer; a follow-style trigger seeds it through DiscordTriggerNotificationHandler.ViewerData."
        ),
    ];

    /// <summary>
    /// Cells that fail today, one row per failing cell, grouped by cause. A later slice deletes the rows with
    /// its fix. The test fails when a row renders, so a row cannot go stale.
    /// </summary>
    private static readonly (TemplateHelperContext Surface, string Key)[] KnownGap =
    [
        // Cause: the command seed has no target.name / target.id (only target); an unknown viewer has no lookup.
        (TemplateHelperContext.Command, "target.name"),
        (TemplateHelperContext.Command, "target.id"),
        (TemplateHelperContext.Pipeline, "target.name"),
        (TemplateHelperContext.Pipeline, "target.id"),
        // Cause: declared on EventResponse but the shared alert seed never carries it; only some events do.
        (TemplateHelperContext.EventResponse, "user.provider"),
        (TemplateHelperContext.EventResponse, "platform.user"),
        (TemplateHelperContext.EventResponse, "platform.user.name"),
        (TemplateHelperContext.EventResponse, "platform.user.icon"),
        (TemplateHelperContext.EventResponse, "chat.message"),
        (TemplateHelperContext.EventResponse, "chat.message.length"),
        (TemplateHelperContext.EventResponse, "chat.message.noCommand"),
        (TemplateHelperContext.EventResponse, "chat.message.bits"),
        // Cause: a webhook body gets the same alert seed as an event response, which never carries these.
        (TemplateHelperContext.Webhook, "user.provider"),
        (TemplateHelperContext.Webhook, "platform.user"),
        (TemplateHelperContext.Webhook, "platform.user.name"),
        (TemplateHelperContext.Webhook, "platform.user.icon"),
        (TemplateHelperContext.Webhook, "chat.message"),
        (TemplateHelperContext.Webhook, "chat.message.length"),
        (TemplateHelperContext.Webhook, "chat.message.noCommand"),
        (TemplateHelperContext.Webhook, "chat.message.bits"),
        // Cause: declared on Timer but a timer fire seeds only timer.name and timer.message.
        (TemplateHelperContext.Timer, "platform.user"),
        (TemplateHelperContext.Timer, "platform.user.name"),
        (TemplateHelperContext.Timer, "platform.user.icon"),
        (TemplateHelperContext.Timer, "music.queue.target.count"),
        (TemplateHelperContext.Timer, "chat.mention.user"),
        (TemplateHelperContext.Timer, "chat.message"),
        (TemplateHelperContext.Timer, "chat.message.length"),
        (TemplateHelperContext.Timer, "chat.message.noCommand"),
        (TemplateHelperContext.Timer, "chat.message.bits"),
        // Cause: declared on Discord but the go-live seed and links carry no viewer or chat data.
        (TemplateHelperContext.Discord, "platform.user"),
        (TemplateHelperContext.Discord, "platform.user.name"),
        (TemplateHelperContext.Discord, "platform.user.icon"),
        (TemplateHelperContext.Discord, "music.queue.target.count"),
        (TemplateHelperContext.Discord, "chat.mention.user"),
        (TemplateHelperContext.Discord, "chat.message"),
        (TemplateHelperContext.Discord, "chat.message.length"),
        (TemplateHelperContext.Discord, "chat.message.noCommand"),
        (TemplateHelperContext.Discord, "chat.message.bits"),
    ];

    private enum CellState
    {
        Rendered,
        EmptyByData,
        AsWritten,
        Threw,
    }

    private sealed record Cell(
        TemplateHelperContext Surface,
        string Key,
        CellState State,
        string Detail = ""
    );

    private static bool Failed(Cell cell) => cell.State is CellState.AsWritten or CellState.Threw;

    [Fact]
    public async Task Every_declared_helper_renders_on_the_surface_that_declares_it()
    {
        await using ServiceProvider services = Services();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        TemplateResolver resolver = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(Now),
            registry
        );

        List<Cell> cells = [];
        foreach (TemplateHelperContext surface in Enum.GetValues<TemplateHelperContext>())
        foreach (
            TemplateHelperEntry entry in registry
                .ForContext(surface)
                .Where(e => e.Prefix is null && !e.EventScoped)
        )
            cells.Add(await RenderCellAsync(resolver, surface, entry.Key));

        // A key that fails on every surface that declares it points at the fixture (a fake service holds no
        // data, or the argument shape is unknown), not at one surface. Only a key that renders somewhere
        // and fails elsewhere is a surface gap: a missing seed or a missing context declaration.
        HashSet<string> workingKeys = [.. cells.Where(c => !Failed(c)).Select(c => c.Key)];
        List<Cell> universal = [.. cells.Where(c => Failed(c) && !workingKeys.Contains(c.Key))];

        List<string> unexplained = [];
        foreach (Cell cell in cells.Where(c => Failed(c) && workingKeys.Contains(c.Key)))
        {
            bool absent = DataAbsent.Any(a => a.Surface == cell.Surface && a.Key == cell.Key);
            bool known = KnownGap.Any(g => g.Surface == cell.Surface && g.Key == cell.Key);
            if (!absent && !known)
                unexplained.Add($"{cell.Surface}\t{cell.Key}\t{cell.State}\t{cell.Detail}");
        }

        List<string> stale =
        [
            .. KnownGap
                .Where(g => cells.Any(c => c.Surface == g.Surface && c.Key == g.Key && !Failed(c)))
                .Select(g => $"{g.Surface}\t{g.Key}"),
            .. DataAbsent
                .Where(a => cells.Any(c => c.Surface == a.Surface && c.Key == a.Key && !Failed(c)))
                .Select(a => $"{a.Surface}\t{a.Key} (DataAbsent)"),
        ];

        StringBuilder report = new();
        report.AppendLine(
            $"cells {cells.Count}: rendered {cells.Count(c => c.State == CellState.Rendered)}, "
                + $"empty-by-data {cells.Count(c => c.State == CellState.EmptyByData)}, "
                + $"as-written {cells.Count(c => c.State == CellState.AsWritten)}, "
                + $"threw {cells.Count(c => c.State == CellState.Threw)}"
        );
        report.AppendLine(
            $"fixture-limited (fails on every surface, not asserted): {universal.Count} cells, "
                + $"{universal.Select(c => c.Key).Distinct().Count()} keys"
        );
        foreach (Cell cell in universal)
            report.AppendLine($"FIXTURE\t{cell.Surface}\t{cell.Key}\t{cell.State}\t{cell.Detail}");
        report.AppendLine("FAILED and not allowlisted (surface, key, state, detail):");
        foreach (string row in unexplained)
            report.AppendLine(row);
        report.AppendLine("KnownGap rows that now render (delete them):");
        foreach (string row in stale)
            report.AppendLine(row);

        (unexplained.Count + stale.Count).Should().Be(0, report.ToString());
    }

    private static async Task<Cell> RenderCellAsync(
        TemplateResolver resolver,
        TemplateHelperContext surface,
        string key
    )
    {
        CellState best = CellState.AsWritten;
        string thrown = string.Empty;
        foreach (string shape in ArgumentShapes)
        {
            string token = "{" + key + shape + "}";
            string rendered;
            try
            {
                rendered = await RenderOnAsync(resolver, surface, token);
            }
            catch (Exception ex)
            {
                thrown = ex.GetType().Name + ": " + ex.Message;
                continue;
            }

            if (rendered == token)
                continue;
            if (!string.IsNullOrWhiteSpace(rendered))
                return new(surface, key, CellState.Rendered);
            best = CellState.EmptyByData;
        }

        return best == CellState.AsWritten && thrown.Length > 0
            ? new(surface, key, CellState.Threw, thrown)
            : new(surface, key, best);
    }

    private static async Task<string> RenderOnAsync(
        TemplateResolver resolver,
        TemplateHelperContext surface,
        string template
    )
    {
        Dictionary<string, string> seeds = SeedsFor(surface);
        return surface switch
        {
            TemplateHelperContext.Viewer => (
                await resolver.ResolveViewerAsync(
                    template,
                    seeds,
                    Channel,
                    ViewerTemplateLimits.WithOutputCap(500)
                )
            ).Text,
            TemplateHelperContext.Webhook => await new WebhookBodyTemplateRenderer(
                resolver
            ).RenderAsync(template, seeds, false, Channel),
            _ => await resolver.ResolveAsync(template, seeds, Channel),
        };
    }

    /// <summary>The seed variables the production caller of one surface builds.</summary>
    private static Dictionary<string, string> SeedsFor(TemplateHelperContext surface)
    {
        switch (surface)
        {
            case TemplateHelperContext.Command:
            case TemplateHelperContext.Pipeline:
                // A pipeline starts from the trigger that ran it; the chat command is the common trigger.
                return ChatMessageHandler.BuildInitialVariables(
                    new ChatMessageReceivedEvent
                    {
                        BroadcasterId = Channel,
                        MessageId = "sample-message",
                        TwitchBroadcasterId = "100000001",
                        UserId = "100000042",
                        UserDisplayName = "SampleViewer",
                        UserLogin = "sampleviewer",
                        Message = "!hug samplefriend second",
                        Fragments = [],
                        Badges = [],
                        IsSubscriber = false,
                        IsVip = false,
                        IsModerator = false,
                        IsBroadcaster = false,
                        OccurredAt = Now,
                    },
                    "samplefriend second"
                );

            case TemplateHelperContext.Timer:
                return TimerService.BuildVariables(
                    new Timer { Name = "sample timer", Messages = ["hello chat"] }
                );

            case TemplateHelperContext.Discord:
                // DiscordGoLiveNotificationHandler seeds these keys in a private method (not callable from a
                // test); DiscordChannelLinks adds the five link keys for every trigger.
                return new(StringComparer.OrdinalIgnoreCase)
                {
                    ["broadcaster"] = "SampleStreamer",
                    ["channel.name"] = "SampleStreamer",
                    ["channel.title"] = "Cozy coding night",
                    ["channel.game"] = "Just Chatting",
                    ["title"] = "Cozy coding night",
                    ["game"] = "Just Chatting",
                    [DiscordChannelLinks.ChannelUrl] = "https://www.twitch.tv/samplestreamer",
                    [DiscordChannelLinks.StreamUrl] = "https://www.twitch.tv/samplestreamer",
                    [DiscordChannelLinks.ChannelLogin] = "samplestreamer",
                    [DiscordChannelLinks.ChannelAvatar] = string.Empty,
                    [DiscordChannelLinks.StreamThumbnail] = "https://static.example/thumb.jpg",
                };

            case TemplateHelperContext.Viewer:
            {
                // ViewerInputTemplateService seeds the viewer identity and nothing else.
                Dictionary<string, string> viewer = new(StringComparer.OrdinalIgnoreCase);
                TemplateSubject.SeedUser(viewer, "100000042", "sampleviewer", "SampleViewer");
                return viewer;
            }

            default:
            {
                // EventResponse and Webhook: the alert handlers add the event's own keys, then the viewer
                // identity and the target copy of it (TwitchAlertHandlerBase.ComposeVariables).
                Dictionary<string, string> alert = new(StringComparer.OrdinalIgnoreCase);
                TemplateSubject.SeedUser(alert, "100000042", "sampleviewer", "SampleViewer");
                TemplateSubject.SeedTargetFromUser(alert);
                return alert;
            }
        }
    }

    /// <summary>
    /// The real helper registry and every real provider, with an NSubstitute fake for each outside-world
    /// service a provider asks for and an in-memory database.
    /// </summary>
    private static ServiceProvider Services()
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels
            .Get(Channel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Channel,
                    TwitchChannelId = "555001",
                    ChannelName = "samplestreamer",
                    DisplayName = "SampleStreamer",
                    IsLive = true,
                    CurrentStreamId = "stream-1",
                    WentLiveAt = Now.AddHours(-2),
                    Timezone = "Europe/Amsterdam",
                    Language = "en",
                }
            );

        ServiceCollection services = new();
        services.AddSingleton(channels);
        services.AddSingleton<IApplicationDbContext>(ViewerDataTestDbContext.New());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddSingleton(new Random(1));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddTemplateHelpers(typeof(TemplateResolver).Assembly);
        FakeOutsideWorldServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Registers a lazy fake for every interface of the Application, Domain and Infrastructure assemblies that
    /// the container does not hold yet: a provider takes some services in its constructor and others from a
    /// scope while it renders. The providers are real; only the services behind them are fakes.
    /// </summary>
    private static void FakeOutsideWorldServices(ServiceCollection services)
    {
        HashSet<Type> known = [.. services.Select(d => d.ServiceType)];
        Assembly[] assemblies =
        [
            typeof(ITemplateResolver).Assembly,
            typeof(IChannelRegistry).Assembly,
            typeof(TemplateResolver).Assembly,
        ];
        foreach (
            Type type in assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsInterface && !t.IsGenericType && !known.Contains(t))
        )
            services.AddSingleton(
                type,
                _ => type.IsVisible ? Substitute.For([type], []) : EmptyServiceFake.Create(type)
            );
    }
}
