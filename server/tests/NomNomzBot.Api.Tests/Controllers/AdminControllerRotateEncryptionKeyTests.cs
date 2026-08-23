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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the S098e KEK-rotation re-wrap pass is REACHABLE over HTTP (not only callable in-process): the
/// admin action wires the request body straight into <see cref="IDekRotationService.RotateAllDeksAsync"/>
/// and surfaces its result — this is the entry point an operator hits after rotating <c>Encryption:Key</c>.
/// </summary>
public sealed class AdminControllerRotateEncryptionKeyTests
{
    [Fact]
    public async Task RotateEncryptionKey_InvokesRotationService_AndReturnsItsSummary()
    {
        IAdminService adminService = Substitute.For<IAdminService>();
        IApplicationDbContext db = Substitute.For<IApplicationDbContext>();
        IDekRotationService rotationService = Substitute.For<IDekRotationService>();

        DekRotationSummary summary = new(RewrappedCount: 3, AlreadyCurrentCount: 1, Failures: []);
        rotationService
            .RotateAllDeksAsync("old-key", "new-key", Arg.Any<CancellationToken>())
            .Returns(Result.Success(summary));

        AdminController controller = new(adminService, db, rotationService);

        IActionResult result = await controller.RotateEncryptionKey(
            new AdminController.RotateEncryptionKeyRequestDto("old-key", "new-key"),
            CancellationToken.None
        );

        await rotationService
            .Received(1)
            .RotateAllDeksAsync("old-key", "new-key", Arg.Any<CancellationToken>());

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<DekRotationSummary> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<DekRotationSummary>>()
            .Subject;
        body.Data.Should().NotBeNull();
        body.Data!.RewrappedCount.Should().Be(3);
        body.Data.AlreadyCurrentCount.Should().Be(1);
    }
}
