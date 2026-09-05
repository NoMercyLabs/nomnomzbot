// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Content.Platform;

/// <summary>
/// Rewrites already-stored <c>${variable}</c> templates to <c>{variable}</c>.
/// <para>
/// <c>TemplateSyntaxInterceptor</c> corrects the syntax on every save, but only for rows that are
/// saved again — a template the owner authored months ago keeps rendering with a stray <c>$</c> in
/// front of the value until something rewrites it. That is the actual reported symptom (a live
/// <c>!lurk</c> reply reading "$MisadventuresInAstro rolls up…"), so the existing rows need this pass.
/// </para>
/// <para>
/// Deliberately a seeder rather than a SQL migration. The rewrite is only correct when the braces hold
/// a name the product actually knows as a variable — that set lives in <c>TemplateHelperRegistry</c>,
/// and encoding it as a hand-written list of SQL REPLACE calls would both duplicate it and go stale
/// the next time a helper is added. Running in-process also makes it provider-agnostic: one
/// implementation covers SQLite and PostgreSQL instead of two migrations that must agree.
/// </para>
/// <para>
/// Idempotent by construction: it loads only rows whose text still contains <c>${</c>, and
/// <see cref="TemplateSyntaxNormalizer"/> leaves anything it does not recognise alone — so a second
/// boot writes nothing, and a row holding a deliberate literal <c>$</c> before a non-variable is never
/// touched, however many times this runs.
/// </para>
/// </summary>
public sealed class TemplateSyntaxBackfillSeeder : ISeeder
{
    private const string DollarMarker = "${";

    private readonly IApplicationDbContext _db;
    private readonly ITemplateHelperValidator _validator;
    private readonly ILogger<TemplateSyntaxBackfillSeeder> _logger;

    public TemplateSyntaxBackfillSeeder(
        IApplicationDbContext db,
        ITemplateHelperValidator validator,
        ILogger<TemplateSyntaxBackfillSeeder> logger
    )
    {
        _db = db;
        _validator = validator;
        _logger = logger;
    }

    // Late, like the other backfills: these rows are authored by streamers, not seeded, so there is
    // nothing to wait for except the schema itself.
    public int Order => 910;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        int rewritten = 0;

        rewritten += await BackfillCommandsAsync(ct);
        rewritten += await BackfillTimersAsync(ct);
        rewritten += await BackfillEventResponsesAsync(ct);
        rewritten += await BackfillOutboundWebhooksAsync(ct);

        if (rewritten > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Template syntax backfill rewrote {Count} stored template(s) from ${{x}} to {{x}}.",
                rewritten
            );
        }
    }

    private async Task<int> BackfillCommandsAsync(CancellationToken ct)
    {
        List<Domain.Commands.Entities.Command> rows = await _db
            .Commands.IgnoreQueryFilters()
            .Where(c =>
                (c.TemplateResponse != null && c.TemplateResponse.Contains(DollarMarker))
                || c.TemplateResponses!.Any(t => t.Contains(DollarMarker))
            )
            .ToListAsync(ct);

        int changed = 0;
        foreach (Domain.Commands.Entities.Command row in rows)
        {
            if (Rewrite(row.TemplateResponse, TemplateHelperContext.Command, out string? single))
            {
                row.TemplateResponse = single;
                changed++;
            }

            changed += RewriteList(row.TemplateResponses, TemplateHelperContext.Command);
        }

        return changed;
    }

    private async Task<int> BackfillTimersAsync(CancellationToken ct)
    {
        List<Domain.Commands.Entities.Timer> rows = await _db
            .Timers.IgnoreQueryFilters()
            .Where(t => t.Messages.Any(m => m.Contains(DollarMarker)))
            .ToListAsync(ct);

        int changed = 0;
        foreach (Domain.Commands.Entities.Timer row in rows)
            changed += RewriteList(row.Messages, TemplateHelperContext.Timer);

        return changed;
    }

    private async Task<int> BackfillEventResponsesAsync(CancellationToken ct)
    {
        List<Domain.Commands.Entities.EventResponse> rows = await _db
            .EventResponses.IgnoreQueryFilters()
            .Where(e => e.Message != null && e.Message.Contains(DollarMarker))
            .ToListAsync(ct);

        int changed = 0;
        foreach (Domain.Commands.Entities.EventResponse row in rows)
        {
            if (!Rewrite(row.Message, TemplateHelperContext.EventResponse, out string? message))
                continue;

            row.Message = message;
            changed++;
        }

        return changed;
    }

    private async Task<int> BackfillOutboundWebhooksAsync(CancellationToken ct)
    {
        List<Domain.Webhooks.Entities.OutboundWebhookEndpoint> rows = await _db
            .OutboundWebhookEndpoints.IgnoreQueryFilters()
            .Where(w => w.BodyTemplate != null && w.BodyTemplate.Contains(DollarMarker))
            .ToListAsync(ct);

        int changed = 0;
        foreach (Domain.Webhooks.Entities.OutboundWebhookEndpoint row in rows)
        {
            if (!Rewrite(row.BodyTemplate, TemplateHelperContext.Webhook, out string? body))
                continue;

            row.BodyTemplate = body;
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// True when normalising actually changed the value AND the result still validates, so callers only
    /// count real rewrites.
    /// <para>
    /// The rewrite is validated rather than trusted for the reason
    /// <c>TemplatedUserContentSavePathGuardTests</c> exists: every path that persists
    /// <c>[TemplatedUserContent]</c> text routes it through <see cref="ITemplateHelperValidator"/>
    /// first. A stored template that does not validate is left EXACTLY as it was — this pass repairs
    /// syntax, and refusing to touch a row it cannot vouch for is better than persisting something it
    /// has not checked or throwing on boot over content a streamer authored long ago.
    /// </para>
    /// </summary>
    private bool Rewrite(string? current, TemplateHelperContext context, out string? normalized)
    {
        normalized = TemplateSyntaxNormalizer.Normalize(current);
        if (string.Equals(normalized, current, StringComparison.Ordinal))
            return false;

        if (_validator.Validate(normalized, context).IsFailure)
        {
            _logger.LogWarning(
                "Template syntax backfill left a {Context} template unchanged: the normalised form did not validate.",
                context
            );
            normalized = current;
            return false;
        }

        return true;
    }

    private int RewriteList(List<string>? values, TemplateHelperContext context)
    {
        if (values is null)
            return 0;

        int changed = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (!Rewrite(values[i], context, out string? normalized) || normalized is null)
                continue;

            values[i] = normalized;
            changed++;
        }

        return changed;
    }
}
