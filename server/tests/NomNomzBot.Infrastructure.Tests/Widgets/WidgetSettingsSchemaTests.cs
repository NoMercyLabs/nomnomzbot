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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the typed settings schema is complete and correctly wired: every first-party widget type has a schema
/// whose fields cover exactly its <c>DefaultSettings</c> keys (the drift guard — a new or renamed settings key with
/// no matching field fails the build), each field's default is the catalogue default, and a select/multiselect
/// field carries usable options. Also proves the service resolves a first-party widget's schema from its gallery
/// link and refuses a self-authored custom widget.
/// </summary>
public sealed class WidgetSettingsSchemaTests
{
    private static readonly WidgetSettingsSchemaProvider Provider = new();

    // The control types the schema is allowed to emit (must match the frontend field renderer's known set).
    private static readonly HashSet<string> KnownFieldTypes =
    [
        "bool",
        "number",
        "text",
        "color",
        "select",
        "multiselect",
        "json",
    ];

    [Fact]
    public void Every_first_party_widget_type_has_a_schema()
    {
        HashSet<string> schematised = Provider.GetAll().Select(s => s.WidgetKey).ToHashSet();
        HashSet<string> catalogue = FirstPartyWidgetCatalogue.All.Select(w => w.Key).ToHashSet();

        schematised
            .Should()
            .BeEquivalentTo(
                catalogue,
                "every first-party widget must have exactly one settings schema (no type left un-schematised, "
                    + "no orphan schema)"
            );
    }

    [Fact]
    public void Each_schema_covers_exactly_its_default_settings_keys_wired_to_the_catalogue_defaults()
    {
        foreach (FirstPartyWidgetDefinition definition in FirstPartyWidgetCatalogue.All)
        {
            WidgetSettingsSchema? schema = Provider.GetByKey(definition.Key);
            schema.Should().NotBeNull($"'{definition.Key}' must have a schema");

            // The set of field keys must equal the set of settings keys the widget honours — no key unconfigurable,
            // no field for a key the widget does not read.
            HashSet<string> fieldKeys = schema!.Fields.Select(f => f.Key).ToHashSet();
            fieldKeys
                .Should()
                .BeEquivalentTo(
                    definition.DefaultSettings.Keys,
                    $"'{definition.Key}' fields must cover exactly its DefaultSettings keys"
                );

            // No duplicate fields for the same key.
            schema
                .Fields.Select(f => f.Key)
                .Should()
                .OnlyHaveUniqueItems($"'{definition.Key}' must not declare a key twice");

            // The read-only event-subscription reference is the widget's real default topic set.
            schema
                .EventSubscriptions.Should()
                .Equal(
                    definition.DefaultEventSubscriptions,
                    $"'{definition.Key}' event subscriptions"
                );

            foreach (WidgetSettingsField field in schema.Fields)
            {
                KnownFieldTypes
                    .Should()
                    .Contain(
                        field.Type,
                        $"'{definition.Key}.{field.Key}' uses a renderable control type"
                    );

                field
                    .Label.Should()
                    .NotBeNullOrWhiteSpace($"'{definition.Key}.{field.Key}' needs a label");
                field
                    .Group.Should()
                    .NotBeNullOrWhiteSpace($"'{definition.Key}.{field.Key}' needs a group");

                // The default shown in the form IS the catalogue default (same instance) — proves defaults are read
                // back from the seeded catalogue, never re-typed (which is how they would silently drift).
                field
                    .Default.Should()
                    .BeSameAs(
                        definition.DefaultSettings[field.Key],
                        $"'{definition.Key}.{field.Key}' default must come from the catalogue"
                    );
            }
        }
    }

    [Fact]
    public void Choice_fields_carry_usable_options_and_a_valid_default()
    {
        foreach (WidgetSettingsSchema schema in Provider.GetAll())
        {
            foreach (
                WidgetSettingsField field in schema.Fields.Where(f =>
                    f.Type is "select" or "multiselect"
                )
            )
            {
                field
                    .Options.Should()
                    .NotBeNullOrEmpty(
                        $"'{schema.WidgetKey}.{field.Key}' is a choice field and needs options"
                    );
                field
                    .Options!.Select(o => o.Value)
                    .Should()
                    .OnlyHaveUniqueItems(
                        $"'{schema.WidgetKey}.{field.Key}' options must be distinct"
                    );
                field
                    .Options!.Should()
                    .OnlyContain(
                        o =>
                            !string.IsNullOrWhiteSpace(o.Value)
                            && !string.IsNullOrWhiteSpace(o.Label),
                        $"'{schema.WidgetKey}.{field.Key}' every option needs a value + label"
                    );

                // A single-choice field's default value must be one of the offered options.
                if (field.Type == "select" && field.Default is string defaultValue)
                    field
                        .Options!.Select(o => o.Value)
                        .Should()
                        .Contain(
                            defaultValue,
                            $"'{schema.WidgetKey}.{field.Key}' default is a valid option"
                        );
            }
        }
    }

    [Fact]
    public async Task Service_resolves_a_first_party_widgets_schema_from_its_gallery_link()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid galleryItemId = Guid.CreateVersion7();
        Guid widgetId = Guid.CreateVersion7();

        await using (WidgetTestDbContext db = database.NewContext())
        {
            db.Channels.Add(NewChannel(channel));
            db.WidgetGalleryItems.Add(
                new WidgetGalleryItem
                {
                    Id = galleryItemId,
                    NaturalKey = "chat_box",
                    Name = "Chat Box",
                    Framework = "vue",
                    TrustTier = "first_party",
                    SourceKind = "in_repo",
                    ReviewStatus = "verified",
                    SourceCode = "<template/>",
                }
            );
            db.Widgets.Add(
                new Widget
                {
                    Id = widgetId,
                    BroadcasterId = channel,
                    Name = "Chat Box",
                    Framework = "vue",
                    Source = "first_party",
                    GalleryItemId = galleryItemId,
                    IsEnabled = true,
                }
            );
            await db.SaveChangesAsync();
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetSettingsSchema> result = await NewService(db)
                .GetSettingsSchemaAsync(channel.ToString(), widgetId.ToString());

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.Value.WidgetKey.Should().Be("chat_box");
            result
                .Value.Fields.Select(f => f.Key)
                .Should()
                .Contain(new[] { "theme", "fontSize", "showTimestamps", "accentColor" });
            result.Value.Fields.Single(f => f.Key == "theme").Type.Should().Be("select");
            result.Value.Fields.Single(f => f.Key == "accentColor").Type.Should().Be("color");
        }
    }

    [Fact]
    public async Task Service_refuses_a_schema_for_a_self_authored_custom_widget()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widgetId = Guid.CreateVersion7();

        await using (WidgetTestDbContext db = database.NewContext())
        {
            db.Channels.Add(NewChannel(channel));
            db.Widgets.Add(
                new Widget
                {
                    Id = widgetId,
                    BroadcasterId = channel,
                    Name = "My custom overlay",
                    Framework = "vue",
                    Source = "custom",
                    GalleryItemId = null,
                    IsEnabled = true,
                }
            );
            await db.SaveChangesAsync();
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetSettingsSchema> result = await NewService(db)
                .GetSettingsSchemaAsync(channel.ToString(), widgetId.ToString());

            result.IsFailure.Should().BeTrue();
            result.ErrorCode.Should().Be("WIDGET_NO_SETTINGS_SCHEMA");
        }
    }

    // A widget row has an FK to its channel (BroadcasterId → Channel), enforced by the real SQLite schema, so the
    // channel must exist before the widget is inserted — even though schema resolution never reads the channel.
    private static Channel NewChannel(Guid channelId) =>
        new()
        {
            Id = channelId,
            OwnerUserId = Guid.CreateVersion7(),
            TwitchChannelId = "12345",
            Name = "teststreamer",
            NameNormalized = "teststreamer",
            OverlayToken = "tok",
        };

    private static WidgetService NewService(WidgetTestDbContext db) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            Substitute.For<IEventBus>(),
            Substitute.For<IWidgetBuildService>(),
            new WidgetSettingsSchemaProvider(),
            new FakeTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero))
        );
}
