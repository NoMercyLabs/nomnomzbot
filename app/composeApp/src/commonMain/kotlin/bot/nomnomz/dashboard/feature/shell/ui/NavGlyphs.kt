// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.ui

import org.jetbrains.compose.resources.DrawableResource
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcons

/** Navigation / section icons for the shell, aliased to the designer's exported pack ([AppIcons]). */
val DashboardGlyph: DrawableResource = AppIcons.GridInterfaceHeader
val ChatGlyph: DrawableResource = AppIcons.Comments
val CommandsGlyph: DrawableResource = AppIcons.Terminal
val EventResponsesGlyph: DrawableResource = AppIcons.MessageSettings
val PipelinesGlyph: DrawableResource = AppIcons.FlowChart
val TimersGlyph: DrawableResource = AppIcons.TimerClock
val QuotesGlyph: DrawableResource = AppIcons.CreativeQuoteOpen
val PickListsGlyph: DrawableResource = AppIcons.ListBox
val CodeScriptsGlyph: DrawableResource = AppIcons.TerminalBox
val ModerationGlyph: DrawableResource = AppIcons.Shield
// The three sibling moderation pages carry their JOB, not a variation on the shield: an inbox for the
// items awaiting a decision, a filter for the rules that decide automatically, a clock for what already
// happened. Four near-identical shields in one nav group would be unreadable at a glance.
val ModerationQueueGlyph: DrawableResource = AppIcons.Inbox
val ModerationRulesGlyph: DrawableResource = AppIcons.Filter
val ModerationHistoryGlyph: DrawableResource = AppIcons.History
val RewardsGlyph: DrawableResource = AppIcons.Gift
val EconomyGlyph: DrawableResource = AppIcons.Coins
val GamesGlyph: DrawableResource = AppIcons.Game
val GiveawaysGlyph: DrawableResource = AppIcons.GiftCard
val SupportersGlyph: DrawableResource = AppIcons.Heart
val MusicGlyph: DrawableResource = AppIcons.SongsLibrary
val SongRequestsGlyph: DrawableResource = AppIcons.SongsLibraryNote
val TtsGlyph: DrawableResource = AppIcons.Speaker
val SoundClipsGlyph: DrawableResource = AppIcons.Clips
val AssetsGlyph: DrawableResource = AppIcons.Image
val WidgetsGlyph: DrawableResource = AppIcons.Category
val AlertsGlyph: DrawableResource = AppIcons.Notification
val AnalyticsGlyph: DrawableResource = AppIcons.Chart
val CommunityGlyph: DrawableResource = AppIcons.UsersGroup
val DiscordGlyph: DrawableResource = AppIcons.Discord
val IntegrationsGlyph: DrawableResource = AppIcons.ConnectingCable
val RolesGlyph: DrawableResource = AppIcons.ShieldProfile
val FeaturesGlyph: DrawableResource = AppIcons.CategorySquare
val WebhooksGlyph: DrawableResource = AppIcons.Link2
val FederationGlyph: DrawableResource = AppIcons.NetworkWorld
val CustomEventsGlyph: DrawableResource = AppIcons.Calendar
val SettingsGlyph: DrawableResource = AppIcons.Setting
val AdminGlyph: DrawableResource = AppIcons.Key
val ObsGlyph: DrawableResource = AppIcons.Camera
val VtsGlyph: DrawableResource = AppIcons.Face
val AutomationGlyph: DrawableResource = AppIcons.BotFlow
val MediaShareGlyph: DrawableResource = AppIcons.Share2
val MyDataGlyph: DrawableResource = AppIcons.Paper
val BundlesGlyph: DrawableResource = AppIcons.DeliveryBox1
