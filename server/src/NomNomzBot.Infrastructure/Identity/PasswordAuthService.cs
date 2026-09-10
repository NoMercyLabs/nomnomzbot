// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The native email+password login (identity-auth — generic account credential). Register opens the same
/// tenant-less session as <see cref="ExternalLoginService"/> (no channel yet); login resolves the caller's
/// existing channel, exactly like a returning Twitch login. Hashing goes entirely through
/// <see cref="PasswordHasher{TUser}"/> — never hand-rolled.
/// </summary>
public sealed class PasswordAuthService : IPasswordAuthService
{
    // A stable minimum — long enough to rule out the trivial cases, short enough not to be its own usability
    // bug. No further "complexity" rule (a required digit/symbol/etc.) — length is what actually resists
    // brute force; composition rules mostly just push users toward predictable substitutions (NIST SP 800-63B).
    private const int MinPasswordLength = 8;

    private static readonly PasswordHasher<User> Hasher = new();

    private readonly IApplicationDbContext _db;
    private readonly ISessionService _sessions;
    private readonly TimeProvider _clock;
    private readonly ILogger<PasswordAuthService> _logger;

    public PasswordAuthService(
        IApplicationDbContext db,
        ISessionService sessions,
        TimeProvider clock,
        ILogger<PasswordAuthService> logger
    )
    {
        _db = db;
        _sessions = sessions;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<AuthResultDto>> RegisterAsync(
        string email,
        string password,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    )
    {
        string? normalized = NormalizeEmail(email);
        if (normalized is null)
            return Result.Failure<AuthResultDto>("Enter a valid email address.", "EMAIL_INVALID");

        if (password.Length < MinPasswordLength)
            return Result.Failure<AuthResultDto>(
                $"Password must be at least {MinPasswordLength} characters.",
                "WEAK_PASSWORD"
            );

        bool taken = await _db.Users.AnyAsync(
            u => u.LoginEmailNormalized == normalized,
            cancellationToken
        );
        if (taken)
            return Result.Failure<AuthResultDto>(
                "An account with that email already exists.",
                "EMAIL_TAKEN"
            );

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        string localPart = normalized[..normalized.IndexOf('@')];
        User user = new()
        {
            Platform = AuthEnums.LoginProvider.Password,
            Username = localPart,
            UsernameNormalized = localPart,
            DisplayName = localPart,
            LoginEmail = email.Trim(),
            LoginEmailNormalized = normalized,
            Enabled = true,
            LastSeenAt = now,
        };
        user.PasswordHash = Hasher.HashPassword(user, password);
        user.PasswordUpdatedAt = now;
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        // Tenant-less session: a brand-new password account has no channel — they land on the setup wizard's
        // "connect a platform" step exactly like a first non-Twitch OAuth login (ExternalLoginService).
        Result<SessionTokensDto> session = await _sessions.CreateSessionAsync(
            user.Id,
            broadcasterId: null,
            context,
            cancellationToken
        );
        if (session.IsFailure)
            return session.WithValue<AuthResultDto>(null!);

        _logger.LogInformation("User {UserId} registered via email+password", user.Id);
        return Result.Success(BuildAuthResult(session.Value, user));
    }

    public async Task<Result<AuthResultDto>> LoginAsync(
        string email,
        string password,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    )
    {
        string? normalized = NormalizeEmail(email);
        User? user = normalized is null
            ? null
            : await _db.Users.FirstOrDefaultAsync(
                u => u.LoginEmailNormalized == normalized,
                cancellationToken
            );

        // Verify against a dummy hash for an unknown email / passwordless account so a failed lookup takes
        // roughly the same time as a failed verify — a cheap guard against trivial email-enumeration timing.
        string hashToVerify =
            user?.PasswordHash ?? Hasher.HashPassword(new User(), Guid.NewGuid().ToString());
        PasswordVerificationResult verify = Hasher.VerifyHashedPassword(
            user ?? new User(),
            hashToVerify,
            password
        );
        if (
            user is null
            || user.PasswordHash is null
            || verify == PasswordVerificationResult.Failed
        )
            return Result.Failure<AuthResultDto>(
                "Invalid email or password.",
                "INVALID_CREDENTIALS"
            );

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = Hasher.HashPassword(user, password);

        user.LastSeenAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);

        Guid? broadcasterId = await _db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.OwnerUserId == user.Id)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        Result<SessionTokensDto> session = await _sessions.CreateSessionAsync(
            user.Id,
            broadcasterId,
            context,
            cancellationToken
        );
        if (session.IsFailure)
            return session.WithValue<AuthResultDto>(null!);

        _logger.LogInformation("User {UserId} authenticated via email+password", user.Id);
        return Result.Success(BuildAuthResult(session.Value, user));
    }

    /// <summary>Trim + lower-case for lookup/uniqueness; returns null for anything that doesn't parse as an email.</summary>
    private static string? NormalizeEmail(string email)
    {
        string trimmed = email.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 255)
            return null;
        try
        {
            // System.Net.Mail.MailAddress is a pragmatic, dependency-free format check — not a mailbox proof
            // (no verification email exists yet in this deployment). Good enough to reject "not an email".
            _ = new MailAddress(trimmed);
        }
        catch (FormatException)
        {
            return null;
        }
        return trimmed.ToLowerInvariant();
    }

    private static AuthResultDto BuildAuthResult(SessionTokensDto session, User user) =>
        new(
            session.AccessToken,
            session.RawRefreshToken,
            session.AccessExpiresAt,
            new UserDto(
                user.Id.ToString(),
                user.Username,
                user.DisplayName,
                user.ProfileImageUrl,
                null,
                user.CreatedAt,
                user.UpdatedAt
            )
        );
}
