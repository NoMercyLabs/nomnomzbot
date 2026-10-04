// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Editor;

/// <summary>
/// A test run runs the files the editor has open, saved or not, and never stores them. Everything goes through the
/// real API of the dev instance, against a throwaway script that is deleted afterwards.
/// </summary>
public sealed class EditorTestRunUnsavedCodeTests : IAsyncLifetime
{
    private const string LiveLine = "live-v1";

    private readonly HttpClient _http = new() { BaseAddress = new(E2ESettings.BaseUrl + "/") };
    private string? _scriptId;

    public Task InitializeAsync()
    {
        if (!E2ESettings.Enabled)
            return Task.CompletedTask;

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            E2ESettings.Token
        );
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_scriptId is not null)
            await _http.DeleteAsync($"api/v1/code-scripts/{_scriptId}");
        _http.Dispose();
    }

    [E2EFact]
    public async Task An_unsaved_edit_runs_in_a_test_run_and_nothing_goes_live()
    {
        await CreateScriptAsync();
        JsonElement before = await GetScriptAsync();
        string versionId = before.GetProperty("currentVersionId").GetString()!;
        string storedSource = before
            .GetProperty("currentVersion")
            .GetProperty("sourceCode")
            .GetString()!;
        int versionCount = await VersionCountAsync();
        Assert.Contains(LiveLine, storedSource);

        string unsaved = $"unsaved-{Guid.NewGuid():N}";
        JsonElement run = await TestRunAsync(SendLine(unsaved));

        List<string> chat = ChatOutput(run);
        Assert.True(run.GetProperty("success").GetBoolean(), run.GetRawText());
        Assert.Contains(chat, line => line.Contains(unsaved, StringComparison.Ordinal));
        Assert.DoesNotContain(chat, line => line.Contains(LiveLine, StringComparison.Ordinal));

        await AssertNothingWentLiveAsync(versionId, storedSource, versionCount);
    }

    [E2EFact]
    public async Task A_compile_error_in_the_sent_files_fails_the_run_at_its_line_and_stores_nothing()
    {
        await CreateScriptAsync();
        JsonElement before = await GetScriptAsync();
        string versionId = before.GetProperty("currentVersionId").GetString()!;
        string storedSource = before
            .GetProperty("currentVersion")
            .GetProperty("sourceCode")
            .GetString()!;
        int versionCount = await VersionCountAsync();

        string broken = "const ok = 1;\nconst also = 2;\nconst oops = ;\n";
        HttpResponseMessage response = await PostTestRunAsync(Project(broken));
        string body = await response.Content.ReadAsStringAsync();

        Assert.False(response.IsSuccessStatusCode, body);
        using JsonDocument parsed = JsonDocument.Parse(body);
        Assert.Contains("\"line\":3", Compact(parsed.RootElement.GetRawText()));

        await AssertNothingWentLiveAsync(versionId, storedSource, versionCount);
    }

    private static string SendLine(string text) => $"bot.call('chat.send', '{text}');\n";

    private static string Compact(string json) => json.Replace(" ", string.Empty);

    private static object Project(string source) =>
        new
        {
            files = new Dictionary<string, string> { ["index.ts"] = source },
            manifest = new
            {
                entry = "index.ts",
                kind = "script",
                framework = "typescript",
                dependencies = Array.Empty<string>(),
            },
        };

    private async Task CreateScriptAsync()
    {
        HttpResponseMessage created = await _http.PostAsJsonAsync(
            "api/v1/code-scripts",
            new
            {
                name = $"e2e-testrun-{Guid.NewGuid():N}"[..24],
                description = "E2E throwaway, deleted by the test",
                sourceCode = SendLine(LiveLine),
            }
        );
        string body = await created.Content.ReadAsStringAsync();
        Assert.True(created.IsSuccessStatusCode, body);
        using JsonDocument parsed = JsonDocument.Parse(body);
        _scriptId = parsed.RootElement.GetProperty("data").GetProperty("id").GetString();

        HttpResponseMessage published = await _http.PostAsJsonAsync(
            $"api/v1/code-scripts/{_scriptId}/versions",
            new { sourceCode = SendLine(LiveLine), publish = true }
        );
        Assert.True(published.IsSuccessStatusCode, await published.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> GetScriptAsync()
    {
        HttpResponseMessage response = await _http.GetAsync($"api/v1/code-scripts/{_scriptId}");
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    private async Task<int> VersionCountAsync()
    {
        HttpResponseMessage response = await _http.GetAsync(
            $"api/v1/code-scripts/{_scriptId}/versions?take=100"
        );
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").GetArrayLength();
    }

    private Task<HttpResponseMessage> PostTestRunAsync(object? project) =>
        _http.PostAsJsonAsync(
            $"api/v1/code-scripts/{_scriptId}/test-run",
            new
            {
                variables = new Dictionary<string, string>(),
                args = Array.Empty<string>(),
                project,
            }
        );

    private async Task<JsonElement> TestRunAsync(string source)
    {
        HttpResponseMessage response = await PostTestRunAsync(Project(source));
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    private static List<string> ChatOutput(JsonElement run) =>
        run.GetProperty("chatOutput").EnumerateArray().Select(e => e.GetString()!).ToList();

    private async Task AssertNothingWentLiveAsync(string versionId, string source, int versionCount)
    {
        JsonElement after = await GetScriptAsync();
        Assert.Equal(versionId, after.GetProperty("currentVersionId").GetString());
        Assert.Equal(
            source,
            after.GetProperty("currentVersion").GetProperty("sourceCode").GetString()
        );
        Assert.Equal(versionCount, await VersionCountAsync());
    }
}
