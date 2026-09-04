// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;

namespace NomNomzBot.Infrastructure.Content.PlatformContent;

/// <summary>
/// The <c>Kind = "widget"</c> shape of <c>PlatformContentVersion.PayloadJson</c> (platform-admin.md §3.2 extended
/// for the widget kind): a single-file Vue SFC source plus the settings a fresh install seeds a tenant
/// <c>Widget</c> row with — the same three ingredients <c>FirstPartyWidgetCatalogueSeeder</c> ships into the
/// gallery, now carried through the authored/versioned/published platform-content spine instead of a build-time
/// embedded asset.
/// </summary>
public sealed record WidgetContentPayload(
    string SourceCode,
    Dictionary<string, object> DefaultSettings,
    List<string> DefaultEventSubscriptions
)
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>Parses a <see cref="WidgetContentPayload"/> from its <c>PayloadJson</c> string. A missing
    /// <c>sourceCode</c> or a document that isn't a JSON object is a validation failure the caller turns into
    /// <c>VALIDATION_FAILED</c> — never a thrown exception reaching the controller.</summary>
    public static bool TryParse(string? json, out WidgetContentPayload? payload, out string? error)
    {
        payload = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Widget content payload is empty.";
            return false;
        }

        try
        {
            WidgetContentPayload? parsed = JsonConvert.DeserializeObject<WidgetContentPayload>(
                json,
                SerializerSettings
            );
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.SourceCode))
            {
                error = "Widget content payload must carry non-empty \"sourceCode\".";
                return false;
            }

            payload = parsed with
            {
                DefaultSettings = parsed.DefaultSettings ?? [],
                DefaultEventSubscriptions = parsed.DefaultEventSubscriptions ?? [],
            };
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Widget content payload is not valid JSON: {ex.Message}";
            return false;
        }
    }

    /// <summary>The canonicalized hash of just the tenant-mutable slice (settings + event subscriptions) —
    /// compared against a tenant <c>Widget</c> row's <c>PlatformSourceHash</c> to decide "untouched" for
    /// <c>update_in_place_where_untouched</c> publishes. Deliberately excludes <see cref="SourceCode"/>: the
    /// Vue source is not stored on the tenant row itself (that lives on <c>WidgetVersion</c>), so only the
    /// fields a tenant can actually edit in place participate in staleness detection.
    /// </summary>
    public string ComputeSettingsHash() =>
        ComputeSettingsHash(DefaultSettings, DefaultEventSubscriptions);

    public static string ComputeSettingsHash(
        IReadOnlyDictionary<string, object> settings,
        IReadOnlyList<string> eventSubscriptions
    )
    {
        string json = JsonConvert.SerializeObject(
            new { settings, eventSubscriptions },
            SerializerSettings
        );
        return PlatformContentHash.ComputeHash(json);
    }
}
