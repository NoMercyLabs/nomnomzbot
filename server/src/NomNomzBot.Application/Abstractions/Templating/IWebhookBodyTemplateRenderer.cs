// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Renders an outbound webhook body template through the single <see cref="ITemplateResolver"/> grammar
/// (S-WEBHOOK-TEMPLATE-GRAMMAR) in a way that cannot corrupt the JSON payload: when the template parses
/// as JSON, only the string leaves are resolved (post-parse, pre-serialize), so every substituted value is
/// JSON-escaped by the serializer and can never break the document's structure. A template that is not
/// valid JSON is resolved as plain text instead — JSON-escaping would be meaningless there.
/// </summary>
public interface IWebhookBodyTemplateRenderer
{
    /// <summary>
    /// Renders <paramref name="bodyTemplate"/> with <paramref name="variables"/>. A null template renders the
    /// variables themselves as a JSON object (the endpoint's default body when no template is configured).
    /// </summary>
    string Render(string? bodyTemplate, IReadOnlyDictionary<string, string> variables);
}
