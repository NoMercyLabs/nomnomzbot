// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.icon

import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.activity
import nomnomzbot.composeapp.generated.resources.add_user
import nomnomzbot.composeapp.generated.resources.arrow_down
import nomnomzbot.composeapp.generated.resources.arrow_jump_left
import nomnomzbot.composeapp.generated.resources.arrow_jump_right
import nomnomzbot.composeapp.generated.resources.arrow_left_circle
import nomnomzbot.composeapp.generated.resources.arrow_right
import nomnomzbot.composeapp.generated.resources.arrow_up
import nomnomzbot.composeapp.generated.resources.arrows_bent_arrow_left_up
import nomnomzbot.composeapp.generated.resources.badoo
import nomnomzbot.composeapp.generated.resources.bag
import nomnomzbot.composeapp.generated.resources.bag_2
import nomnomzbot.composeapp.generated.resources.behance
import nomnomzbot.composeapp.generated.resources.bluesky
import nomnomzbot.composeapp.generated.resources.bookmark
import nomnomzbot.composeapp.generated.resources.bookmark_filled
import nomnomzbot.composeapp.generated.resources.bot_flow
import nomnomzbot.composeapp.generated.resources.calendar
import nomnomzbot.composeapp.generated.resources.calendar_arrow_right
import nomnomzbot.composeapp.generated.resources.camera
import nomnomzbot.composeapp.generated.resources.category
import nomnomzbot.composeapp.generated.resources.category_square
import nomnomzbot.composeapp.generated.resources.chart
import nomnomzbot.composeapp.generated.resources.check_circle
import nomnomzbot.composeapp.generated.resources.checkmark
import nomnomzbot.composeapp.generated.resources.checkmarks
import nomnomzbot.composeapp.generated.resources.chevron_down
import nomnomzbot.composeapp.generated.resources.chevron_left
import nomnomzbot.composeapp.generated.resources.chevron_right
import nomnomzbot.composeapp.generated.resources.chevron_up
import nomnomzbot.composeapp.generated.resources.circle_message_question
import nomnomzbot.composeapp.generated.resources.clips
import nomnomzbot.composeapp.generated.resources.clock_arrow_right
import nomnomzbot.composeapp.generated.resources.close_remove
import nomnomzbot.composeapp.generated.resources.close_square
import nomnomzbot.composeapp.generated.resources.coins
import nomnomzbot.composeapp.generated.resources.colapse
import nomnomzbot.composeapp.generated.resources.comments
import nomnomzbot.composeapp.generated.resources.connecting_cable
import nomnomzbot.composeapp.generated.resources.copy
import nomnomzbot.composeapp.generated.resources.creative_quote_close
import nomnomzbot.composeapp.generated.resources.creative_quote_open
import nomnomzbot.composeapp.generated.resources.cup
import nomnomzbot.composeapp.generated.resources.danger
import nomnomzbot.composeapp.generated.resources.danger_circle
import nomnomzbot.composeapp.generated.resources.delete
import nomnomzbot.composeapp.generated.resources.delivery_box_1
import nomnomzbot.composeapp.generated.resources.digg
import nomnomzbot.composeapp.generated.resources.discord
import nomnomzbot.composeapp.generated.resources.document
import nomnomzbot.composeapp.generated.resources.download
import nomnomzbot.composeapp.generated.resources.dribbble
import nomnomzbot.composeapp.generated.resources.edit
import nomnomzbot.composeapp.generated.resources.edit_square
import nomnomzbot.composeapp.generated.resources.ello_circle
import nomnomzbot.composeapp.generated.resources.email
import nomnomzbot.composeapp.generated.resources.email_open
import nomnomzbot.composeapp.generated.resources.expand
import nomnomzbot.composeapp.generated.resources.face
import nomnomzbot.composeapp.generated.resources.facebook
import nomnomzbot.composeapp.generated.resources.facebook_old
import nomnomzbot.composeapp.generated.resources.figma
import nomnomzbot.composeapp.generated.resources.filter
import nomnomzbot.composeapp.generated.resources.flickr_circle
import nomnomzbot.composeapp.generated.resources.flow_chart
import nomnomzbot.composeapp.generated.resources.forward_60
import nomnomzbot.composeapp.generated.resources.game
import nomnomzbot.composeapp.generated.resources.game_controller_dual
import nomnomzbot.composeapp.generated.resources.gift
import nomnomzbot.composeapp.generated.resources.gift_card
import nomnomzbot.composeapp.generated.resources.github
import nomnomzbot.composeapp.generated.resources.github_circle
import nomnomzbot.composeapp.generated.resources.google_podcast
import nomnomzbot.composeapp.generated.resources.grid_interface_header
import nomnomzbot.composeapp.generated.resources.grooveshark_circle
import nomnomzbot.composeapp.generated.resources.hangout
import nomnomzbot.composeapp.generated.resources.headphone
import nomnomzbot.composeapp.generated.resources.heart
import nomnomzbot.composeapp.generated.resources.heart_filled
import nomnomzbot.composeapp.generated.resources.heart_rate
import nomnomzbot.composeapp.generated.resources.hide
import nomnomzbot.composeapp.generated.resources.hipchat
import nomnomzbot.composeapp.generated.resources.history
import nomnomzbot.composeapp.generated.resources.home
import nomnomzbot.composeapp.generated.resources.image
import nomnomzbot.composeapp.generated.resources.imo
import nomnomzbot.composeapp.generated.resources.inbox
import nomnomzbot.composeapp.generated.resources.info_square
import nomnomzbot.composeapp.generated.resources.instagram
import nomnomzbot.composeapp.generated.resources.key
import nomnomzbot.composeapp.generated.resources.keys
import nomnomzbot.composeapp.generated.resources.kick
import nomnomzbot.composeapp.generated.resources.layers
import nomnomzbot.composeapp.generated.resources.letter
import nomnomzbot.composeapp.generated.resources.line
import nomnomzbot.composeapp.generated.resources.link
import nomnomzbot.composeapp.generated.resources.link_2
import nomnomzbot.composeapp.generated.resources.linkedin
import nomnomzbot.composeapp.generated.resources.list_box
import nomnomzbot.composeapp.generated.resources.loader
import nomnomzbot.composeapp.generated.resources.loader_fade
import nomnomzbot.composeapp.generated.resources.loader_fast
import nomnomzbot.composeapp.generated.resources.location
import nomnomzbot.composeapp.generated.resources.lock
import nomnomzbot.composeapp.generated.resources.login
import nomnomzbot.composeapp.generated.resources.logout
import nomnomzbot.composeapp.generated.resources.mastodon
import nomnomzbot.composeapp.generated.resources.medium
import nomnomzbot.composeapp.generated.resources.megaphone
import nomnomzbot.composeapp.generated.resources.message_filter
import nomnomzbot.composeapp.generated.resources.message_notification
import nomnomzbot.composeapp.generated.resources.message_notification_square
import nomnomzbot.composeapp.generated.resources.message_settings
import nomnomzbot.composeapp.generated.resources.messages
import nomnomzbot.composeapp.generated.resources.messages_2
import nomnomzbot.composeapp.generated.resources.meta
import nomnomzbot.composeapp.generated.resources.meta_messenger
import nomnomzbot.composeapp.generated.resources.mima_review
import nomnomzbot.composeapp.generated.resources.minus
import nomnomzbot.composeapp.generated.resources.money_bag_dollar
import nomnomzbot.composeapp.generated.resources.monitor_display_stand
import nomnomzbot.composeapp.generated.resources.moon
import nomnomzbot.composeapp.generated.resources.moon_big
import nomnomzbot.composeapp.generated.resources.more
import nomnomzbot.composeapp.generated.resources.more_vertical
import nomnomzbot.composeapp.generated.resources.mymind
import nomnomzbot.composeapp.generated.resources.network_world
import nomnomzbot.composeapp.generated.resources.nintendo
import nomnomzbot.composeapp.generated.resources.notification
import nomnomzbot.composeapp.generated.resources.notification_badge
import nomnomzbot.composeapp.generated.resources.notification_mute
import nomnomzbot.composeapp.generated.resources.notification_sleep
import nomnomzbot.composeapp.generated.resources.onlyfans
import nomnomzbot.composeapp.generated.resources.paper
import nomnomzbot.composeapp.generated.resources.paper_download
import nomnomzbot.composeapp.generated.resources.patreon
import nomnomzbot.composeapp.generated.resources.periscope
import nomnomzbot.composeapp.generated.resources.pin
import nomnomzbot.composeapp.generated.resources.pinterest
import nomnomzbot.composeapp.generated.resources.play
import nomnomzbot.composeapp.generated.resources.plus
import nomnomzbot.composeapp.generated.resources.plus_circle
import nomnomzbot.composeapp.generated.resources.pocket
import nomnomzbot.composeapp.generated.resources.profile
import nomnomzbot.composeapp.generated.resources.quora
import nomnomzbot.composeapp.generated.resources.quote_block_close
import nomnomzbot.composeapp.generated.resources.quote_block_open
import nomnomzbot.composeapp.generated.resources.reddit
import nomnomzbot.composeapp.generated.resources.refresh
import nomnomzbot.composeapp.generated.resources.refresh_left
import nomnomzbot.composeapp.generated.resources.reload_left
import nomnomzbot.composeapp.generated.resources.reload_right
import nomnomzbot.composeapp.generated.resources.remove
import nomnomzbot.composeapp.generated.resources.scan
import nomnomzbot.composeapp.generated.resources.scan_security
import nomnomzbot.composeapp.generated.resources.search
import nomnomzbot.composeapp.generated.resources.send
import nomnomzbot.composeapp.generated.resources.send_2
import nomnomzbot.composeapp.generated.resources.setting
import nomnomzbot.composeapp.generated.resources.share
import nomnomzbot.composeapp.generated.resources.share_2
import nomnomzbot.composeapp.generated.resources.shield
import nomnomzbot.composeapp.generated.resources.shield_done
import nomnomzbot.composeapp.generated.resources.shield_profile
import nomnomzbot.composeapp.generated.resources.show
import nomnomzbot.composeapp.generated.resources.sidebar
import nomnomzbot.composeapp.generated.resources.sidebar_colapse
import nomnomzbot.composeapp.generated.resources.sidebar_expand
import nomnomzbot.composeapp.generated.resources.slack_2
import nomnomzbot.composeapp.generated.resources.snapchat
import nomnomzbot.composeapp.generated.resources.songs_library
import nomnomzbot.composeapp.generated.resources.songs_library_note
import nomnomzbot.composeapp.generated.resources.soundcloud
import nomnomzbot.composeapp.generated.resources.source
import nomnomzbot.composeapp.generated.resources.speaker
import nomnomzbot.composeapp.generated.resources.speaker_megaphone_2
import nomnomzbot.composeapp.generated.resources.speaker_megaphone_4
import nomnomzbot.composeapp.generated.resources.speaker_megaphone_5
import nomnomzbot.composeapp.generated.resources.split
import nomnomzbot.composeapp.generated.resources.spotify
import nomnomzbot.composeapp.generated.resources.sun
import nomnomzbot.composeapp.generated.resources.sun_happy
import nomnomzbot.composeapp.generated.resources.swap
import nomnomzbot.composeapp.generated.resources.swarm
import nomnomzbot.composeapp.generated.resources.terminal
import nomnomzbot.composeapp.generated.resources.terminal_box
import nomnomzbot.composeapp.generated.resources.threads
import nomnomzbot.composeapp.generated.resources.ticket
import nomnomzbot.composeapp.generated.resources.tidal
import nomnomzbot.composeapp.generated.resources.tiktok
import nomnomzbot.composeapp.generated.resources.time_square
import nomnomzbot.composeapp.generated.resources.timer_clock
import nomnomzbot.composeapp.generated.resources.translate
import nomnomzbot.composeapp.generated.resources.tumblr
import nomnomzbot.composeapp.generated.resources.twitch
import nomnomzbot.composeapp.generated.resources.twitter
import nomnomzbot.composeapp.generated.resources.unlink
import nomnomzbot.composeapp.generated.resources.unlock
import nomnomzbot.composeapp.generated.resources.update_left
import nomnomzbot.composeapp.generated.resources.update_left_big
import nomnomzbot.composeapp.generated.resources.upwork
import nomnomzbot.composeapp.generated.resources.users_3
import nomnomzbot.composeapp.generated.resources.users_group
import nomnomzbot.composeapp.generated.resources.vimeo
import nomnomzbot.composeapp.generated.resources.vk
import nomnomzbot.composeapp.generated.resources.volume_down
import nomnomzbot.composeapp.generated.resources.volume_off
import nomnomzbot.composeapp.generated.resources.volume_up
import nomnomzbot.composeapp.generated.resources.vote2
import nomnomzbot.composeapp.generated.resources.wattpad
import nomnomzbot.composeapp.generated.resources.wechat
import nomnomzbot.composeapp.generated.resources.whatsapp
import nomnomzbot.composeapp.generated.resources.x_com
import nomnomzbot.composeapp.generated.resources.xbox
import nomnomzbot.composeapp.generated.resources.xiaomi_square
import nomnomzbot.composeapp.generated.resources.yahoo
import nomnomzbot.composeapp.generated.resources.yelp
import nomnomzbot.composeapp.generated.resources.youtube

import org.jetbrains.compose.resources.DrawableResource

/**
 * Typed registry for the designer's exported icon pack (monochrome line/fill, 24×24 viewport).
 *
 * The source SVGs carry a baked colour; render every entry through [AppIcon], which tints them to a
 * theme token so they follow light/dark automatically. Reference icons by their semantic name here
 * (e.g. `AppIcons.ArrowRight`) rather than reaching for `Res.drawable.*` directly, so the pack stays
 * managed in one place. Names mirror the designer's export filenames.
 */
object AppIcons {
    val Activity: DrawableResource = Res.drawable.activity
    val AddUser: DrawableResource = Res.drawable.add_user
    val ArrowDown: DrawableResource = Res.drawable.arrow_down
    val ArrowJumpLeft: DrawableResource = Res.drawable.arrow_jump_left
    val ArrowJumpRight: DrawableResource = Res.drawable.arrow_jump_right
    val ArrowLeftCircle: DrawableResource = Res.drawable.arrow_left_circle
    val ArrowRight: DrawableResource = Res.drawable.arrow_right
    val ArrowUp: DrawableResource = Res.drawable.arrow_up
    val ArrowsBentArrowLeftUp: DrawableResource = Res.drawable.arrows_bent_arrow_left_up
    val Badoo: DrawableResource = Res.drawable.badoo
    val Bag: DrawableResource = Res.drawable.bag
    val Bag2: DrawableResource = Res.drawable.bag_2
    val Behance: DrawableResource = Res.drawable.behance
    val Bluesky: DrawableResource = Res.drawable.bluesky
    val Bookmark: DrawableResource = Res.drawable.bookmark
    val BookmarkFilled: DrawableResource = Res.drawable.bookmark_filled
    val BotFlow: DrawableResource = Res.drawable.bot_flow
    val Calendar: DrawableResource = Res.drawable.calendar
    val CalendarArrowRight: DrawableResource = Res.drawable.calendar_arrow_right
    val Camera: DrawableResource = Res.drawable.camera
    val Category: DrawableResource = Res.drawable.category
    val CategorySquare: DrawableResource = Res.drawable.category_square
    val Chart: DrawableResource = Res.drawable.chart
    val CheckCircle: DrawableResource = Res.drawable.check_circle
    val Checkmark: DrawableResource = Res.drawable.checkmark
    val Checkmarks: DrawableResource = Res.drawable.checkmarks
    val ChevronDown: DrawableResource = Res.drawable.chevron_down
    val ChevronLeft: DrawableResource = Res.drawable.chevron_left
    val ChevronRight: DrawableResource = Res.drawable.chevron_right
    val ChevronUp: DrawableResource = Res.drawable.chevron_up
    val CircleMessageQuestion: DrawableResource = Res.drawable.circle_message_question
    val Clips: DrawableResource = Res.drawable.clips
    val ClockArrowRight: DrawableResource = Res.drawable.clock_arrow_right
    val CloseRemove: DrawableResource = Res.drawable.close_remove
    val CloseSquare: DrawableResource = Res.drawable.close_square
    val Coins: DrawableResource = Res.drawable.coins
    val Colapse: DrawableResource = Res.drawable.colapse
    val Comments: DrawableResource = Res.drawable.comments
    val ConnectingCable: DrawableResource = Res.drawable.connecting_cable
    val Copy: DrawableResource = Res.drawable.copy
    val CreativeQuoteClose: DrawableResource = Res.drawable.creative_quote_close
    val CreativeQuoteOpen: DrawableResource = Res.drawable.creative_quote_open
    val Cup: DrawableResource = Res.drawable.cup
    val Danger: DrawableResource = Res.drawable.danger
    val DangerCircle: DrawableResource = Res.drawable.danger_circle
    val Delete: DrawableResource = Res.drawable.delete
    val DeliveryBox1: DrawableResource = Res.drawable.delivery_box_1
    val Digg: DrawableResource = Res.drawable.digg
    val Discord: DrawableResource = Res.drawable.discord
    val Document: DrawableResource = Res.drawable.document
    val Download: DrawableResource = Res.drawable.download
    val Dribbble: DrawableResource = Res.drawable.dribbble
    val Edit: DrawableResource = Res.drawable.edit
    val EditSquare: DrawableResource = Res.drawable.edit_square
    val ElloCircle: DrawableResource = Res.drawable.ello_circle
    val Email: DrawableResource = Res.drawable.email
    val EmailOpen: DrawableResource = Res.drawable.email_open
    val Expand: DrawableResource = Res.drawable.expand
    val Face: DrawableResource = Res.drawable.face
    val Facebook: DrawableResource = Res.drawable.facebook
    val FacebookOld: DrawableResource = Res.drawable.facebook_old
    val Figma: DrawableResource = Res.drawable.figma
    val Filter: DrawableResource = Res.drawable.filter
    val FlickrCircle: DrawableResource = Res.drawable.flickr_circle
    val FlowChart: DrawableResource = Res.drawable.flow_chart
    val Forward60: DrawableResource = Res.drawable.forward_60
    val Game: DrawableResource = Res.drawable.game
    val GameControllerDual: DrawableResource = Res.drawable.game_controller_dual
    val Gift: DrawableResource = Res.drawable.gift
    val GiftCard: DrawableResource = Res.drawable.gift_card
    val Github: DrawableResource = Res.drawable.github
    val GithubCircle: DrawableResource = Res.drawable.github_circle
    val GooglePodcast: DrawableResource = Res.drawable.google_podcast
    val GridInterfaceHeader: DrawableResource = Res.drawable.grid_interface_header
    val GroovesharkCircle: DrawableResource = Res.drawable.grooveshark_circle
    val Hangout: DrawableResource = Res.drawable.hangout
    val Headphone: DrawableResource = Res.drawable.headphone
    val Heart: DrawableResource = Res.drawable.heart
    val HeartFilled: DrawableResource = Res.drawable.heart_filled
    val HeartRate: DrawableResource = Res.drawable.heart_rate
    val Hide: DrawableResource = Res.drawable.hide
    val Hipchat: DrawableResource = Res.drawable.hipchat
    val History: DrawableResource = Res.drawable.history
    val Home: DrawableResource = Res.drawable.home
    val Image: DrawableResource = Res.drawable.image
    val Imo: DrawableResource = Res.drawable.imo
    val Inbox: DrawableResource = Res.drawable.inbox
    val InfoSquare: DrawableResource = Res.drawable.info_square
    val Instagram: DrawableResource = Res.drawable.instagram
    val Key: DrawableResource = Res.drawable.key
    val Keys: DrawableResource = Res.drawable.keys
    val Kick: DrawableResource = Res.drawable.kick
    val Layers: DrawableResource = Res.drawable.layers
    val Letter: DrawableResource = Res.drawable.letter
    val Line: DrawableResource = Res.drawable.line
    val Link: DrawableResource = Res.drawable.link
    val Link2: DrawableResource = Res.drawable.link_2
    val Linkedin: DrawableResource = Res.drawable.linkedin
    val ListBox: DrawableResource = Res.drawable.list_box
    val Loader: DrawableResource = Res.drawable.loader
    val LoaderFade: DrawableResource = Res.drawable.loader_fade
    val LoaderFast: DrawableResource = Res.drawable.loader_fast
    val Location: DrawableResource = Res.drawable.location
    val Lock: DrawableResource = Res.drawable.lock
    val Login: DrawableResource = Res.drawable.login
    val Logout: DrawableResource = Res.drawable.logout
    val Mastodon: DrawableResource = Res.drawable.mastodon
    val Medium: DrawableResource = Res.drawable.medium
    val Megaphone: DrawableResource = Res.drawable.megaphone
    val MessageFilter: DrawableResource = Res.drawable.message_filter
    val MessageNotification: DrawableResource = Res.drawable.message_notification
    val MessageNotificationSquare: DrawableResource = Res.drawable.message_notification_square
    val MessageSettings: DrawableResource = Res.drawable.message_settings
    val Messages: DrawableResource = Res.drawable.messages
    val Messages2: DrawableResource = Res.drawable.messages_2
    val Meta: DrawableResource = Res.drawable.meta
    val MetaMessenger: DrawableResource = Res.drawable.meta_messenger
    val MimaReview: DrawableResource = Res.drawable.mima_review
    val Minus: DrawableResource = Res.drawable.minus
    val MoneyBagDollar: DrawableResource = Res.drawable.money_bag_dollar
    val MonitorDisplayStand: DrawableResource = Res.drawable.monitor_display_stand
    val Moon: DrawableResource = Res.drawable.moon
    val MoonBig: DrawableResource = Res.drawable.moon_big
    val More: DrawableResource = Res.drawable.more
    val MoreVertical: DrawableResource = Res.drawable.more_vertical
    val Mymind: DrawableResource = Res.drawable.mymind
    val NetworkWorld: DrawableResource = Res.drawable.network_world
    val Nintendo: DrawableResource = Res.drawable.nintendo
    val Notification: DrawableResource = Res.drawable.notification
    val NotificationBadge: DrawableResource = Res.drawable.notification_badge
    val NotificationMute: DrawableResource = Res.drawable.notification_mute
    val NotificationSleep: DrawableResource = Res.drawable.notification_sleep
    val Onlyfans: DrawableResource = Res.drawable.onlyfans
    val Paper: DrawableResource = Res.drawable.paper
    val PaperDownload: DrawableResource = Res.drawable.paper_download
    val Patreon: DrawableResource = Res.drawable.patreon
    val Periscope: DrawableResource = Res.drawable.periscope
    val Pin: DrawableResource = Res.drawable.pin
    val Pinterest: DrawableResource = Res.drawable.pinterest
    val Play: DrawableResource = Res.drawable.play
    val Plus: DrawableResource = Res.drawable.plus
    val PlusCircle: DrawableResource = Res.drawable.plus_circle
    val Pocket: DrawableResource = Res.drawable.pocket
    val Profile: DrawableResource = Res.drawable.profile
    val Quora: DrawableResource = Res.drawable.quora
    val QuoteBlockClose: DrawableResource = Res.drawable.quote_block_close
    val QuoteBlockOpen: DrawableResource = Res.drawable.quote_block_open
    val Reddit: DrawableResource = Res.drawable.reddit
    val Refresh: DrawableResource = Res.drawable.refresh
    val RefreshLeft: DrawableResource = Res.drawable.refresh_left
    val ReloadLeft: DrawableResource = Res.drawable.reload_left
    val ReloadRight: DrawableResource = Res.drawable.reload_right
    val Remove: DrawableResource = Res.drawable.remove
    val Scan: DrawableResource = Res.drawable.scan
    val ScanSecurity: DrawableResource = Res.drawable.scan_security
    val Search: DrawableResource = Res.drawable.search
    val Send: DrawableResource = Res.drawable.send
    val Send2: DrawableResource = Res.drawable.send_2
    val Setting: DrawableResource = Res.drawable.setting
    val Share: DrawableResource = Res.drawable.share
    val Share2: DrawableResource = Res.drawable.share_2
    val Shield: DrawableResource = Res.drawable.shield
    val ShieldDone: DrawableResource = Res.drawable.shield_done
    val ShieldProfile: DrawableResource = Res.drawable.shield_profile
    val Show: DrawableResource = Res.drawable.show
    val Sidebar: DrawableResource = Res.drawable.sidebar
    val SidebarColapse: DrawableResource = Res.drawable.sidebar_colapse
    val SidebarExpand: DrawableResource = Res.drawable.sidebar_expand
    val Slack2: DrawableResource = Res.drawable.slack_2
    val Snapchat: DrawableResource = Res.drawable.snapchat
    val SongsLibrary: DrawableResource = Res.drawable.songs_library
    val SongsLibraryNote: DrawableResource = Res.drawable.songs_library_note
    val Soundcloud: DrawableResource = Res.drawable.soundcloud
    val Source: DrawableResource = Res.drawable.source
    val Speaker: DrawableResource = Res.drawable.speaker
    val SpeakerMegaphone2: DrawableResource = Res.drawable.speaker_megaphone_2
    val SpeakerMegaphone4: DrawableResource = Res.drawable.speaker_megaphone_4
    val SpeakerMegaphone5: DrawableResource = Res.drawable.speaker_megaphone_5
    val Split: DrawableResource = Res.drawable.split
    val Spotify: DrawableResource = Res.drawable.spotify
    val Sun: DrawableResource = Res.drawable.sun
    val SunHappy: DrawableResource = Res.drawable.sun_happy
    val Swap: DrawableResource = Res.drawable.swap
    val Swarm: DrawableResource = Res.drawable.swarm
    val Terminal: DrawableResource = Res.drawable.terminal
    val TerminalBox: DrawableResource = Res.drawable.terminal_box
    val Threads: DrawableResource = Res.drawable.threads
    val Ticket: DrawableResource = Res.drawable.ticket
    val Tidal: DrawableResource = Res.drawable.tidal
    val Tiktok: DrawableResource = Res.drawable.tiktok
    val TimeSquare: DrawableResource = Res.drawable.time_square
    val TimerClock: DrawableResource = Res.drawable.timer_clock
    val Translate: DrawableResource = Res.drawable.translate
    val Tumblr: DrawableResource = Res.drawable.tumblr
    val Twitch: DrawableResource = Res.drawable.twitch
    val Twitter: DrawableResource = Res.drawable.twitter
    val Unlink: DrawableResource = Res.drawable.unlink
    val Unlock: DrawableResource = Res.drawable.unlock
    val UpdateLeft: DrawableResource = Res.drawable.update_left
    val UpdateLeftBig: DrawableResource = Res.drawable.update_left_big
    val Upwork: DrawableResource = Res.drawable.upwork
    val Users3: DrawableResource = Res.drawable.users_3
    val UsersGroup: DrawableResource = Res.drawable.users_group
    val Vimeo: DrawableResource = Res.drawable.vimeo
    val Vk: DrawableResource = Res.drawable.vk
    val VolumeDown: DrawableResource = Res.drawable.volume_down
    val VolumeOff: DrawableResource = Res.drawable.volume_off
    val VolumeUp: DrawableResource = Res.drawable.volume_up
    val Vote2: DrawableResource = Res.drawable.vote2
    val Wattpad: DrawableResource = Res.drawable.wattpad
    val Wechat: DrawableResource = Res.drawable.wechat
    val Whatsapp: DrawableResource = Res.drawable.whatsapp
    val XCom: DrawableResource = Res.drawable.x_com
    val Xbox: DrawableResource = Res.drawable.xbox
    val XiaomiSquare: DrawableResource = Res.drawable.xiaomi_square
    val Yahoo: DrawableResource = Res.drawable.yahoo
    val Yelp: DrawableResource = Res.drawable.yelp
    val Youtube: DrawableResource = Res.drawable.youtube

    /** Every pack icon — used by [IconPreload] to warm the async SVG cache at startup. */
    val all: List<DrawableResource> = listOf(
        Activity,
        AddUser,
        ArrowDown,
        ArrowJumpLeft,
        ArrowJumpRight,
        ArrowLeftCircle,
        ArrowRight,
        ArrowUp,
        ArrowsBentArrowLeftUp,
        Badoo,
        Bag,
        Bag2,
        Behance,
        Bluesky,
        Bookmark,
        BookmarkFilled,
        BotFlow,
        Calendar,
        CalendarArrowRight,
        Camera,
        Category,
        CategorySquare,
        Chart,
        CheckCircle,
        Checkmark,
        Checkmarks,
        ChevronDown,
        ChevronLeft,
        ChevronRight,
        ChevronUp,
        CircleMessageQuestion,
        Clips,
        ClockArrowRight,
        CloseRemove,
        CloseSquare,
        Coins,
        Colapse,
        Comments,
        ConnectingCable,
        Copy,
        CreativeQuoteClose,
        CreativeQuoteOpen,
        Cup,
        Danger,
        DangerCircle,
        Delete,
        DeliveryBox1,
        Digg,
        Discord,
        Document,
        Download,
        Dribbble,
        Edit,
        EditSquare,
        ElloCircle,
        Email,
        EmailOpen,
        Expand,
        Face,
        Facebook,
        FacebookOld,
        Figma,
        Filter,
        FlickrCircle,
        FlowChart,
        Forward60,
        Game,
        GameControllerDual,
        Gift,
        GiftCard,
        Github,
        GithubCircle,
        GooglePodcast,
        GridInterfaceHeader,
        GroovesharkCircle,
        Hangout,
        Headphone,
        Heart,
        HeartFilled,
        HeartRate,
        Hide,
        Hipchat,
        History,
        Home,
        Image,
        Imo,
        Inbox,
        InfoSquare,
        Instagram,
        Key,
        Keys,
        Kick,
        Layers,
        Letter,
        Line,
        Link,
        Link2,
        Linkedin,
        ListBox,
        Loader,
        LoaderFade,
        LoaderFast,
        Location,
        Lock,
        Login,
        Logout,
        Mastodon,
        Medium,
        Megaphone,
        MessageFilter,
        MessageNotification,
        MessageNotificationSquare,
        MessageSettings,
        Messages,
        Messages2,
        Meta,
        MetaMessenger,
        MimaReview,
        Minus,
        MoneyBagDollar,
        MonitorDisplayStand,
        Moon,
        MoonBig,
        More,
        MoreVertical,
        Mymind,
        NetworkWorld,
        Nintendo,
        Notification,
        NotificationBadge,
        NotificationMute,
        NotificationSleep,
        Onlyfans,
        Paper,
        PaperDownload,
        Patreon,
        Periscope,
        Pin,
        Pinterest,
        Play,
        Plus,
        PlusCircle,
        Pocket,
        Profile,
        Quora,
        QuoteBlockClose,
        QuoteBlockOpen,
        Reddit,
        Refresh,
        RefreshLeft,
        ReloadLeft,
        ReloadRight,
        Remove,
        Scan,
        ScanSecurity,
        Search,
        Send,
        Send2,
        Setting,
        Share,
        Share2,
        Shield,
        ShieldDone,
        ShieldProfile,
        Show,
        Sidebar,
        SidebarColapse,
        SidebarExpand,
        Slack2,
        Snapchat,
        SongsLibrary,
        SongsLibraryNote,
        Soundcloud,
        Source,
        Speaker,
        SpeakerMegaphone2,
        SpeakerMegaphone4,
        SpeakerMegaphone5,
        Split,
        Spotify,
        Sun,
        SunHappy,
        Swap,
        Swarm,
        Terminal,
        TerminalBox,
        Threads,
        Ticket,
        Tidal,
        Tiktok,
        TimeSquare,
        TimerClock,
        Translate,
        Tumblr,
        Twitch,
        Twitter,
        Unlink,
        Unlock,
        UpdateLeft,
        UpdateLeftBig,
        Upwork,
        Users3,
        UsersGroup,
        Vimeo,
        Vk,
        VolumeDown,
        VolumeOff,
        VolumeUp,
        Vote2,
        Wattpad,
        Wechat,
        Whatsapp,
        XCom,
        Xbox,
        XiaomiSquare,
        Yahoo,
        Yelp,
        Youtube,
    )
}
