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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// JSON-aware implementation of <see cref="IWebhookBodyTemplateRenderer"/> (S-WEBHOOK-TEMPLATE-GRAMMAR):
/// parses the body template as JSON, walks the resulting tree, resolves every <see cref="ITemplateResolver"/>
/// placeholder found inside a string leaf, then re-serializes. Because substitution happens on the parsed
/// object model — never on the raw text — a resolved value can contain any character (quotes, backslashes,
/// newlines, unicode) without ever corrupting the surrounding JSON: <c>JsonConvert</c> escapes it on the way
/// back out. A template that does not parse as JSON is resolved as plain text — JSON-escaping does not apply
/// to a non-JSON body.
/// </summary>
public sealed class WebhookBodyTemplateRenderer(ITemplateResolver templateResolver)
    : IWebhookBodyTemplateRenderer
{
    public string Render(
        string? bodyTemplate,
        IReadOnlyDictionary<string, string> variables,
        bool bodyIsJson
    )
    {
        if (bodyTemplate is null)
            return JsonConvert.SerializeObject(variables);

        Dictionary<string, string> vars = new(variables, StringComparer.OrdinalIgnoreCase);

        if (!bodyIsJson)
            return templateResolver.Resolve(bodyTemplate, vars);

        JToken parsed;
        try
        {
            parsed = JToken.Parse(bodyTemplate);
        }
        catch (JsonReaderException ex)
        {
            throw new WebhookBodyTemplateInvalidJsonException(
                $"Body template is declared as JSON but does not parse: {ex.Message}",
                ex
            );
        }

        ResolveStringLeaves(parsed, vars);
        return parsed.ToString(Formatting.None);
    }

    private void ResolveStringLeaves(JToken token, Dictionary<string, string> vars)
    {
        switch (token)
        {
            case JValue { Type: JTokenType.String } stringValue:
                stringValue.Value = templateResolver.Resolve((string)stringValue.Value!, vars);
                break;
            case JObject jsonObject:
                foreach (JProperty property in jsonObject.Properties())
                    ResolveStringLeaves(property.Value, vars);
                break;
            case JArray jsonArray:
                foreach (JToken item in jsonArray)
                    ResolveStringLeaves(item, vars);
                break;
        }
    }
}
