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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Admin.Services;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Infrastructure.Admin.Diagnostics;

/// <summary>
/// Gates and audits FIRST through <see cref="IPlatformIamService.AuthorizePlatformAsync"/> on <c>tenant:read</c>
/// (no justification: a read-only insight, Stoney 2026-10-30), naming the sections or the run in
/// <c>TargetResource</c>, then runs the requested <see cref="IChannelDiagnosticSection"/> classes one after the
/// other (they share one database context). A section the caller did not ask for is never read.
/// </summary>
public sealed class ChannelDiagnosticsService(
    IApplicationDbContext db,
    IPlatformIamService iam,
    IPipelineExecutionQueryService runs,
    IEnumerable<IChannelDiagnosticSection> registered
) : IChannelDiagnosticsService
{
    public async Task<Result<ChannelDiagnosticsDto>> GetAsync(
        Guid actingPrincipalId,
        ChannelDiagnosticRequest request,
        IReadOnlyCollection<string>? sections,
        CancellationToken cancellationToken = default
    )
    {
        Result<List<IChannelDiagnosticSection>> selected = Select(sections);
        if (selected.IsFailure)
            return selected.WithValue<ChannelDiagnosticsDto>(null!);

        string keys = string.Join(',', selected.Value.Select(s => s.Key));
        Result authorized = await AuthorizeAsync(
            actingPrincipalId,
            request.BroadcasterId,
            $"diagnostics:{keys}",
            cancellationToken
        );
        if (authorized.IsFailure)
            return authorized.WithValue<ChannelDiagnosticsDto>(null!);

        bool channelExists = await db.Channels.AnyAsync(
            c => c.Id == request.BroadcasterId,
            cancellationToken
        );
        if (!channelExists)
            return Result.Failure<ChannelDiagnosticsDto>("Unknown channel.", "NOT_FOUND");

        ChannelDiagnosticsDto diagnostics = new() { BroadcasterId = request.BroadcasterId };
        foreach (IChannelDiagnosticSection section in selected.Value)
        {
            Result read = await section.ReadIntoAsync(request, diagnostics, cancellationToken);
            if (read.IsFailure)
                return read.WithValue<ChannelDiagnosticsDto>(null!);
        }

        return Result.Success(diagnostics);
    }

    public async Task<Result<PipelineExecutionDetailDto>> GetRunAsync(
        Guid actingPrincipalId,
        Guid broadcasterId,
        long runId,
        CancellationToken cancellationToken = default
    )
    {
        Result authorized = await AuthorizeAsync(
            actingPrincipalId,
            broadcasterId,
            $"diagnostics:runs:{runId}",
            cancellationToken
        );
        if (authorized.IsFailure)
            return authorized.WithValue<PipelineExecutionDetailDto>(null!);

        return await runs.GetDetailAsync(broadcasterId.ToString(), runId, cancellationToken);
    }

    private Result<List<IChannelDiagnosticSection>> Select(IReadOnlyCollection<string>? requested)
    {
        List<IChannelDiagnosticSection> all = registered.ToList();
        if (requested is null || requested.Count == 0)
            return Result.Success(all);

        List<IChannelDiagnosticSection> picked = [];
        foreach (string key in requested.Distinct(StringComparer.Ordinal))
        {
            IChannelDiagnosticSection? section = all.FirstOrDefault(s => s.Key == key);
            if (section is null)
            {
                string known = string.Join(", ", all.Select(s => s.Key).Order());
                return Result.Failure<List<IChannelDiagnosticSection>>(
                    $"Unknown diagnostic section '{key}'. Known sections: {known}.",
                    "VALIDATION_FAILED"
                );
            }

            picked.Add(section);
        }

        return Result.Success(picked);
    }

    /// <summary>Decides AND audits (allowed or denied) before a single channel row is read.</summary>
    private async Task<Result> AuthorizeAsync(
        Guid actingPrincipalId,
        Guid broadcasterId,
        string targetResource,
        CancellationToken cancellationToken
    )
    {
        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            actingPrincipalId,
            IamPermissionKeys.TenantRead,
            broadcasterId,
            false,
            null,
            cancellationToken,
            targetResource
        );
        if (allowed.IsFailure)
            return allowed;
        return allowed.Value
            ? Result.Success()
            : Result.Failure($"Requires {IamPermissionKeys.TenantRead}.", "FORBIDDEN");
    }
}
