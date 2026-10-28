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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

[Flags]
internal enum BotSources
{
    None = 0,
    Identity = 1,
    Builtins = 2,
    Timers = 4,
    Moderator = 8,
    Origin = 16,
}

/// <summary>
/// The bot data of one render. Every read starts once, on the first ask, and every row shares its answer.
/// A read that fails gives no value, so the rows print nothing instead of a made-up number.
/// </summary>
internal sealed class BotData
{
    private const string ItemKey = "bot.data";

    private readonly IServiceScopeFactory _scopes;
    private readonly IBotIdentityDirectory _identity;
    private readonly TemplateRenderScope _scope;
    private readonly Lazy<Task<string>> _name;
    private readonly Lazy<Task<IReadOnlyList<BotCommand>?>> _builtins;
    private readonly Lazy<Task<int?>> _enabledTimers;
    private readonly Lazy<Task<BotModeratorStanding?>> _moderator;
    private readonly Lazy<Task<string?>> _origin;

    private BotData(
        IServiceScopeFactory scopes,
        IBotIdentityDirectory identity,
        IBotProcessInfo process,
        TemplateRenderScope scope
    )
    {
        _scopes = scopes;
        _identity = identity;
        _scope = scope;
        Process = process;
        _name = new(LoadNameAsync);
        _builtins = new(LoadBuiltinsAsync);
        _enabledTimers = new(LoadEnabledTimersAsync);
        _moderator = new(LoadModeratorAsync);
        _origin = new(LoadOriginAsync);
    }

    public IBotProcessInfo Process { get; }

    /// <summary>The render's one bot data.</summary>
    public static BotData For(
        TemplateRenderScope scope,
        IServiceScopeFactory scopes,
        IBotIdentityDirectory identity,
        IBotProcessInfo process
    )
    {
        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is BotData data)
            return data;

        BotData created = new(scopes, identity, process, scope);
        scope.Items[ItemKey] = created;
        return created;
    }

    public Task<string> NameAsync() => _name.Value;

    /// <summary>The enabled built-ins as the channel sees them; null when the read fails.</summary>
    public Task<IReadOnlyList<BotCommand>?> BuiltinsAsync() => _builtins.Value;

    /// <summary>How many timers of the channel are enabled; null when the read fails.</summary>
    public Task<int?> EnabledTimersAsync() => _enabledTimers.Value;

    /// <summary>Where the bot stands as a moderator; null when the read fails.</summary>
    public Task<BotModeratorStanding?> ModeratorAsync() => _moderator.Value;

    /// <summary>The public origin the operator configured (<c>App:BaseUrl</c>); null when none is public.</summary>
    public Task<string?> OriginAsync() => _origin.Value;

    /// <summary>Starts the reads of the given sources side by side.</summary>
    public Task WarmAsync(BotSources sources)
    {
        List<Task> started = [];
        if (sources.HasFlag(BotSources.Identity))
            started.Add(NameAsync());
        if (sources.HasFlag(BotSources.Builtins))
            started.Add(BuiltinsAsync());
        if (sources.HasFlag(BotSources.Timers))
            started.Add(EnabledTimersAsync());
        if (sources.HasFlag(BotSources.Moderator))
            started.Add(ModeratorAsync());
        if (sources.HasFlag(BotSources.Origin))
            started.Add(OriginAsync());
        return Task.WhenAll(started);
    }

    private async Task<string> LoadNameAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        return await _identity.NameAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private async Task<IReadOnlyList<BotCommand>?> LoadBuiltinsAsync()
    {
        if (_scope.Channel is not { } channel)
            return null;

        using IServiceScope scope = _scopes.CreateScope();
        Result<IReadOnlyList<BuiltinCommandDto>> list = await scope
            .ServiceProvider.GetRequiredService<IBuiltinCommandService>()
            .ListAsync(channel.BroadcasterId.ToString(), CancellationToken.None);
        return list.IsSuccess ? BotCommands.Builtins(list.Value, channel.CommandPrefix) : null;
    }

    private async Task<int?> LoadEnabledTimersAsync()
    {
        if (_scope.BroadcasterId is not { } broadcasterId)
            return null;

        using IServiceScope scope = _scopes.CreateScope();
        Result<PagedList<TimerListItem>> timers = await scope
            .ServiceProvider.GetRequiredService<ITimerManagementService>()
            .ListAsync(
                broadcasterId.ToString(),
                new PaginationParams(Page: 1, PageSize: PaginationParams.MaxPageSize),
                CancellationToken.None
            );
        return timers.IsSuccess ? timers.Value.Items.Count(t => t.IsEnabled) : null;
    }

    private async Task<BotModeratorStanding?> LoadModeratorAsync()
    {
        if (_scope.BroadcasterId is not { } broadcasterId)
            return null;

        using IServiceScope scope = _scopes.CreateScope();
        BotModeratorReading reading = await scope
            .ServiceProvider.GetRequiredService<IBotModeratorStatusReader>()
            .ReadAsync(broadcasterId, CancellationToken.None);
        return reading.Standing;
    }

    private Task<string?> LoadOriginAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        string? configured = scope.ServiceProvider.GetService<IConfiguration>()?["App:BaseUrl"];
        return Task.FromResult(BotText.PublicOrigin(configured));
    }
}
