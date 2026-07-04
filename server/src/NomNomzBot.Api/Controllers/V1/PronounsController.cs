// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Viewer pronoun management: the pronoun catalogue and the caller's own pronoun selection.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/pronouns")]
public sealed class PronounsController : BaseController
{
    private readonly IPronounSelfService _selfService;
    private readonly ICurrentUserService _currentUser;
    private readonly AppDbContext _db;

    public PronounsController(
        IPronounSelfService selfService,
        ICurrentUserService currentUser,
        AppDbContext db
    )
    {
        _selfService = selfService;
        _currentUser = currentUser;
        _db = db;
    }

    /// <summary>Full pronoun catalog — anonymous, same data as <c>GET /system/pronouns</c> but namespaced here.</summary>
    [HttpGet("catalog")]
    [AllowAnonymous]
    [ProducesResponseType<StatusResponseDto<IEnumerable<PronounCatalogDto>>>(
        StatusCodes.Status200OK
    )]
    public IActionResult GetCatalog()
    {
        IEnumerable<PronounCatalogDto> catalog = _db
            .Pronouns.OrderBy(p => p.Name)
            .Select(p => new PronounCatalogDto(p.Id, p.Name, p.Subject, p.Object, p.Key))
            .AsEnumerable();

        return Ok(new StatusResponseDto<IEnumerable<PronounCatalogDto>> { Data = catalog });
    }

    /// <summary>Return the authenticated viewer's current pronoun state.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<StatusResponseDto<UserPronounDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        if (!TryParseUserId(out Guid userId))
            return Unauthorized();

        UserPronounDto? dto = await _selfService.GetAsync(userId, ct);
        return dto is null ? NotFound() : Ok(new StatusResponseDto<UserPronounDto> { Data = dto });
    }

    /// <summary>Update the authenticated viewer's pronouns.</summary>
    [HttpPut("me")]
    [Authorize]
    [RequireAction("pronouns:self:write")]
    [ProducesResponseType<StatusResponseDto<UserPronounDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutMe(
        [FromBody] SetPronounRequest request,
        CancellationToken ct
    )
    {
        if (!TryParseUserId(out Guid userId))
            return Unauthorized();

        UserPronounDto? dto = await _selfService.SetAsync(userId, request, ct);
        return dto is null ? NotFound() : Ok(new StatusResponseDto<UserPronounDto> { Data = dto });
    }

    private bool TryParseUserId(out Guid userId)
    {
        string? raw = _currentUser.UserId;
        return Guid.TryParse(raw, out userId);
    }
}

/// <summary>Catalog entry as returned by the public catalog endpoint.</summary>
public sealed record PronounCatalogDto(
    int Id,
    string Name,
    string Subject,
    string Object,
    string? Key
);
