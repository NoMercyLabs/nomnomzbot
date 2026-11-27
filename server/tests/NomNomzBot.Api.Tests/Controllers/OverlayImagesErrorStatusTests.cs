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

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Overlays.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// A refused image goes through the real MVC pipeline, content negotiation included: the caller gets the status
/// the refusal names, with the JSON error body, never a 406 because the success type is an image.
/// </summary>
public sealed class OverlayImagesErrorStatusTests : IAsyncDisposable
{
    private const string Url = "/api/v1/overlay/images?w=56&src=https%3A%2F%2Fexample.com%2Fa.png";

    private IHost? _host;

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private async Task<HttpClient> StartAsync(string errorCode)
    {
        IOverlayImageOptimizer optimizer = Substitute.For<IOverlayImageOptimizer>();
        optimizer
            .OptimizeAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<OptimizedImage>("Refused for the test.", errorCode));

        IHostBuilder builder = new HostBuilder().ConfigureWebHost(webHost =>
            webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(OverlayImagesController).Assembly);
                    services
                        .AddApiVersioning(o =>
                        {
                            o.DefaultApiVersion = new(1, 0);
                            o.AssumeDefaultVersionWhenUnspecified = true;
                        })
                        .AddMvc();
                    services.AddSingleton(optimizer);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })
        );

        _host = await builder.StartAsync();
        return _host.GetTestClient();
    }

    [Theory]
    [InlineData(IOverlayImageOptimizer.ErrorCodes.SourceNotAllowed, HttpStatusCode.BadRequest)]
    [InlineData(IOverlayImageOptimizer.ErrorCodes.InvalidWidth, HttpStatusCode.BadRequest)]
    [InlineData(IOverlayImageOptimizer.ErrorCodes.FetchFailed, HttpStatusCode.BadGateway)]
    public async Task A_refused_image_answers_its_own_status_with_the_error_code_in_json(
        string errorCode,
        HttpStatusCode expected
    )
    {
        HttpClient client = await StartAsync(errorCode);
        using HttpRequestMessage request = new(HttpMethod.Get, Url);
        request.Headers.Accept.ParseAdd("image/avif,image/webp,image/*,*/*;q=0.8");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be(errorCode);
    }
}
