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
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// The event samples a widget author edited, saved in the project as <c>events/&lt;type&gt;.json</c>. The editor's fire
/// bar sends the file's content as the event payload, so a file that is not a JSON object is refused at save.
/// </summary>
public static class WidgetEventSampleFiles
{
    public const string Folder = "events/";

    public const string Extension = ".json";

    public const string InvalidCode = "WIDGET_EVENT_SAMPLE_INVALID";

    public static bool IsSampleFile(string path) =>
        path.StartsWith(Folder, StringComparison.Ordinal)
        && path.EndsWith(Extension, StringComparison.Ordinal);

    public static Result Validate(IReadOnlyDictionary<string, string> files)
    {
        foreach ((string path, string content) in files)
        {
            if (!IsSampleFile(path))
                continue;

            if (!IsJsonObject(content))
                return Result.Failure($"{path} must be a JSON object.", InvalidCode);
        }

        return Result.Success();
    }

    private static bool IsJsonObject(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
