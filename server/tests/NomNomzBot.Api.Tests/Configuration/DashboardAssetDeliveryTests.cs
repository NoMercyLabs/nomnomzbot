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
/// S-KMP-FONTS: the first visit to the dashboard moved 46 MB because the bundled fonts went out raw. Runs the
/// real compression and static-file wiring (the same calls <c>Program.cs</c> makes) over a web root holding a
/// compressible font and a script, and reads the bytes a browser asking for brotli would receive.
/// </summary>
public sealed class DashboardAssetDeliveryTests : IAsyncLifetime
{
    private const string FontPath =
        "/composeResources/nomnomzbot.composeapp.generated.resources/font/noto_sans_kr.ttf";
    private const int FontBytes = 400_000;
    private string _webRoot = string.Empty;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "nnz-assets-" + Guid.NewGuid().ToString("N"));
        string fontDir = Path.Combine(
            _webRoot,
            "composeResources",
            "nomnomzbot.composeapp.generated.resources",
            "font"
        );
        Directory.CreateDirectory(fontDir);
        await File.WriteAllBytesAsync(
            Path.Combine(fontDir, "noto_sans_kr.ttf"),
            RepetitiveFontLikeBytes()
        );
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "composeApp.js"),
            string.Concat(Enumerable.Repeat("function boot() { return 1; }\n", 5_000))
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
    [InlineData(FontPath)]
    [InlineData("/composeApp.js")]
    public async Task A_bundled_file_is_sent_brotli_compressed_and_much_smaller(string path)
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
    public async Task The_font_stays_cacheable_for_a_year()
    {
        HttpResponseMessage response = await GetWithBrotliAsync(FontPath);

        response.Headers.CacheControl!.ToString().Should().Be(StaticAssetCachePolicy.Immutable);
    }

    [Fact]
    public async Task The_entry_script_is_revalidated_by_etag_so_a_repeat_visit_costs_no_bytes()
    {
        HttpResponseMessage first = await GetWithBrotliAsync("/composeApp.js");
        first.Headers.CacheControl!.NoStore.Should().BeFalse("composeApp.js must be cacheable");
        first.Headers.ETag.Should().NotBeNull("the browser revalidates with the ETag");

        HttpRequestMessage again = new(HttpMethod.Get, "/composeApp.js");
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

    private static byte[] RepetitiveFontLikeBytes()
    {
        byte[] bytes = new byte[FontBytes];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }
}
