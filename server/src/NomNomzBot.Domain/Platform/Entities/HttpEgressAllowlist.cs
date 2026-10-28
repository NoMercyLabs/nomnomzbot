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

namespace NomNomzBot.Domain.Platform.Entities;

/// <summary>
/// A tenant's approved outbound HTTP egress target (schema H.7) — the single SSRF boundary shared by the
/// code-execution sandbox and outbound webhooks. Default-deny: a guest/endpoint may only reach an enabled row's
/// <see cref="Fqdn"/>. The per-row clamps scope second-order/confused-deputy SSRF (only the opened doors —
/// methods, body, query, path prefix — are reachable). Soft-deletable + tenant-scoped.
/// </summary>
public class HttpEgressAllowlist : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }

    /// <summary>The exact host the egress request may reach.</summary>
    public string Fqdn { get; set; } = null!;
    public Guid? ApprovedByUserId { get; set; }
    public bool IsEnabled { get; set; }

    public int MaxResponseBytes { get; set; }

    /// <summary>Outbound request-body cap — reject (never truncate) when exceeded.</summary>
    public int MaxRequestBytes { get; set; } = 8192;
    public bool AllowRequestBody { get; set; }

    /// <summary>Whether a guest may attach an arbitrary query string (second-order SSRF reduction; default off).</summary>
    public bool AllowQuery { get; set; }

    /// <summary>CSV of permitted HTTP methods (default <c>GET</c>).</summary>
    public string AllowedMethods { get; set; } = "GET";

    /// <summary>Optional path-prefix restriction (null = any path).</summary>
    public string? PathPrefix { get; set; }
}
