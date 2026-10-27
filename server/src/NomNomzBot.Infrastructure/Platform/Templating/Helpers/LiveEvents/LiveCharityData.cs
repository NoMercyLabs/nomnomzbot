// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The render's one charity campaign read and its one donations page; every <c>charity.*</c> token shares them.</summary>
internal sealed class LiveCharityData : ILiveEventData
{
    private const string ItemKey = "charity.data";

    private readonly IServiceScopeFactory _scopes;
    private readonly Guid _broadcasterId;
    private readonly Lazy<Task<TwitchCharityCampaign?>> _campaign;
    private readonly Lazy<Task<TwitchPage<TwitchCharityDonation>?>> _donations;

    private LiveCharityData(IServiceScopeFactory scopes, Guid broadcasterId)
    {
        _scopes = scopes;
        _broadcasterId = broadcasterId;
        _campaign = new Lazy<Task<TwitchCharityCampaign?>>(LoadCampaignAsync);
        _donations = new Lazy<Task<TwitchPage<TwitchCharityDonation>?>>(LoadDonationsAsync);
    }

    /// <summary>The render's one data object; null when the render has no channel.</summary>
    public static LiveCharityData? For(TemplateRenderScope scope, IServiceScopeFactory scopes)
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is LiveCharityData data)
            return data;

        LiveCharityData created = new(scopes, broadcasterId);
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>The active campaign; null when the channel runs none.</summary>
    public Task<TwitchCharityCampaign?> CampaignAsync() => _campaign.Value;

    /// <summary>The newest page of donations to the campaign (newest first); null when there is no campaign.</summary>
    public Task<TwitchPage<TwitchCharityDonation>?> DonationsAsync() => _donations.Value;

    private async Task<TwitchCharityCampaign?> LoadCampaignAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ITwitchCharityApi api = scope.ServiceProvider.GetRequiredService<ITwitchCharityApi>();
        Result<TwitchCharityCampaign> campaign = await api.GetCharityCampaignAsync(_broadcasterId);
        return campaign.IsSuccess ? campaign.Value : null;
    }

    private async Task<TwitchPage<TwitchCharityDonation>?> LoadDonationsAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ITwitchCharityApi api = scope.ServiceProvider.GetRequiredService<ITwitchCharityApi>();
        Result<TwitchPage<TwitchCharityDonation>> donations =
            await api.GetCharityCampaignDonationsAsync(_broadcasterId, new TwitchPageRequest());
        return donations.IsSuccess ? donations.Value : null;
    }
}
