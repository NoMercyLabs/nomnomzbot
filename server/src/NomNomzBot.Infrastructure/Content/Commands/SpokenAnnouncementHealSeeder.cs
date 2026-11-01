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

using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Makes the cheer and resub pipelines that speak ONLY the viewer's message speak the whole announcement.
/// Older cheer flows and the imported "Resub response" flow end in a <c>play_tts</c> of <c>{message}</c>, so the
/// bot never called out the amount or the duration, and a cheer or resub without a message went silent. The
/// step's text becomes the full thank-you line; <c>play_tts</c> reads the announcement in the bot's voice and
/// the viewer's message in the viewer's own voice, in one clip. The "only when there is a message" condition
/// is removed, since the announcement is always spoken. Order 89, after the flow seeders and the graph-cache
/// healer. Idempotent: a step whose text is no longer exactly <c>{message}</c> is never touched.
/// </summary>
public sealed class SpokenAnnouncementHealSeeder(IApplicationDbContext db) : ISeeder
{
    private const string MessageOnly = "{message}";
    private const string CheerPipeline = "Cheer response";
    private const string ResubPipeline = "Resub response";
    private const string ResubLine =
        "{user} resubscribed for {months} months! Thank you!{also_said}";

    public int Order => 89;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<Pipeline> pipelines = await db
            .Pipelines.Where(p => p.Name == CheerPipeline || p.Name == ResubPipeline)
            .ToListAsync(ct);
        if (pipelines.Count == 0)
            return;

        List<Guid> ids = pipelines.Select(p => p.Id).ToList();
        List<PipelineStep> steps = await db
            .PipelineSteps.Include(s => s.Conditions)
            .Where(s => ids.Contains(s.PipelineId))
            .ToListAsync(ct);

        bool changed = false;
        foreach (Pipeline pipeline in pipelines)
        {
            List<PipelineStep> own = steps.Where(s => s.PipelineId == pipeline.Id).ToList();
            string? line = pipeline.Name == ResubPipeline ? ResubLine : CheerLine(own);
            if (line is null)
                continue;

            bool healed = false;
            foreach (PipelineStep step in own.Where(s => s.ActionType == "play_tts"))
                healed |= TryHeal(step, line);
            if (!healed)
                continue;

            pipeline.GraphJsonCache = PipelineGraphBuilder.BuildGraphJson(own);
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }

    /// <summary>The cheer flow's own chat thank-you, so the spoken line is the line chat shows.</summary>
    private static string? CheerLine(List<PipelineStep> steps)
    {
        PipelineStep? thanks = steps
            .Where(s => s.ActionType == "send_message")
            .OrderBy(s => s.Order)
            .FirstOrDefault();
        string? line = thanks is null
            ? null
            : JsonNode.Parse(thanks.ConfigJson)?["message"]?.GetValue<string>();

        return string.IsNullOrWhiteSpace(line) || !line.Contains("{also_said}") ? null : line;
    }

    private static bool TryHeal(PipelineStep step, string line)
    {
        if (JsonNode.Parse(step.ConfigJson) is not JsonObject config)
            return false;
        if (config["text"]?.GetValue<string>()?.Trim() != MessageOnly)
            return false;
        string? speaker = config["as"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(speaker) && speaker != "viewer")
            return false;

        config.Remove("as");
        config["text"] = line;
        step.ConfigJson = config.ToJsonString();
        step.Conditions.Clear();

        return true;
    }
}
