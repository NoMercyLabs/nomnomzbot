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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Common.Models;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The public asset serving route's safety contract: anonymous access, immutable caching, nosniff on every
/// response, the SNIFFED content type only, and the CSP + Content-Disposition lockdown that keeps a
/// broadcaster-uploaded SVG from ever executing script when opened as a document.
/// </summary>
public sealed class AssetsControllerServingTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-00000000d001");

    private static (AssetsController Controller, IChannelAssetService Service) Build()
    {
        IChannelAssetService service = Substitute.For<IChannelAssetService>();
        AssetsController controller = new(
            service,
            Substitute.For<ICurrentUserService>(),
            Substitute.For<ICurrentTenantService>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, service);
    }

    private static void ServeReturns(IChannelAssetService service, string mimeType, byte[] bytes) =>
        service
            .OpenForServingAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<ChannelAssetContent>.Success(
                    new ChannelAssetContent(
                        new MemoryStream(bytes),
                        mimeType,
                        mimeType.StartsWith("image/") ? "image" : "audio",
                        bytes.Length
                    )
                )
            );

    [Fact]
    public void Serve_route_is_anonymous_so_obs_browser_sources_need_no_jwt()
    {
        typeof(AssetsController)
            .GetMethod(nameof(AssetsController.ServeFile))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
            .Should()
            .NotBeEmpty();
    }

    [Fact]
    public async Task Png_serves_with_the_sniffed_type_immutable_cache_and_nosniff()
    {
        (AssetsController controller, IChannelAssetService service) = Build();
        ServeReturns(service, "image/png", [0x89, 0x50, 0x4E, 0x47]);

        IActionResult result = await controller.ServeFile(Channel, "boot-screen", default);

        FileStreamResult file = result.Should().BeOfType<FileStreamResult>().Subject;
        file.ContentType.Should().Be("image/png");
        file.EnableRangeProcessing.Should().BeTrue();
        controller
            .Response.Headers.CacheControl.ToString()
            .Should()
            .Be("public, max-age=31536000, immutable");
        controller.Response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        // No CSP needed for raster images.
        controller.Response.Headers.ContentSecurityPolicy.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Svg_serves_locked_down_so_embedded_script_can_never_execute()
    {
        (AssetsController controller, IChannelAssetService service) = Build();
        ServeReturns(
            service,
            "image/svg+xml",
            System.Text.Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>")
        );

        IActionResult result = await controller.ServeFile(Channel, "feather", default);

        FileStreamResult file = result.Should().BeOfType<FileStreamResult>().Subject;
        file.ContentType.Should().Be("image/svg+xml");
        string csp = controller.Response.Headers.ContentSecurityPolicy.ToString();
        csp.Should().Contain("default-src 'none'");
        csp.Should().Contain("sandbox");
        controller.Response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        controller
            .Response.Headers.ContentDisposition.ToString()
            .Should()
            .StartWith("inline; filename=");
    }

    [Fact]
    public async Task Missing_asset_serves_404_not_an_error_page()
    {
        (AssetsController controller, IChannelAssetService service) = Build();
        service
            .OpenForServingAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<ChannelAssetContent>.Failure("Asset not found.", "NOT_FOUND"));

        IActionResult result = await controller.ServeFile(Channel, "ghost", default);

        result.Should().BeOfType<NotFoundResult>();
    }
}
