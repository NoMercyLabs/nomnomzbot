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
/// The <c>Kind = "code_script"</c> shape of <c>PlatformContentVersion.PayloadJson</c> (platform-admin.md §3.2
/// extended for the code-script kind, S-ADMIN-2e): the raw TypeScript/JavaScript source a fresh install or
/// publish writes onto a tenant <c>CodeScript</c> row as a NEW <c>CodeScriptVersion</c>, validated through the
/// SAME <c>IScriptExecutor.CompileAsync</c> path a tenant's own editor save uses — never a second compiler.
/// </summary>
public sealed record CodeScriptContentPayload(string SourceCode)
{
    /// <summary>Parses a <see cref="CodeScriptContentPayload"/> from its <c>PayloadJson</c> string. A missing
    /// <c>sourceCode</c> or a document that isn't a JSON object is a validation failure the caller turns into
    /// <c>VALIDATION_FAILED</c> — never a thrown exception reaching the controller.</summary>
    public static bool TryParse(
        string? json,
        out CodeScriptContentPayload? payload,
        out string? error
    )
    {
        payload = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Code script content payload is empty.";
            return false;
        }

        try
        {
            CodeScriptContentPayload? parsed =
                JsonConvert.DeserializeObject<CodeScriptContentPayload>(json);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.SourceCode))
            {
                error = "Code script content payload must carry non-empty \"sourceCode\".";
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Code script content payload is not valid JSON: {ex.Message}";
            return false;
        }
    }

    /// <summary>The canonicalized hash of just the source — compared against a tenant <c>CodeScript</c> row's
    /// <c>PlatformSourceHash</c> to decide "untouched" for <c>update_in_place_where_untouched</c> publishes.
    /// </summary>
    public string ComputeSourceHash() => ComputeSourceHash(SourceCode);

    /// <summary>Hashes the source wrapped as <c>{"sourceCode": "..."}</c> — <see cref="PlatformContentHash"/>
    /// canonicalizes/hashes JSON documents, not arbitrary text, so the raw source is never passed to it
    /// directly (a source string that happens not to parse as JSON — virtually all real source — would throw).
    /// </summary>
    public static string ComputeSourceHash(string sourceCode) =>
        PlatformContentHash.ComputeHash(JsonConvert.SerializeObject(new { sourceCode }));
}
