// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using NomNomzBot.Api.Configuration;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// S-KMP-BASEHREF: <c>/app</c> and <c>/app/</c> must load the same dashboard. The server wiring under test is
/// the real <see cref="DashboardShellExtensions"/> (the same two calls <c>Program.cs</c> makes); the web root
/// holds the REAL <c>index.html</c> from the app source plus a stand-in <c>composeApp.js</c>. The browser's
/// asset loading is reproduced by resolving the script address the way a browser does: against the page
/// address, or against the <c>&lt;base href&gt;</c> when the page has one.
/// </summary>
public sealed class DashboardShellAddressTests : IAsyncLifetime
{
    private const string AssetBody = "// stand-in for the compiled dashboard bundle";
    private string _webRoot = string.Empty;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "nnz-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);
        File.Copy(FindAppIndexHtml(), Path.Combine(_webRoot, "index.html"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "composeApp.js"), AssetBody);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { WebRootPath = _webRoot }
        );
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseDashboardStaticFiles();
        _app.MapDashboardFallback();
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
    [InlineData("/app")]
    [InlineData("/app/")]
    public async Task Address_serves_the_index_and_the_script_it_references(string address)
    {
        Uri page = new("http://localhost" + address);

        HttpResponseMessage index = await _client.GetAsync(page);
        index.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        index.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        string html = await index.Content.ReadAsStringAsync();

        Uri script = ResolveScriptAddress(html, page);
        HttpResponseMessage asset = await _client.GetAsync(script);

        asset
            .StatusCode.Should()
            .Be(System.Net.HttpStatusCode.OK, $"the page at {address} loads {script}");
        asset
            .Content.Headers.ContentType!.MediaType.Should()
            .Be("text/javascript", $"{script} must be the script, not the HTML shell");
        (await asset.Content.ReadAsStringAsync()).Should().Be(AssetBody);
    }

    /// <summary>The address of the <c>composeApp.js</c> script tag, resolved as a browser resolves it.</summary>
    private static Uri ResolveScriptAddress(string html, Uri page)
    {
        Match src = Regex.Match(html, "<script src=\"(?<src>[^\"]*composeApp\\.js)\"");
        src.Success.Should().BeTrue("index.html loads composeApp.js");

        Match baseTag = Regex.Match(html, "<base href=\"(?<href>[^\"]*)\"");
        Uri baseAddress = baseTag.Success ? new(page, baseTag.Groups["href"].Value) : page;
        return new(baseAddress, src.Groups["src"].Value);
    }

    private static string FindAppIndexHtml()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "app",
                "composeApp",
                "src",
                "wasmJsMain",
                "resources",
                "index.html"
            );
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "app/composeApp/src/wasmJsMain/resources/index.html not found above the test binaries"
        );
    }
}
