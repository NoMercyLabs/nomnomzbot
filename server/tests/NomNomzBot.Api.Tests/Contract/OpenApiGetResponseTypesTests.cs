// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;

namespace NomNomzBot.Api.Tests.Contract;

/// <summary>
/// Contract guard for the committed <c>server/openapi/v1.json</c> snapshot: every GET operation declares
/// the real type of its success body. An action that returns a bare <c>IActionResult</c> without a
/// <c>[ProducesResponseType&lt;T&gt;(200)]</c> leaves the document with no 2xx schema, so the generated
/// TypeScript and Kotlin clients type the body as <c>unknown</c>. The only GETs allowed to lack a typed
/// JSON success body are the ones in <see cref="NonJsonGets"/>, each with the reason it is not JSON.
/// </summary>
public sealed class OpenApiGetResponseTypesTests
{
    private const string ApiPrefix = "/api/v{version}";

    /// <summary>GET routes (without the api prefix) whose success answer is not a JSON body, with the reason.</summary>
    private static readonly Dictionary<string, string> NonJsonGets = new()
    {
        // OAuth browser navigations: the success answer is a 302 redirect (or an HTML page), never a JSON body.
        ["/auth/twitch"] = "302 redirect to the Twitch authorize page",
        ["/auth/twitch/callback"] = "302 redirect, or an HTML page, after the Twitch code exchange",
        ["/auth/{provider}/authorize"] = "302 redirect to the provider authorize page",
        ["/auth/{provider}/callback"] = "302 redirect after the provider code exchange",
        ["/integrations/{provider}/callback"] =
            "302 redirect through /oauth-relay after the code exchange",
        ["/integrations/discord/callback"] =
            "302 redirect through /oauth-relay after the Discord install",
        ["/channels/{channelId}/integrations/discord/callback/start"] =
            "302 redirect to the Discord authorize page",
        // Pages, files, streams and text feeds.
        ["/automation/pair/device/approve"] = "text/html approval page",
        ["/oauth-relay"] = "text/html relay page that posts the OAuth result to the opener window",
        ["/channels/{channelId}/live-ops/schedule/icalendar"] = "text/calendar feed (RFC 5545)",
        ["/channels/{channelId}/live-ops/schedule/icalendar/subscribe"] =
            "text/calendar feed (RFC 5545)",
        ["/assets/file/{channelId}/{name}"] =
            "binary asset file served with its sniffed media type",
        ["/overlay/bundle/{widgetId}"] = "text/html or application/javascript widget bundle",
        ["/sound-clips/stream/{storageKey}"] = "audio file stream with range requests",
        ["/sdk/types.d.ts"] = "text/plain TypeScript declaration file",
    };

    [Fact]
    public void Every_GET_operation_declares_a_typed_success_body_unless_it_is_not_JSON()
    {
        JsonElement paths = LoadSnapshot().GetProperty("paths");
        List<string> untyped = [];

        foreach (JsonProperty path in paths.EnumerateObject())
        {
            if (!path.Value.TryGetProperty("get", out JsonElement get))
                continue;

            string route = path.Name.StartsWith(ApiPrefix)
                ? path.Name[ApiPrefix.Length..]
                : path.Name;
            if (NonJsonGets.ContainsKey(route))
                continue;

            if (!HasTypedSuccessBody(get))
                untyped.Add(route);
        }

        string report =
            $"{untyped.Count} GET operation(s) without a typed 2xx JSON body: {string.Join(", ", untyped)}. "
            + "Each GET must declare its real schema via [ProducesResponseType<T>(200)]; "
            + "a GET that is not JSON belongs in NonJsonGets with a reason";
        untyped.Should().BeEmpty("{0}", report);
    }

    [Fact]
    public void The_non_JSON_allowlist_names_only_real_GET_routes_with_a_reason()
    {
        JsonElement paths = LoadSnapshot().GetProperty("paths");
        List<string> stale = [];

        foreach (KeyValuePair<string, string> entry in NonJsonGets)
        {
            bool exists =
                (
                    paths.TryGetProperty(ApiPrefix + entry.Key, out JsonElement item)
                    || paths.TryGetProperty(entry.Key, out item)
                ) && item.TryGetProperty("get", out _);
            if (!exists || string.IsNullOrWhiteSpace(entry.Value))
                stale.Add(entry.Key);
        }

        stale
            .Should()
            .BeEmpty("an allowlist entry must match a GET operation and say why it is not JSON");
    }

    private static bool HasTypedSuccessBody(JsonElement operation)
    {
        foreach (JsonProperty response in operation.GetProperty("responses").EnumerateObject())
        {
            if (!response.Name.StartsWith('2'))
                continue;
            if (
                !response.Value.TryGetProperty("content", out JsonElement content)
                || !content.TryGetProperty("application/json", out JsonElement json)
                || !json.TryGetProperty("schema", out JsonElement schema)
            )
                continue;

            // The framework fills untyped actions with the envelope of Object; that is not a real type.
            if (
                schema.TryGetProperty("$ref", out JsonElement reference)
                && reference.GetString()!.EndsWith("OfObject", StringComparison.Ordinal)
            )
                continue;

            return true;
        }

        return false;
    }

    private static JsonElement LoadSnapshot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "openapi", "v1.json")))
        {
            string nested = Path.Combine(dir.FullName, "server", "openapi", "v1.json");
            if (File.Exists(nested))
                return Parse(nested);
            dir = dir.Parent;
        }

        return Parse(
            Path.Combine(
                dir?.FullName
                    ?? throw new FileNotFoundException("server/openapi/v1.json not found"),
                "openapi",
                "v1.json"
            )
        );
    }

    private static JsonElement Parse(string file)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
        return document.RootElement.Clone();
    }
}
