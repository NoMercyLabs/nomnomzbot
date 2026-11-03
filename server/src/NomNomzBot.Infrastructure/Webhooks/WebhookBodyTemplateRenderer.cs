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
    public async Task<string> RenderAsync(
        string? bodyTemplate,
        IReadOnlyDictionary<string, string> variables,
        bool bodyIsJson,
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        if (bodyTemplate is null)
            return JsonConvert.SerializeObject(variables);

        Dictionary<string, string> vars = new(variables, StringComparer.OrdinalIgnoreCase);

        if (!bodyIsJson)
            return await templateResolver.ResolveAsync(bodyTemplate, vars, broadcasterId, ct);

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

        await ResolveStringLeavesAsync(parsed, vars, broadcasterId, ct);
        return parsed.ToString(Formatting.None);
    }

    private async Task ResolveStringLeavesAsync(
        JToken token,
        Dictionary<string, string> vars,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        switch (token)
        {
            case JValue { Type: JTokenType.String } stringValue:
                stringValue.Value = await templateResolver.ResolveAsync(
                    (string)stringValue.Value!,
                    vars,
                    broadcasterId,
                    ct
                );
                break;
            case JObject jsonObject:
                foreach (JProperty property in jsonObject.Properties())
                    await ResolveStringLeavesAsync(property.Value, vars, broadcasterId, ct);
                break;
            case JArray jsonArray:
                foreach (JToken item in jsonArray)
                    await ResolveStringLeavesAsync(item, vars, broadcasterId, ct);
                break;
        }
    }
}
