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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authentication;

/// <summary>
/// Points the three session operations the auth endpoints use (refresh, logout, logout-all) at the REAL
/// <see cref="ISessionService"/> and revocation store — the same calls <c>AuthService</c> makes — without
/// dragging in the Twitch login machinery the rest of <c>AuthService</c> needs.
/// </summary>
internal static class SessionBackedAuth
{
    public static void Wire(IAuthService auth, IServiceProvider root)
    {
        auth.RefreshTokenAsync(
                Arg.Any<string>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => RefreshAsync(root, ci.ArgAt<string>(0), ci.ArgAt<AuthContextDto>(1)));
        auth.LogoutAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => LogoutAsync(root, ci.ArgAt<Guid>(1)));
        auth.LogoutAllAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => LogoutAllAsync(root, ci.ArgAt<Guid>(0)));
    }

    private static async Task<Result<AuthResultDto>> RefreshAsync(
        IServiceProvider root,
        string refreshToken,
        AuthContextDto context
    )
    {
        await using AsyncServiceScope scope = root.CreateAsyncScope();
        Result<SessionTokensDto> rotated = await scope
            .ServiceProvider.GetRequiredService<ISessionService>()
            .RotateAsync(refreshToken, context);
        if (rotated.IsFailure)
            return rotated.WithValue<AuthResultDto>(null!);

        User user = await scope
            .ServiceProvider.GetRequiredService<AppDbContext>()
            .AuthSessions.Where(s => s.Id == rotated.Value.SessionId)
            .Select(s => s.User)
            .SingleAsync();
        return Result.Success(
            new AuthResultDto(
                rotated.Value.AccessToken,
                rotated.Value.RawRefreshToken,
                rotated.Value.AccessExpiresAt,
                new(
                    user.Id.ToString(),
                    user.Username,
                    user.DisplayName,
                    user.ProfileImageUrl,
                    null,
                    user.CreatedAt,
                    user.UpdatedAt
                )
            )
        );
    }

    private static async Task<Result> LogoutAsync(IServiceProvider root, Guid sessionId)
    {
        await using AsyncServiceScope scope = root.CreateAsyncScope();
        Result revoked = await scope
            .ServiceProvider.GetRequiredService<ISessionService>()
            .RevokeSessionAsync(sessionId, AuthEnums.RefreshTokenRevokedReason.Logout);
        await scope
            .ServiceProvider.GetRequiredService<ISessionRevocationService>()
            .RevokeAsync(sessionId);
        return revoked;
    }

    private static async Task<Result<int>> LogoutAllAsync(IServiceProvider root, Guid userId)
    {
        await using AsyncServiceScope scope = root.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<ISessionService>()
            .RevokeAllForUserAsync(userId, AuthEnums.RefreshTokenRevokedReason.Logout);
    }
}
