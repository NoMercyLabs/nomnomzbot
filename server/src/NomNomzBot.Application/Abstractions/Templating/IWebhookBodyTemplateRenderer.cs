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

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Renders an outbound webhook body template through the single <see cref="ITemplateResolver"/> grammar
/// (S-WEBHOOK-TEMPLATE-GRAMMAR) in a way that cannot corrupt the JSON payload: when the caller declares the
/// template as JSON (<paramref name="bodyIsJson"/>), only the string leaves are resolved (post-parse,
/// pre-serialize), so every substituted value is JSON-escaped by the serializer and can never break the
/// document's structure. A template that is genuinely not JSON is resolved as plain text instead —
/// JSON-escaping would be meaningless there. Whether a template is "intended as JSON" is a property of the
/// endpoint (<c>OutboundWebhookEndpoint.BodyIsJson</c>, S-WEBHOOK-JSON-FALLBACK), never guessed from the body
/// text — a JSON-declared template that fails to parse throws <see cref="WebhookBodyTemplateInvalidJsonException"/>
/// rather than silently downgrading to the unsafe plain-text path.
/// </summary>
public interface IWebhookBodyTemplateRenderer
{
    /// <summary>
    /// Renders <paramref name="bodyTemplate"/> with <paramref name="variables"/>. A null template renders the
    /// variables themselves as a JSON object (the endpoint's default body when no template is configured).
    /// </summary>
    /// <param name="bodyIsJson">
    /// True when the template is declared as JSON — it must parse, and is rendered through the JSON-safe
    /// leaf-substitution path. False renders through the plain-text path unconditionally, skipping the JSON
    /// parse attempt entirely.
    /// </param>
    /// <exception cref="WebhookBodyTemplateInvalidJsonException">
    /// <paramref name="bodyIsJson"/> is true and <paramref name="bodyTemplate"/> does not parse as JSON.
    /// </exception>
    string Render(
        string? bodyTemplate,
        IReadOnlyDictionary<string, string> variables,
        bool bodyIsJson
    );
}
