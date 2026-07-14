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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Owned ids are serialized to clients as 26-char ULID strings (<see cref="UlidGuidJsonConverter"/>), so the editor
/// sends a ULID widget/version id to the compile/versions/rollback routes. Those route params are typed
/// <c>string</c> and reach the service via <c>Guid.TryParse</c>, which rejects a ULID — the defect that made every
/// compile/version/rollback 404 ("the edit button doesn't work"). <see cref="WidgetsController"/> must normalize the
/// wire id (ULID <b>or</b> raw guid) to the canonical guid the service parses. These tests fail if that decode is dropped.
/// </summary>
public sealed class WidgetsControllerUlidRouteTests
{
    private static WidgetsController NewController(IWidgetService service) =>
        new(service, new ConfigurationBuilder().Build());

    [Theory]
    [InlineData(true)] // client sends the ULID wire form (what the dashboard actually sends)
    [InlineData(false)] // client sends a raw guid (inbound tolerance)
    public async Task Compile_normalizes_the_wire_widget_id_to_the_service_guid(bool asUlid)
    {
        Guid widgetGuid = Guid.CreateVersion7();
        string wire = asUlid ? GuidUlidCodec.Encode(widgetGuid) : widgetGuid.ToString();
        if (asUlid)
            wire.Should().NotBe(widgetGuid.ToString()); // it really is the 26-char ULID, not a guid

        string? received = null;
        IWidgetService service = Substitute.For<IWidgetService>();
        service
            .CompileAsync(
                Arg.Any<string>(),
                Arg.Do<string>(w => received = w),
                Arg.Any<CompileWidgetRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<WidgetVersionDetail>("short-circuit", "STOP"));

        await NewController(service)
            .CompileWidget(
                "chan",
                wire,
                new CompileWidgetRequest { SourceCode = "x" },
                CancellationToken.None
            );

        received.Should().Be(widgetGuid.ToString());
    }

    [Fact]
    public async Task Rollback_decodes_both_the_widget_and_version_ULIDs()
    {
        Guid widgetGuid = Guid.CreateVersion7();
        Guid versionGuid = Guid.CreateVersion7();

        string? gotWidget = null;
        string? gotVersion = null;
        IWidgetService service = Substitute.For<IWidgetService>();
        service
            .RollbackAsync(
                Arg.Any<string>(),
                Arg.Do<string>(w => gotWidget = w),
                Arg.Do<string>(v => gotVersion = v),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<WidgetDetail>("short-circuit", "STOP"));

        await NewController(service)
            .RollbackWidget(
                "chan",
                GuidUlidCodec.Encode(widgetGuid),
                GuidUlidCodec.Encode(versionGuid),
                CancellationToken.None
            );

        gotWidget.Should().Be(widgetGuid.ToString());
        gotVersion.Should().Be(versionGuid.ToString());
    }
}
