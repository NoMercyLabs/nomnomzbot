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
