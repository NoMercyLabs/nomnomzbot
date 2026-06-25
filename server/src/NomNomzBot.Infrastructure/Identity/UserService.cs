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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

public class UserService : IUserService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IServiceScopeFactory _scopeFactory;

    public UserService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IServiceScopeFactory scopeFactory
    )
    {
        _db = db;
        _currentUser = currentUser;
        _scopeFactory = scopeFactory;
    }

    public async Task<Result<CurrentUserDto>> GetCurrentUserAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Errors.NotAuthenticated().ToTyped<CurrentUserDto>();

        if (!Guid.TryParse(_currentUser.UserId, out Guid currentUserId))
            return Errors.NotFound<CurrentUserDto>("User", _currentUser.UserId);

        CurrentUserDto? user = await _db
            .Users.Where(u => u.Id == currentUserId)
            .Select(u => new CurrentUserDto(
                u.Id.ToString(),
                u.Username,
                u.DisplayName,
                u.ProfileImageUrl,
                u.Color,
                u.BroadcasterType,
                u.IsPlatformPrincipal,
                u.CreatedAt
            ))
            .FirstOrDefaultAsync(cancellationToken);

        return user is null
            ? Errors.NotFound<CurrentUserDto>("User", _currentUser.UserId)
            : Result.Success(user);
    }

    public async Task<Result<UserDto>> GetOrCreateAsync(
        string platformUserId,
        string username,
        string displayName,
        CancellationToken cancellationToken = default
    )
    {
        // Use a dedicated scope so this call never races with other DB operations on the caller's
        // scoped context (e.g. when the membership seeder calls GetOrCreateAsync inside a loop
        // while the Helix token resolver runs a concurrent query on the same scope's DbContext).
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // platformUserId is the external Twitch user id (the id seen in chat), not the internal key.
        User? user = await db.Users.FirstOrDefaultAsync(
            u => u.TwitchUserId == platformUserId,
            cancellationToken
        );

        if (user is null)
        {
            user = new()
            {
                TwitchUserId = platformUserId,
                Username = username,
                UsernameNormalized = username.ToLowerInvariant(),
                DisplayName = displayName,
                Enabled = true,
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            user.Username = username;
            user.UsernameNormalized = username.ToLowerInvariant();
            user.DisplayName = displayName;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(ToDto(user));
    }

    public async Task<Result<UserProfileDto>> UpdateProfileAsync(
        string userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(userId, out Guid userGuid))
            return Errors.NotFound<UserProfileDto>("User", userId);

        User? user = await _db
            .Users.Include(u => u.Pronoun)
            .FirstOrDefaultAsync(u => u.Id == userGuid, cancellationToken);

        if (user is null)
            return Errors.NotFound<UserProfileDto>("User", userId);

        if (request.DisplayName is not null)
            user.DisplayName = request.DisplayName;

        if (request.PronounId is not null)
        {
            // 0 clears the pronoun; any positive id resolves against the Pronouns table.
            if (request.PronounId == 0)
            {
                user.Pronoun = null;
                user.PronounManualOverride = false;
            }
            else
            {
                Pronoun? pronoun = await _db.Pronouns.FindAsync(
                    [request.PronounId.Value],
                    cancellationToken
                );
                if (pronoun is null)
                    return Errors.NotFound<UserProfileDto>(
                        "Pronoun",
                        request.PronounId.ToString()!
                    );
                user.Pronoun = pronoun;
                user.PronounManualOverride = true;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToProfileDto(user));
    }

    public async Task<Result<PagedList<UserSearchResult>>> SearchAsync(
        string query,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<User> dbQuery = _db.Users.Where(u =>
            u.Username.Contains(query) || u.DisplayName.Contains(query)
        );

        int total = await dbQuery.CountAsync(cancellationToken);

        List<UserSearchResult> items = await dbQuery
            .OrderBy(u => u.Username)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(u => new UserSearchResult(
                u.Id.ToString(),
                u.Username,
                u.DisplayName,
                u.ProfileImageUrl
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<UserSearchResult>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<UserDto>> GetAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(userId, out Guid userGuid))
            return Errors.NotFound<UserDto>("User", userId);

        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userGuid, cancellationToken);

        if (user is null)
            return Errors.NotFound<UserDto>("User", userId);

        return Result.Success(ToDto(user));
    }

    public async Task<Result<UserProfileDto>> GetProfileAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(userId, out Guid userGuid))
            return Errors.NotFound<UserProfileDto>("User", userId);

        User? user = await _db
            .Users.Include(u => u.Pronoun)
            .FirstOrDefaultAsync(u => u.Id == userGuid, cancellationToken);

        if (user is null)
            return Errors.NotFound<UserProfileDto>("User", userId);

        return Result.Success(ToProfileDto(user));
    }

    public async Task<Result<UserStatsDto>> GetStatsAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(userId, out Guid userGuid))
            return Errors.NotFound<UserStatsDto>("User", userId);

        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userGuid, cancellationToken);

        if (user is null)
            return Errors.NotFound<UserStatsDto>("User", userId);

        // ChatMessage.UserId and WatchStreak.UserId are Twitch platform user IDs, not internal Guids.
        string twitchUserId = user.TwitchUserId;

        int messageCount = await _db.ChatMessages.CountAsync(
            m => m.UserId == twitchUserId,
            cancellationToken
        );

        int watchStreakCount = await _db.WatchStreaks.CountAsync(
            w => w.UserId == twitchUserId,
            cancellationToken
        );

        int moderatorChannels = await _db.ChannelModerators.CountAsync(
            m => m.UserId == userGuid,
            cancellationToken
        );

        bool ownsChannel = await _db.Channels.AnyAsync(
            c => c.OwnerUserId == userGuid,
            cancellationToken
        );

        int channelsCount = moderatorChannels + (ownsChannel ? 1 : 0);

        UserStatsDto dto = new(
            messageCount,
            0,
            channelsCount,
            0,
            user.CreatedAt,
            user.UpdatedAt,
            true
        );

        return Result.Success(dto);
    }

    public async Task<Result<List<UserChannelAppearanceDto>>> GetUserChannelsAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        Guid.TryParse(userId, out Guid userGuid);

        // Channels the user has sent messages in
        var messageData = await _db
            .ChatMessages.Where(m => m.UserId == userId)
            .GroupBy(m => m.BroadcasterId)
            .Select(g => new
            {
                ChannelId = g.Key,
                MessageCount = g.Count(),
                FirstSeen = g.Min(m => m.CreatedAt),
            })
            .ToListAsync(cancellationToken);

        // Channels where the user is a moderator
        List<Guid> modChannelIds = await _db
            .ChannelModerators.Where(cm => cm.UserId == userGuid)
            .Select(cm => cm.ChannelId)
            .ToListAsync(cancellationToken);

        List<Guid> allChannelIds = messageData
            .Select(c => c.ChannelId)
            .Union(modChannelIds)
            .Distinct()
            .ToList();

        if (allChannelIds.Count == 0)
            return Result.Success(new List<UserChannelAppearanceDto>());

        var channelNames = await _db
            .Channels.Where(c => allChannelIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var watchStreaks = await _db
            .WatchStreaks.Where(w => w.UserId == userId && allChannelIds.Contains(w.BroadcasterId))
            .Select(w => new { w.BroadcasterId, w.CurrentStreak })
            .ToListAsync(cancellationToken);

        List<UserChannelAppearanceDto> result = channelNames
            .Select(ch =>
            {
                var msgs = messageData.FirstOrDefault(m => m.ChannelId == ch.Id);
                var streak = watchStreaks.FirstOrDefault(w => w.BroadcasterId == ch.Id);

                string followDate = msgs is not null
                    ? msgs.FirstSeen.ToString("yyyy-MM-dd")
                    : "N/A";

                string watchTime = streak is not null
                    ? $"{streak.CurrentStreak} day streak"
                    : "N/A";

                return new UserChannelAppearanceDto(
                    ch.Id.ToString(),
                    ch.Id.ToString(),
                    ch.Name,
                    followDate,
                    msgs is not null ? msgs.MessageCount : 0,
                    watchTime
                );
            })
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<PagedList<AdminUserDto>>> ListAdminUsersAsync(
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<User> query = _db.Users.Include(u => u.Channel);

        int total = await query.CountAsync(cancellationToken);

        List<AdminUserDto> items = await query
            .OrderBy(u => u.Username)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(u => new AdminUserDto(
                u.Id.ToString(),
                u.DisplayName,
                u.Username,
                null,
                u.Channel != null ? "moderator" : "user",
                u.Channel != null ? 1 : 0,
                u.CreatedAt,
                (DateTime?)u.UpdatedAt
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<AdminUserDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    private static UserDto ToDto(User u) =>
        new(
            u.Id.ToString(),
            u.Username,
            u.DisplayName,
            u.ProfileImageUrl,
            null,
            u.CreatedAt,
            u.UpdatedAt
        );

    private static UserProfileDto ToProfileDto(User u) =>
        new(
            u.Id.ToString(),
            u.Username,
            u.DisplayName,
            u.ProfileImageUrl,
            null,
            u.Pronoun?.Name,
            u.PronounId,
            u.CreatedAt,
            u.UpdatedAt
        );
}
