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
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using NomNomzBot.Api.Configuration;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// The Vue dashboard is a single-page app: every dashboard address serves <c>index.html</c>, while the bot's own
/// address spaces (<c>/api</c>, <c>/hubs</c>, <c>/health</c>) and a missing build file never do. Runs the real
/// wiring (the same calls <c>Program.cs</c> makes) over a web root holding a stand-in shell and one hashed script.
/// </summary>
public sealed class DashboardShellFallbackTests : IAsyncLifetime
{
    private const string ShellBody = "<!doctype html><title>shell</title>";
    private const string PrivacyBody = "<!doctype html><h1>Privacy Policy</h1>";
    private const string TermsBody = "<!doctype html><h1>Terms of Service</h1>";
    private const string ScriptBody = "// stand-in for the hashed dashboard script";
    private const string EditorBody = "<!doctype html><title>editor page</title>";
    private string _webRoot = string.Empty;
    private string _editorRoot = string.Empty;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "nnz-shell-" + Guid.NewGuid().ToString("N"));
        _editorRoot = Path.Combine(_webRoot + "-editor");
        Directory.CreateDirectory(_editorRoot);
        await File.WriteAllTextAsync(Path.Combine(_editorRoot, "index.html"), EditorBody);
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), ShellBody);
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "privacy-policy.html"), PrivacyBody);
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "terms-of-service.html"), TermsBody);
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "assets", "index-3KohvHdj.js"),
            ScriptBody
        );

        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { WebRootPath = _webRoot }
        );
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        // Routing first, as WebApplication does in the real app: every endpoint is matched before static files.
        _app.UseRouting();
        _app.UseDashboardStaticFiles();
        _app.UseEditorStaticFiles(_editorRoot);
        _app.MapGet("/api/v1/ping", () => "pong");
        _app.MapDashboardFallback();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        Directory.Delete(_webRoot, recursive: true);
        Directory.Delete(_editorRoot, recursive: true);
    }

    [Theory]
    [InlineData("/editor/index.html", "must-revalidate, no-cache")]
    [InlineData("/editor/index.html?v=1.2.3", "public, max-age=31536000, immutable")]
    public async Task The_editor_page_is_served_from_its_folder_and_never_answered_by_a_fallback(
        string address,
        string cacheControl
    )
    {
        HttpResponseMessage response = await _client.GetAsync(address);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(EditorBody);
        response.Headers.CacheControl!.ToString().Should().Be(cacheControl);
    }

    [Theory]
    [InlineData("/app")]
    [InlineData("/app/")]
    [InlineData("/app/chat/some-deep-link")]
    [InlineData("/setup")]
    [InlineData("/login")]
    [InlineData("/me")]
    [InlineData("/popout/chat")]
    public async Task A_dashboard_address_serves_the_shell_and_never_stores_it(string address)
    {
        HttpResponseMessage response = await _client.GetAsync(address);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Be(ShellBody);
        response
            .Headers.CacheControl!.NoStore.Should()
            .BeTrue("a reload must always land the latest deploy");
    }

    // A policy checker reads the file without running the app, so each legal address has its own written page.
    [Theory]
    [InlineData("/privacy-policy", PrivacyBody)]
    [InlineData("/terms-of-service", TermsBody)]
    public async Task A_legal_address_serves_its_own_written_page_and_never_stores_it(
        string address,
        string body
    )
    {
        HttpResponseMessage response = await _client.GetAsync(address);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Be(body);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private void AskThroughTheProxy()
    {
        _client.DefaultRequestHeaders.Add("X-Forwarded-Host", "bot.example");
        // The live tunnel reports its inner hop, so a crawler's request arrives marked plain http.
        _client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
    }

    [Fact]
    public async Task The_sitemap_lists_exactly_the_public_pages_on_the_address_it_was_asked_on()
    {
        AskThroughTheProxy();

        HttpResponseMessage response = await _client.GetAsync("/sitemap.xml");
        XDocument sitemap = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        sitemap.Root!.Name.Should().Be(ns + "urlset");
        sitemap
            .Descendants(ns + "loc")
            .Select(loc => loc.Value)
            .Should()
            .Equal(
                "https://bot.example/",
                "https://bot.example/privacy-policy",
                "https://bot.example/terms-of-service"
            );
    }

    [Fact]
    public async Task The_robots_file_allows_crawling_and_points_at_the_sitemap()
    {
        AskThroughTheProxy();

        HttpResponseMessage response = await _client.GetAsync("/robots.txt");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        (await response.Content.ReadAsStringAsync())
            .Should()
            .Be("User-agent: *\nAllow: /\nSitemap: https://bot.example/sitemap.xml\n");
    }

    [Fact]
    public async Task A_hashed_build_file_is_served_as_the_script_and_cached_for_a_year()
    {
        HttpResponseMessage response = await _client.GetAsync("/assets/index-3KohvHdj.js");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        (await response.Content.ReadAsStringAsync()).Should().Be(ScriptBody);
        response.Headers.CacheControl!.ToString().Should().Be(StaticAssetCachePolicy.Immutable);
    }

    [Fact]
    public async Task A_missing_build_file_is_a_404_and_never_the_shell()
    {
        HttpResponseMessage response = await _client.GetAsync("/assets/index-gone0000.js");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("shell");
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData("/hubs/unknown")]
    [InlineData("/health/unknown")]
    [InlineData("/editor/unknown")]
    public async Task An_unknown_address_in_a_bot_owned_space_is_a_404_and_never_the_shell(
        string address
    )
    {
        HttpResponseMessage response = await _client.GetAsync(address);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("shell");
    }

    [Fact]
    public async Task A_real_api_endpoint_is_still_answered_by_its_own_handler()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/v1/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("pong");
    }
}
