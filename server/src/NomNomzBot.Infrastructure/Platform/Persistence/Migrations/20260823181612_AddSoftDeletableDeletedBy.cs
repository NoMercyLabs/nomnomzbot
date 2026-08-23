using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeletableDeletedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "YouTubeLiveChatBans",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Widgets",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "WidgetGalleryItems",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "VtsConnections",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerReports",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerProfiles",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerEngagementStates",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerData",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerAgeConsents",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "UserIdentities",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "TtsLexiconEntries",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "TtsConfigs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "TtsApprovalQueueEntries",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Timers",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "TierLimits",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "SupporterEvents",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "SupporterConnections",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Subscriptions",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "SoundClips",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "SavingsJars",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "SavingsJarMemberships",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Rewards",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Records",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Quotes",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Pipelines",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "PickLists",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "PermitGrants",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Permissions",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "OutboundWebhookEndpoints",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ObsConnections",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "NetworkNukeBatches",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "NamedCounters",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ModerationEscalationPolicies",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "MediaShareRequests",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "MediaShareConfigs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "LeaderboardOptOuts",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "LeaderboardConfigs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "IpcDevModeKeys",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Invoices",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "InviteCodes",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "IntegrationTokens",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "IntegrationConnections",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "InstalledBundles",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "InboundWebhookEndpoints",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "IamRoles",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "IamPrincipals",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "HttpEgressAllowlists",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Giveaways",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "GiveawayEntries",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "GiveawayCodes",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "GiveawayCodePools",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "GameSessions",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "GameConfigs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "FederationPeers",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "EventSubSubscriptions",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "EventResponses",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "EngagementConfigs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "EarningRules",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordNotificationRoles",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordNotificationConfigs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordMemberOptIns",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordGuildConnections",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "CustomDataSources",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "CurrencyConfigs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "CurrencyAccounts",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ConsentRecords",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Commands",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "CodeScripts",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChatTriggers",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChatMessages",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChatFilters",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Channels",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelModerators",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelMemberships",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelFederationOptIns",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelBuiltinCommands",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelBotAuthorizations",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelAssets",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelActionOverrides",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "CatalogItems",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "BotAccounts",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "BlockedTracks",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "BillingTiers",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "AutomationApiTokens",
                type: "uuid",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DeletedBy", table: "YouTubeLiveChatBans");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Widgets");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "WidgetGalleryItems");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "VtsConnections");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ViewerReports");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ViewerProfiles");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ViewerEngagementStates");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ViewerData");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ViewerAgeConsents");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "UserIdentities");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "TtsLexiconEntries");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "TtsConfigs");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "TtsApprovalQueueEntries");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Timers");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "TierLimits");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "SupporterEvents");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "SupporterConnections");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Subscriptions");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "SoundClips");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "SavingsJars");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "SavingsJarMemberships");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Rewards");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Records");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Quotes");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Pipelines");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "PickLists");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "PermitGrants");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Permissions");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "OutboundWebhookEndpoints");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ObsConnections");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "NetworkNukeBatches");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "NamedCounters");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ModerationEscalationPolicies");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "MediaShareRequests");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "MediaShareConfigs");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "LeaderboardOptOuts");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "LeaderboardConfigs");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "IpcDevModeKeys");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Invoices");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "InviteCodes");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "IntegrationTokens");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "IntegrationConnections");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "InstalledBundles");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "InboundWebhookEndpoints");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "IamRoles");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "IamPrincipals");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "HttpEgressAllowlists");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Giveaways");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "GiveawayEntries");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "GiveawayCodes");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "GiveawayCodePools");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "GameSessions");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "GameConfigs");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "FederationPeers");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "EventSubSubscriptions");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "EventResponses");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "EngagementConfigs");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "EarningRules");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "DiscordNotificationRoles");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "DiscordNotificationConfigs");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "DiscordMemberOptIns");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "DiscordGuildConnections");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "CustomDataSources");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "CurrencyConfigs");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "CurrencyAccounts");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ConsentRecords");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Commands");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "CodeScripts");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChatTriggers");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChatFilters");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "Channels");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChannelModerators");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChannelMemberships");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChannelFederationOptIns");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChannelBuiltinCommands");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChannelBotAuthorizations");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChannelAssets");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "ChannelActionOverrides");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "CatalogItems");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "BotAccounts");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "BlockedTracks");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "BillingTiers");

            migrationBuilder.DropColumn(name: "DeletedBy", table: "AutomationApiTokens");
        }
    }
}
