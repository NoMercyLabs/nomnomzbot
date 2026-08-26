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
/// Thrown by <see cref="IWebhookBodyTemplateRenderer"/> when an endpoint's body template is declared as JSON
/// (<c>BodyIsJson</c>) but does not parse (S-WEBHOOK-JSON-FALLBACK). This should never happen for an endpoint
/// created or updated through the service — the same JSON syntax check runs at save time — but is the honest
/// outcome if a bad template somehow reaches storage anyway: the delivery fails with a recorded reason instead
/// of silently downgrading to the unescaped plain-text path.
/// </summary>
public sealed class WebhookBodyTemplateInvalidJsonException(
    string message,
    Exception innerException
) : Exception(message, innerException);
