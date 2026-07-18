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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Behavior tests for the PUBLIC overlay reads (manifest + bundle). They prove that only enabled, successfully-built
/// widgets are exposed to a browser source; that the trust tier is derived (fail-closed) from the widget's source;
/// and that the served bundle is the widget's active compiled content — all resolved by overlay token with the
/// tenant query filter bypassed (the caller is anonymous), on real SQLite.
/// </summary>
public sealed class WidgetServiceOverlayTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static WidgetService NewService(WidgetTestDbContext db, IWidgetBuildService build) =>
        new(db, EmptyConfig, Substitute.For<IEventBus>(), build, Clock);

    private static IWidgetBuildService BuildReturning(string bundle, string hash)
    {
        IWidgetBuildService build = Substitute.For<IWidgetBuildService>();
        build
            .BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WidgetBuildOutput(bundle, hash, "")));
        return build;
    }

    private static async Task SeedChannelAsync(WidgetSqliteTestDatabase database, Guid channelId)
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = "tok",
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedWidgetAsync(
        WidgetSqliteTestDatabase database,
        Guid channelId,
        Guid widgetId,
        string framework = "vanilla",
        string source = "custom",
        bool enabled = true
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Widgets.Add(
            new Widget
            {
                Id = widgetId,
                BroadcasterId = channelId,
                Name = "Alerts",
                Framework = framework,
                Source = source,
                IsEnabled = enabled,
                Settings = new() { ["accent"] = "#fff" },
                EventSubscriptions = ["follow"],
            }
        );
        await db.SaveChangesAsync();
    }

    /// <summary>Compiles the widget once so it has an active, successfully-built version.</summary>
    private static async Task ActivateAsync(
        WidgetSqliteTestDatabase database,
        Guid channelId,
        Guid widgetId,
        string bundle = "BUNDLE",
        string hash = "hash123"
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, BuildReturning(bundle, hash));
        Result<WidgetVersionDetail> result = await service.CompileAsync(
            channelId.ToString(),
            widgetId.ToString(),
            new CompileWidgetRequest { SourceCode = "src" }
        );
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.BuildStatus.Should().Be("success");
    }

    [Fact]
    public async Task Manifest_lists_an_enabled_widget_with_a_successful_active_version()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget);
        await ActivateAsync(database, channel, widget);

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, BuildReturning("x", "y"));
        Result<OverlayManifest> result = await service.GetOverlayManifestAsync("tok");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.ChannelId.Should().Be(channel);
        result.Value.CspNonce.Should().NotBeNullOrWhiteSpace();
        result.Value.Widgets.Should().HaveCount(1);

        OverlayWidgetEntry entry = result.Value.Widgets[0];
        entry.WidgetId.Should().Be(widget);
        entry.Name.Should().Be("Alerts");
        entry.Framework.Should().Be("vanilla");
        entry.ContentHash.Should().Be("hash123");
        entry.TrustTier.Should().Be("unverified"); // Source=custom, fail-closed
        entry
            .BundleUrl.Should()
            .Contain(widget.ToString())
            .And.Contain("token=tok")
            .And.Contain("v=hash123");
        entry.EventSubscriptions.Should().Contain("follow");
        entry.Settings.Should().ContainKey("accent");
    }

    [Fact]
    public async Task Manifest_excludes_a_widget_that_has_never_compiled()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget); // no ActivateAsync -> ActiveVersionId null

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, BuildReturning("x", "y"));
        Result<OverlayManifest> result = await service.GetOverlayManifestAsync("tok");

        result.IsSuccess.Should().BeTrue();
        result.Value.Widgets.Should().BeEmpty();
    }

    [Fact]
    public async Task Manifest_excludes_a_disabled_widget()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget);
        await ActivateAsync(database, channel, widget);

        // Disable it after it has a live version.
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Widget w = await db.Widgets.SingleAsync(x => x.Id == widget);
            w.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        await using WidgetTestDbContext read = database.NewContext();
        WidgetService service = NewService(read, BuildReturning("x", "y"));
        Result<OverlayManifest> result = await service.GetOverlayManifestAsync("tok");

        result.IsSuccess.Should().BeTrue();
        result.Value.Widgets.Should().BeEmpty();
    }

    [Fact]
    public async Task Manifest_fails_for_an_unknown_token()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(database, Guid.CreateVersion7());

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, BuildReturning("x", "y"));
        Result<OverlayManifest> result = await service.GetOverlayManifestAsync("not-a-token");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Theory]
    [InlineData("first_party", "first_party")]
    [InlineData("verified_gallery", "verified_community")]
    [InlineData("custom", "unverified")]
    public async Task Manifest_derives_trust_tier_from_source(string source, string expectedTier)
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget, source: source);
        await ActivateAsync(database, channel, widget);

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, BuildReturning("x", "y"));
        Result<OverlayManifest> result = await service.GetOverlayManifestAsync("tok");

        result.Value.Widgets.Should().ContainSingle().Which.TrustTier.Should().Be(expectedTier);
    }

    [Fact]
    public async Task Bundle_serves_the_active_compiled_content_and_framework()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget);
        await ActivateAsync(database, channel, widget, bundle: "<div>hi</div>", hash: "abc");

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, BuildReturning("x", "y"));
        Result<OverlayBundle> result = await service.GetOverlayBundleAsync(
            "tok",
            widget.ToString()
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Content.Should().Be("<div>hi</div>");
        result.Value.Framework.Should().Be("vanilla");
        result.Value.ContentHash.Should().Be("abc");
    }

    [Fact]
    public async Task Bundle_resolves_a_ulid_encoded_widget_id()
    {
        // The JSON API serializes owned ids as their 26-char ULID form, so a client building the bundle URL from an
        // API response passes a ULID — not the raw Guid. The route must decode it to the same widget, not 404.
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget);
        await ActivateAsync(database, channel, widget, bundle: "<div>hi</div>", hash: "abc");

        string ulidId = new Ulid(widget).ToString();
        ulidId.Should().HaveLength(26).And.NotBe(widget.ToString()); // genuinely the ULID form, not the Guid

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, BuildReturning("x", "y"));
        Result<OverlayBundle> result = await service.GetOverlayBundleAsync("tok", ulidId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Content.Should().Be("<div>hi</div>");
        result.Value.Framework.Should().Be("vanilla");
        result.Value.ContentHash.Should().Be("abc");
    }

    [Fact]
    public async Task Bundle_fails_for_a_wrong_token_or_unknown_widget()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget);
        await ActivateAsync(database, channel, widget);

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, BuildReturning("x", "y"));

        Result<OverlayBundle> wrongToken = await service.GetOverlayBundleAsync(
            "nope",
            widget.ToString()
        );
        wrongToken.IsFailure.Should().BeTrue();
        wrongToken.ErrorCode.Should().Be("NOT_FOUND");

        Result<OverlayBundle> unknownWidget = await service.GetOverlayBundleAsync(
            "tok",
            Guid.NewGuid().ToString()
        );
        unknownWidget.IsFailure.Should().BeTrue();
        unknownWidget.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Bundle_fails_when_the_widget_has_no_successful_active_version()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget); // never compiled -> no active version

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, BuildReturning("x", "y"));
        Result<OverlayBundle> result = await service.GetOverlayBundleAsync(
            "tok",
            widget.ToString()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
