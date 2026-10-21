// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using NomNomzBot.Api.Configuration;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// The first visit to the dashboard must not move raw bytes. Runs the real compression and static-file wiring
/// (the same calls <c>Program.cs</c> makes) over a web root holding a hashed script, a stylesheet, a stable-name
/// service worker and a font, and reads the bytes a browser asking for brotli would receive.
/// </summary>
public sealed class DashboardAssetDeliveryTests : IAsyncLifetime
{
    private const string HashedScript = "/assets/index-3KohvHdj.js";
    private const string HashedStyle = "/assets/index-Dk3f2a1b.css";
    private const string ServiceWorker = "/sw.js";
    private const string Font = "/assets/inter-latin-wght-normal-C2S99t-D.woff2";
    private string _webRoot = string.Empty;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "nnz-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        string script = string.Concat(Enumerable.Repeat("function boot() { return 1; }\n", 5_000));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "assets", "index-3KohvHdj.js"), script);
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "sw.js"), script);
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "assets", "index-Dk3f2a1b.css"),
            string.Concat(Enumerable.Repeat(".a { color: red; }\n", 5_000))
        );
        await File.WriteAllBytesAsync(
            Path.Combine(_webRoot, "assets", "inter-latin-wght-normal-C2S99t-D.woff2"),
            [1, 2, 3, 4]
        );

        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { WebRootPath = _webRoot }
        );
        builder.WebHost.UseTestServer();
        builder.Services.AddDashboardResponseCompression();
        _app = builder.Build();
        _app.UseResponseCompression();
        _app.UseDashboardStaticFiles();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData(HashedScript)]
    [InlineData(HashedStyle)]
    [InlineData(ServiceWorker)]
    public async Task A_script_or_style_is_sent_brotli_compressed_and_much_smaller(string path)
    {
        HttpResponseMessage response = await GetWithBrotliAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response
            .Content.Headers.ContentEncoding.Should()
            .ContainSingle()
            .Which.Should()
            .Be("br", $"{path} must not go out raw");
        long onTheWire = (await response.Content.ReadAsByteArrayAsync()).LongLength;
        long original = new FileInfo(Path.Combine(_webRoot, path.TrimStart('/'))).Length;
        onTheWire.Should().BeLessThan(original / 4);
    }

    [Fact]
    public async Task The_hashed_font_stays_cacheable_for_a_year()
    {
        HttpResponseMessage response = await GetWithBrotliAsync(Font);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().Should().Be(StaticAssetCachePolicy.Immutable);
    }

    [Fact]
    public async Task The_service_worker_is_revalidated_by_etag_so_a_repeat_visit_costs_no_bytes()
    {
        HttpResponseMessage first = await GetWithBrotliAsync(ServiceWorker);
        first.Headers.CacheControl!.NoStore.Should().BeFalse("sw.js must be cacheable");
        first.Headers.CacheControl!.Public.Should().BeTrue();
        first.Headers.CacheControl!.NoCache.Should().BeTrue("sw.js is revalidated on every load");
        first.Headers.CacheControl!.MustRevalidate.Should().BeTrue();
        first.Headers.ETag.Should().NotBeNull("the browser revalidates with the ETag");

        HttpRequestMessage again = new(HttpMethod.Get, ServiceWorker);
        again.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        HttpResponseMessage second = await _client.SendAsync(again);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    private async Task<HttpResponseMessage> GetWithBrotliAsync(string path)
    {
        HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        return await _client.SendAsync(request);
    }
}
