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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.DevPlatform;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// <c>GET /sdk/types.d.ts?context=widget&amp;widget=…</c> types a widget's own settings from its settings schema.
/// The widget id is the id the widget routes use (a ULID string or a Guid) and the schema is read for the caller's
/// own channel only, so another channel's widget is a 404 and never leaks a schema.
/// </summary>
public sealed class SdkTypesWidgetTests
{
    private static readonly Guid OwnChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid OwnWidget = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid OtherChannelsWidget = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000a2"
    );
    private static readonly Guid UnschematisedWidget = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000a3"
    );

    private static SdkController Controller(bool hasTenant = true)
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .GetSettingsSchemaAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string channel = call.ArgAt<string>(0);
                string widget = call.ArgAt<string>(1);
                if (channel != OwnChannel.ToString())
                    return Result.Failure<WidgetSettingsSchema>("Widget not found.", "NOT_FOUND");
                if (widget == UnschematisedWidget.ToString())
                    return Result.Failure<WidgetSettingsSchema>(
                        "No settings schema.",
                        "WIDGET_NO_SETTINGS_SCHEMA"
                    );
                // OtherChannelsWidget belongs to another channel: the service finds no row for this one.
                return widget == OwnWidget.ToString()
                    ? Result.Success(Schema())
                    : Result.Failure<WidgetSettingsSchema>("Widget not found.", "NOT_FOUND");
            });

        ICurrentTenantService currentTenant = Substitute.For<ICurrentTenantService>();
        currentTenant.BroadcasterId.Returns(hasTenant ? OwnChannel : null);

        return new SdkController(
            new SdkTypeEmitter(new EventCatalog()),
            Substitute.For<ICodeScriptTriggerResolver>(),
            widgets,
            currentTenant
        );
    }

    private static WidgetSettingsSchema Schema() =>
        new(
            "demo",
            "Demo",
            [
                new WidgetSettingsField(
                    "durationMs",
                    new LocalizedText("demo.duration.label", "How long it shows"),
                    "number",
                    new LocalizedText("demo.group"),
                    4000
                ),
                new WidgetSettingsField(
                    "layout",
                    new LocalizedText("demo.layout.label"),
                    "select",
                    new LocalizedText("demo.group"),
                    "row",
                    Options:
                    [
                        new WidgetSettingsFieldOption("row", new LocalizedText("demo.row")),
                        new WidgetSettingsFieldOption("column", new LocalizedText("demo.column")),
                    ]
                ),
            ],
            []
        );

    [Fact]
    public async Task The_types_for_this_channels_widget_include_its_settings_interface()
    {
        IActionResult result = await Controller()
            .GetTypes("widget", null, null, OwnWidget.ToString());

        string ts = result.Should().BeOfType<ContentResult>().Subject.Content!;
        ts.Should().Contain("interface NnzWidgetSettings {");
        ts.Should().Contain("  durationMs: number;");
        ts.Should().Contain("  layout: 'row' | 'column';");
        ts.Should().Contain("declare const WIDGET_SETTINGS: NnzWidgetSettings;");
    }

    [Fact]
    public async Task The_widget_id_may_be_the_ulid_form_the_widget_routes_use()
    {
        string ulid = GuidUlidCodec.Encode(OwnWidget);

        IActionResult result = await Controller().GetTypes("widget", null, null, ulid);

        result
            .Should()
            .BeOfType<ContentResult>()
            .Subject.Content.Should()
            .Contain("interface NnzWidgetSettings {");
    }

    [Fact]
    public async Task Another_channels_widget_is_a_404_with_the_not_found_code()
    {
        IActionResult result = await Controller()
            .GetTypes("widget", null, null, OtherChannelsWidget.ToString());

        NotFoundObjectResult notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        StatusResponseDto<object> body = notFound
            .Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Subject;
        body.Status.Should().Be("error");
        body.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task No_tenant_is_a_404_so_no_schema_is_read()
    {
        SdkController controller = Controller(hasTenant: false);

        IActionResult result = await controller.GetTypes(
            "widget",
            null,
            null,
            OwnWidget.ToString()
        );

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task A_widget_without_a_settings_schema_gets_the_open_settings_record()
    {
        IActionResult result = await Controller()
            .GetTypes("widget", null, null, UnschematisedWidget.ToString());

        string ts = result.Should().BeOfType<ContentResult>().Subject.Content!;
        ts.Should().NotContain("interface NnzWidgetSettings");
        ts.Should().Contain("declare const WIDGET_SETTINGS: Record<string, unknown>;");
    }

    [Fact]
    public async Task A_widget_on_the_script_context_is_a_400()
    {
        IActionResult result = await Controller()
            .GetTypes("script", null, null, OwnWidget.ToString());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Without_a_widget_the_widget_types_keep_the_open_settings_record()
    {
        IActionResult result = await Controller().GetTypes("widget", null, null);

        string ts = result.Should().BeOfType<ContentResult>().Subject.Content!;
        ts.Should().Contain("declare const WIDGET_SETTINGS: Record<string, unknown>;");
    }
}
