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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// ITemplateEngine implementation that performs simple {{variable}} substitution.
/// Used for command responses, shoutout templates, and notification messages.
/// </summary>
public sealed partial class TemplateEngine : ITemplateEngine
{
    /// <summary>
    /// Replaces all {{variable}} placeholders in the template with values from the provided dictionary.
    /// Unknown variables are left as-is. Variable names are case-insensitive.
    /// </summary>
    public string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return TemplatePattern()
            .Replace(
                template,
                match =>
                {
                    string variableName = match.Groups[1].Value.Trim();

                    // Case-insensitive lookup
                    foreach (KeyValuePair<string, string> kvp in variables)
                    {
                        if (
                            string.Equals(kvp.Key, variableName, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            return kvp.Value ?? string.Empty;
                        }
                    }

                    // Unknown variable -- leave placeholder intact
                    return match.Value;
                }
            );
    }

    /// <summary>
    /// Renders a template with a single variable substitution.
    /// </summary>
    public string Render(string template, string variableName, string variableValue)
    {
        return Render(
            template,
            (IReadOnlyDictionary<string, string>)
                new Dictionary<string, string> { { variableName, variableValue } }
        );
    }

    /// <summary>
    /// Async render for templates that require data lookups.
    /// Converts all object values to strings synchronously.
    /// </summary>
    public Task<string> RenderAsync(
        string template,
        IDictionary<string, object> variables,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, string> stringVars = variables.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.ToString() ?? string.Empty
        );
        return Task.FromResult(Render(template, stringVars));
    }

    [GeneratedRegex(@"\{\{(.+?)\}\}")]
    private static partial Regex TemplatePattern();
}
