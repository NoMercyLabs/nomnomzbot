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

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.unit.dp

// Navigation icons sourced from the designer's icon pack (Line style, 24 × 24 viewport).
// Each is stored as an ImageVector so Icon(tint = ...) tints them with the token colour —
// selected items get sidebarAccentForeground, idle items get sidebarForeground.

private const val ICON_SIZE: Float = 24f
private const val SW: Float = 1.5f

private fun navIcon(name: String, build: ImageVector.Builder.() -> Unit): ImageVector =
    ImageVector.Builder(
        name = name,
        defaultWidth = ICON_SIZE.dp,
        defaultHeight = ICON_SIZE.dp,
        viewportWidth = ICON_SIZE,
        viewportHeight = ICON_SIZE,
    ).apply(build).build()

val DashboardGlyph: ImageVector = navIcon("Dashboard") {
        addPath(
            pathData = addPathNodes("M4 9.99529V15.4C4 16.8999 4 17.6498 4.38197 18.1756C4.50533 18.3454 4.65464 18.4947 4.82443 18.618C5.35016 19 6.10011 19 7.6 19H16.4C17.8999 19 18.6498 19 19.1756 18.618C19.3454 18.4947 19.4947 18.3454 19.618 18.1756C20 17.6498 20 16.8999 20 15.4V9.99529C20 9.13043 20 8.69799 19.8383 8.32068C19.7851 8.19653 19.7194 8.07808 19.6424 7.96716C19.4081 7.63006 19.0414 7.40088 18.308 6.9425L13.908 4.1925C13.0857 3.67859 12.6746 3.42163 12.223 3.37096C12.0748 3.35434 11.9252 3.35434 11.777 3.37096C11.3254 3.42163 10.9143 3.67859 10.092 4.1925L5.692 6.9425L5.692 6.9425C4.9586 7.40088 4.5919 7.63006 4.35764 7.96716C4.28055 8.07808 4.21491 8.19653 4.1617 8.32068C4 8.69799 4 9.13043 4 9.99529Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val ChatGlyph: ImageVector = navIcon("Chat") {
        addPath(
            pathData = addPathNodes("M12 22C17.5228 22 22 17.5228 22 12C22 6.47715 17.5228 2 12 2C6.47715 2 2 6.47715 2 12C2 12.8795 2.11354 13.7324 2.32675 14.545C2.80749 16.3772 3.04786 17.2933 3.07622 17.6456C3.09139 17.834 3.09047 17.8088 3.089 17.9978C3.08624 18.3513 3.00763 18.7735 2.8504 19.618L2.5 21.5L4.382 21.1496L4.38204 21.1496C5.2265 20.9924 5.64874 20.9138 6.00218 20.911C6.19119 20.9095 6.16597 20.9086 6.35438 20.9238C6.7067 20.9521 7.62279 21.1925 9.45496 21.6733C10.2676 21.8865 11.1205 22 12 22Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val CommandsGlyph: ImageVector = navIcon("Commands") {
        addPath(
            pathData = addPathNodes("M4 6L8.72721 10.7272C9.2575 11.2575 9.52265 11.5226 9.57347 11.8436C9.58989 11.9472 9.58989 12.0528 9.57347 12.1564C9.52265 12.4774 9.2575 12.7425 8.72721 13.2728L4 18M12 18H20"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val EventResponsesGlyph: ImageVector = navIcon("EventResponses") {
        addPath(
            pathData = addPathNodes("M10 20C10 20 10.5 21 12 21C13.5 21 14 20 14 20M12 4C15.3137 4 18 6.68629 18 10V11.8992C18 13.2807 18.3217 14.6433 18.9395 15.879L19.2764 16.5528C19.6088 17.2177 19.1253 18 18.382 18H5.61803C4.87465 18 4.39116 17.2177 4.72361 16.5528L5.06049 15.879C5.67834 14.6433 6 13.2807 6 11.8992V10C6 6.68629 8.68629 4 12 4ZM12 4V3"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val PipelinesGlyph: ImageVector = navIcon("Pipelines") {
        addPath(
            pathData = addPathNodes("M3 14.5H7M4 17H6M10 14.5H14M11 17H13M17 14.5H21M18 17H20M3 8V12H7V8H3ZM10 8V12H14V8H10ZM17 8V12H21V8H17Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val TimersGlyph: ImageVector = navIcon("Timers") {
        addPath(
            pathData = addPathNodes("M20 4L21 5M20 4L19 3M20 4L17.6569 6.34315M4 4L5 3M4 4L3 5M4 4L6.34315 6.34315M10 1.5H14M6.34315 6.34315C4.89543 7.79086 4 9.79086 4 12C4 16.4183 7.58172 20 12 20C16.4183 20 20 16.4183 20 12C20 9.79086 19.1046 7.79086 17.6569 6.34315M6.34315 6.34315C7.79086 4.89543 9.79086 4 12 4C14.2091 4 16.2091 4.89543 17.6569 6.34315M12 12V8"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val QuotesGlyph: ImageVector = navIcon("Quotes") {
        addPath(
            pathData = addPathNodes("M8 7C6 8.5 6 11 6 14M6 14V15C6 16.1046 6.89543 17 8 17C9.10457 17 10 16.1046 10 15V13C10 11.8954 9.10457 11 8 11C6.89543 11 6 11.8954 6 13V14ZM15 7C13 8.5 13 11 13 14M13 14V15C13 16.1046 13.8954 17 15 17C16.1046 17 17 16.1046 17 15V13C17 11.8954 16.1046 11 15 11C13.8954 11 13 11.8954 13 13V14Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val PickListsGlyph: ImageVector = navIcon("PickLists") {
        addPath(
            pathData = addPathNodes("M8 6H21M8 12H21M8 18H21M3 6H3.01M3 12H3.01M3 18H3.01"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val CodeScriptsGlyph: ImageVector = navIcon("CodeScripts") {
        addPath(
            pathData = addPathNodes("M7 8L9.72721 10.7272C10.2575 11.2575 10.5226 11.5226 10.5735 11.8436C10.5899 11.9472 10.5899 12.0528 10.5735 12.1564C10.5226 12.4774 10.2575 12.7425 9.72721 13.2728L7 16M13 16H18M21 8.4V15.6C21 17.8498 21 18.9748 20.4271 19.7634C20.242 20.018 20.018 20.242 19.7634 20.4271C18.9748 21 17.8498 21 15.6 21H8.4C6.15016 21 5.02524 21 4.23664 20.4271C3.98196 20.242 3.75799 20.018 3.57295 19.7634C3 18.9748 3 17.8498 3 15.6V8.4C3 6.15016 3 5.02524 3.57295 4.23664C3.75799 3.98196 3.98196 3.75799 4.23664 3.57295C5.02524 3 6.15016 3 8.4 3H15.6C17.8498 3 18.9748 3 19.7634 3.57295C20.018 3.75799 20.242 3.98196 20.4271 4.23664C21 5.02524 21 6.15016 21 8.4Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val ModerationGlyph: ImageVector = navIcon("Moderation") {
        addPath(
            pathData = addPathNodes("M12 6V18M20 12V6.78078C20 6.32191 19.6879 5.92141 19.2452 5.80086C17.7624 5.39717 14.4553 4.43294 12.6303 3.40412C12.2431 3.18587 11.7569 3.18587 11.3697 3.40412C9.54467 4.43294 6.2376 5.39717 4.75483 5.80086C4.31208 5.92141 4 6.32191 4 6.78078V12C4 16.9039 8.54025 18.2625 11.2455 20.345C11.6832 20.6819 12.3168 20.6819 12.7545 20.345C15.4598 18.2625 20 16.9039 20 12Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val RewardsGlyph: ImageVector = navIcon("Rewards") {
        addPath(
            pathData = addPathNodes("M7 6L6.6 6C5.10011 6 4.35016 6 3.82443 6.38197C3.65464 6.50532 3.50533 6.65464 3.38197 6.82443C3 7.35016 3 8.10011 3 9.6V14.5M17 6H17.4C18.8999 6 19.6498 6 20.1756 6.38197C20.3454 6.50532 20.4947 6.65464 20.618 6.82443C21 7.35016 21 8.10011 21 9.6V14.5M12 6C14.5 4.5 18 3.49999 17 2C15.9041 0.356191 12.5 2.5 12 6ZM12 6C9.5 4.5 6 3.49999 7 2C8.09588 0.356191 11.5 2.5 12 6ZM12 6C11.1667 6.33333 9.4 7.5 9 9.5M12 6C12.8333 6.33333 14.6 7.5 15 9.5M3 14.5H21M3 14.5C3 15.9045 3 16.6067 3.33706 17.1111C3.48298 17.3295 3.67048 17.517 3.88886 17.6629C4.39331 18 5.09554 18 6.5 18H17.5C18.9045 18 19.6067 18 20.1111 17.6629C20.3295 17.517 20.517 17.3295 20.6629 17.1111C21 16.6067 21 15.9045 21 14.5"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val EconomyGlyph: ImageVector = navIcon("Economy") {
        addPath(
            pathData = addPathNodes("M20 10C20 11.5 16.5 13 12 13C7.5 13 4 11.5 4 10M20 10C20 8.5 16.4183 7 12 7C7.58172 7 4 8.5 4 10M20 10V14C20 15.7673 16.4183 17 12 17C7.58172 17 4 15.7673 4 14V10"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val GamesGlyph: ImageVector = navIcon("Games") {
        addPath(
            pathData = addPathNodes("M8.5625 11V9.5M8.5625 11V12.5M8.5625 11H10.0625M8.5625 11H7.0625M15.5625 9.5V9.51M15.5625 12.5V12.49M17.0625 11H17.0525M14.0625 11H14.0725M3.26663 15.578L4.71274 7.86537C4.91554 6.78377 5.85993 6 6.96038 6H17.1646C18.2651 6 19.2095 6.78378 19.4123 7.86537L20.8584 15.578C21.0159 16.418 20.5928 17.2596 19.8247 17.6343C19.0747 18.0002 18.1737 17.8334 17.6043 17.2233L15.4098 14.8721C15.1883 14.6348 14.8782 14.5 14.5535 14.5H9.57148C9.24683 14.5 8.93673 14.6348 8.71521 14.8721L6.52071 17.2233C5.95132 17.8334 5.05034 18.0002 4.30032 17.6343C3.53224 17.2596 3.10913 16.418 3.26663 15.578Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val MusicGlyph: ImageVector = navIcon("Music") {
        addPath(
            pathData = addPathNodes("M5 13V12C5 8.13401 8.13401 5 12 5C15.866 5 19 8.13401 19 12V13M5 13C5.89535 12.3285 7.18249 12.8687 7.33041 13.9781L7.82699 17.7024C7.9274 18.4555 7.47884 19.1737 6.75809 19.414C5.83497 19.7217 4.85531 19.1318 4.69534 18.172L4.2325 15.395C4.08659 14.5195 4.37244 13.6276 5 13ZM19 13C18.1047 12.3285 16.8175 12.8687 16.6696 13.9781L16.173 17.7024C16.0726 18.4555 16.5212 19.1737 17.2419 19.414C18.165 19.7217 19.1447 19.1318 19.3047 18.172L19.7675 15.395C19.9134 14.5195 19.6276 13.6276 19 13Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val SongRequestsGlyph: ImageVector = navIcon("SongRequests") {
        addPath(
            pathData = addPathNodes("M4 6H20M4 10H20M4 14H12M4 18H12M20 16L16 13.5V18.5L20 16Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val TtsGlyph: ImageVector = navIcon("Tts") {
        addPath(
            pathData = addPathNodes("M12.0312 21H12.7917C15.1575 21 16.3404 21 17.1455 20.3873C17.4049 20.1899 17.6304 19.9515 17.8131 19.6816C18.3802 18.8437 18.3146 17.6626 18.1834 15.3005L17.7834 8.10046C17.6647 5.96479 17.6054 4.89696 17.029 4.15682C16.8425 3.91725 16.6206 3.70738 16.3711 3.5344C15.6001 3 14.5306 3 12.3917 3H11.6083C9.46936 3 8.39988 3 7.62891 3.5344C7.37936 3.70738 7.15752 3.91725 6.97096 4.15682C6.39461 4.89696 6.33529 5.96479 6.21664 8.10046L5.81664 15.3005C5.68541 17.6626 5.61979 18.8437 6.18688 19.6816C6.3696 19.9515 6.59509 20.1899 6.8545 20.3873C7.65958 21 8.8425 21 11.2083 21H11.9688M13.5 8C13.5 8.82843 12.8284 9.5 12 9.5C11.1716 9.5 10.5 8.82843 10.5 8C10.5 7.17157 11.1716 6.5 12 6.5C12.8284 6.5 13.5 7.17157 13.5 8ZM15 15.5C15 17.1569 13.6569 18.5 12 18.5C10.3431 18.5 9 17.1569 9 15.5C9 13.8431 10.3431 12.5 12 12.5C13.6569 12.5 15 13.8431 15 15.5Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val SoundClipsGlyph: ImageVector = navIcon("SoundClips") {
        addPath(
            pathData = addPathNodes("M11 5L6 9H2V15H6L11 19V5ZM15.54 8.46C16.4774 9.39764 17.004 10.6692 17.004 11.995C17.004 13.3208 16.4774 14.5924 15.54 15.53M19.07 4.93C20.9447 6.80528 21.9979 9.34836 21.9979 12C21.9979 14.6516 20.9447 17.1947 19.07 19.07"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val WidgetsGlyph: ImageVector = navIcon("Widgets") {
        addPath(
            pathData = addPathNodes("M8.5 14.4444L3 12L8.5 9.44444M8.5 14.4444L12 16L15.5 14.4444M8.5 14.4444L3 17L12 21L21 17L15.5 14.4444M15.5 14.4444L21 12L15.5 9.44444M15.5 9.44444L12 11L8.5 9.44444M15.5 9.44444L21 7L12 3L3 7L8.5 9.44444"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val AlertsGlyph: ImageVector = navIcon("Alerts") {
        addPath(
            pathData = addPathNodes("M18 11.8992C18 13.2807 18.3217 14.6433 18.9395 15.879L19.2764 16.5528C19.6088 17.2177 19.1253 18 18.382 18H5.61803C4.87465 18 4.39116 17.2177 4.72361 16.5528L5.06049 15.879C5.67834 14.6433 6 13.2807 6 11.8992V10C6 6.68629 8.68629 4 12 4M10 20C10 20 10.5 21 12 21C13.5 21 14 20 14 20M12 4V3M12 4C12.3341 4 12.6691 4.02821 13 4.0838M19 7C19 5.89543 18.1046 5 17 5C15.8954 5 15 5.89543 15 7C15 8.10457 15.8954 9 17 9C18.1046 9 19 8.10457 19 7Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val AnalyticsGlyph: ImageVector = navIcon("Analytics") {
        addPath(
            pathData = addPathNodes("M20 20H7.6C6.10011 20 5.35016 20 4.82443 19.618C4.65464 19.4947 4.50533 19.3454 4.38197 19.1756C4 18.6498 4 17.8999 4 16.4L4 4M8 16V10M13 16V6M18 16V14"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val CommunityGlyph: ImageVector = navIcon("Community") {
        addPath(
            pathData = addPathNodes("M5 16C4.17346 16 3.49873 16.1518 2.95491 16.3718C2.31582 16.6303 2 17.3106 2 18M19 16C19.8265 16 20.5013 16.1518 21.0451 16.3718C21.6842 16.6303 22 17.3106 22 18M17.2 18C17.2 16.805 16.6526 15.6259 15.5449 15.1778C14.6022 14.7965 13.4327 14.5333 12 14.5333C10.5674 14.5333 9.39785 14.7965 8.45522 15.1778C7.34746 15.6259 6.80005 16.805 6.80005 18M7 12C7 13.1046 6.10457 14 5 14C3.89543 14 3 13.1046 3 12C3 10.8954 3.89543 10 5 10C6.10457 10 7 10.8954 7 12ZM17 12C17 13.1046 17.8954 14 19 14C20.1046 14 21 13.1046 21 12C21 10.8954 20.1046 10 19 10C17.8954 10 17 10.8954 17 12ZM15.4667 8.46667C15.4667 10.3813 13.9146 11.9333 12 11.9333C10.0855 11.9333 8.53338 10.3813 8.53338 8.46667C8.53338 6.55208 10.0855 5 12 5C13.9146 5 15.4667 6.55208 15.4667 8.46667Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val DiscordGlyph: ImageVector = navIcon("Discord") {
        addPath(
            pathData = addPathNodes("M21 12V16C21 17.6569 19.6569 19 18 19H12.618C12.2393 19 11.893 19.214 11.7236 19.5528L10.8944 21.2111C10.5259 21.9482 9.4741 21.9482 9.10557 21.2111L8.27639 19.5528C8.107 19.214 7.76074 19 7.38197 19H6C4.34315 19 3 17.6569 3 16V8C3 6.34315 4.34315 5 6 5H14M19 4C17.3431 4 16 5.34315 16 7C16 8.65685 17.3431 10 19 10C20.6569 10 22 8.65685 22 7C22 5.34315 20.6569 4 19 4Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val IntegrationsGlyph: ImageVector = navIcon("Integrations") {
        addPath(
            pathData = addPathNodes("M15.2779 16.6887L12.4096 13.8203C12.2906 13.7013 12.0976 13.7013 11.9787 13.8203C10.1288 15.6701 7.07544 15.775 5.52019 13.6714C5.23755 13.2891 4.9816 12.9049 4.7775 12.5361C3.39 10.0294 3.08444 5.86398 3.01795 3.97945C2.99873 3.43479 3.43472 2.9988 3.97939 3.01802C5.86391 3.08451 10.0293 3.39007 12.5361 4.77757C12.9048 4.98167 13.289 5.23762 13.6713 5.52026C15.775 7.07551 15.6701 10.1288 13.8202 11.9787C13.7012 12.0977 13.7012 12.2906 13.8202 12.4096L16.6886 15.278C17.3625 15.9519 18.4182 16.0567 19.2114 15.5284L21.7063 13.8669C21.9243 13.7218 22.2144 13.7506 22.3996 13.9358C22.6142 14.1504 22.6142 14.4984 22.3996 14.7131L18.5563 18.5563L14.713 22.3996C14.4983 22.6143 14.1503 22.6143 13.9357 22.3996C13.7505 22.2145 13.7217 21.9243 13.8669 21.7063L15.5284 19.2115C16.0566 18.4183 15.9518 17.3625 15.2779 16.6887Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val RolesGlyph: ImageVector = navIcon("Roles") {
        addPath(
            pathData = addPathNodes("M12 12C13.1046 12 14 11.1046 14 10C14 8.89543 13.1046 8 12 8C10.8954 8 10 8.89543 10 10C10 11.1046 10.8954 12 12 12ZM12 12C13.6569 12 15 13.3431 15 15M12 12C10.3431 12 9 13.3431 9 15M20 12V6.78078C20 6.32191 19.6879 5.92141 19.2452 5.80086C17.7624 5.39717 14.4553 4.43294 12.6303 3.40412C12.2431 3.18587 11.7569 3.18587 11.3697 3.40412C9.54467 4.43294 6.2376 5.39717 4.75483 5.80086C4.31208 5.92141 4 6.32191 4 6.78078V12C4 16.9039 8.54025 18.2625 11.2455 20.345C11.6832 20.6819 12.3168 20.6819 12.7545 20.345C15.4598 18.2625 20 16.9039 20 12Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val FeaturesGlyph: ImageVector = navIcon("Features") {
        addPath(
            pathData = addPathNodes("M4 6H15M20 6H19M17 4V8M4 12H5M20 12H9M7 10V14M4 18H15M20 18H19M17 16V20"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val WebhooksGlyph: ImageVector = navIcon("Webhooks") {
        addPath(
            pathData = addPathNodes("M12 3C15.3313 3 18.2398 4.80989 19.796 7.5C20.5617 8.82378 21 10.3607 21 12M21 12H16.5M21 12C21 13.6393 20.5617 15.1762 19.796 16.5M12 21C15.3313 21 18.2398 19.1901 19.796 16.5M12 21C13.6656 21 15.1199 19.1901 15.898 16.5M12 21C10.3344 21 8.88009 19.1901 8.10202 16.5M3 12C3 13.6393 3.43827 15.1762 4.20404 16.5C5.76018 19.1901 8.66873 21 12 21M12 21V3M12 3C8.66873 3 5.76018 4.80989 4.20404 7.5C3.43827 8.82378 3 10.3607 3 12M3 12H7.5M12 3C10.3344 3 8.88009 4.80989 8.10202 7.5M12 3C13.6656 3 15.1199 4.80989 15.898 7.5M16.5 12C16.5 10.3607 16.2809 8.82378 15.898 7.5M16.5 12H7.5M16.5 12C16.5 13.6393 16.2809 15.1762 15.898 16.5M7.5 12C7.5 10.3607 7.71914 8.82378 8.10202 7.5M7.5 12C7.5 13.6393 7.71914 15.1762 8.10202 16.5M4.20404 7.5H8.10202M8.10202 7.5H15.898M15.898 7.5H19.796M19.796 16.5H15.898M15.898 16.5H8.10202M8.10202 16.5H4.20404"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val FederationGlyph: ImageVector = navIcon("Federation") {
        addPath(
            pathData = addPathNodes("M17.3637 3.63605C20.8784 7.15077 20.8784 12.8493 17.3637 16.364C15.6063 18.1213 13.303 19 10.9997 19M4.63574 16.364C6.3931 18.1213 8.6964 19 10.9997 19M10.9997 19V22M10.9997 22H6.99976M10.9997 22H14.9998M16.9997 10C16.9997 13.3137 14.3134 16 10.9997 16C7.68599 16 4.9997 13.3137 4.9997 10C4.9997 6.6863 7.68599 4.00001 10.9997 4.00001C14.3134 4.00001 16.9997 6.6863 16.9997 10Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val CustomEventsGlyph: ImageVector = navIcon("CustomEvents") {
        addPath(
            pathData = addPathNodes("M22 12H18L15 21L9 3L6 12H2"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val SettingsGlyph: ImageVector = navIcon("Settings") {
        addPath(
            pathData = addPathNodes("M12.0005 20H13.0005C13.5528 20 14.0005 19.5523 14.0005 19V18.3658C14.0005 17.9419 14.2716 17.5715 14.6517 17.384C14.8892 17.2668 15.1178 17.1344 15.3363 16.988C15.6885 16.752 16.1451 16.7023 16.5122 16.9143L17.0626 17.2321C17.5409 17.5082 18.1525 17.3443 18.4287 16.866L19.4287 15.134C19.7048 14.6557 19.5409 14.0441 19.0626 13.768L18.5121 13.4501C18.1455 13.2384 17.96 12.819 17.9876 12.3965C17.9961 12.2654 18.0005 12.1332 18.0005 12C18.0005 11.8668 17.9961 11.7346 17.9876 11.6035C17.96 11.1811 18.1455 10.7616 18.5121 10.5499L19.0626 10.2321C19.5409 9.95591 19.7048 9.34432 19.4287 8.86603L18.4287 7.13398C18.1525 6.65569 17.5409 6.49181 17.0626 6.76795L16.5122 7.08573C16.1451 7.29771 15.6885 7.24805 15.3363 7.01204C15.1178 6.86564 14.8892 6.73321 14.6517 6.61604C14.2716 6.42852 14.0005 6.05805 14.0005 5.63424V5C14.0005 4.44772 13.5528 4 13.0005 4H11.0005C10.4482 4 10.0005 4.44771 10.0005 5V5.63424C10.0005 6.05805 9.72933 6.42852 9.34926 6.61604C9.11175 6.73322 8.88312 6.86565 8.66464 7.01205C8.31244 7.24806 7.85587 7.29772 7.48871 7.08574L6.93829 6.76795C6.46 6.49181 5.84841 6.65569 5.57227 7.13398L4.57227 8.86603C4.29612 9.34432 4.46 9.95591 4.93829 10.2321L5.48886 10.5499C5.85552 10.7616 6.04094 11.1811 6.01338 11.6036C6.00483 11.7346 6.00049 11.8668 6.00049 12C6.00049 12.1332 6.00483 12.2654 6.01338 12.3964C6.04094 12.8189 5.85552 13.2384 5.48886 13.4501L4.93829 13.768C4.46 14.0441 4.29612 14.6557 4.57227 15.134L5.57227 16.866C5.84841 17.3443 6.46 17.5082 6.93829 17.2321L7.48872 16.9143C7.85588 16.7023 8.31245 16.7519 8.66464 16.988C8.88312 17.1344 9.11175 17.2668 9.34926 17.384C9.72933 17.5715 10.0005 17.9419 10.0005 18.3658V19C10.0005 19.5523 10.4482 20 11.0005 20H11.9905M12.0005 9.5C13.3812 9.5 14.5005 10.6193 14.5005 12C14.5005 13.3807 13.3812 14.5 12.0005 14.5C10.6198 14.5 9.50049 13.3807 9.50049 12C9.50049 10.6193 10.6198 9.5 12.0005 9.5Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}

val AdminGlyph: ImageVector = navIcon("Admin") {
        addPath(
            pathData = addPathNodes("M12 12C13.1046 12 14 11.1046 14 10C14 8.89543 13.1046 8 12 8C10.8954 8 10 8.89543 10 10C10 11.1046 10.8954 12 12 12ZM12 12C13.6569 12 15 13.3431 15 15M12 12C10.3431 12 9 13.3431 9 15M20 12V6.78078C20 6.32191 19.6879 5.92141 19.2452 5.80086C17.7624 5.39717 14.4553 4.43294 12.6303 3.40412C12.2431 3.18587 11.7569 3.18587 11.3697 3.40412C9.54467 4.43294 6.2376 5.39717 4.75483 5.80086C4.31208 5.92141 4 6.32191 4 6.78078V12C4 16.9039 8.54025 18.2625 11.2455 20.345C11.6832 20.6819 12.3168 20.6819 12.7545 20.345C15.4598 18.2625 20 16.9039 20 12Z"),
            stroke = SolidColor(Color.Black),
            strokeLineWidth = SW,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
            fill = SolidColor(Color.Transparent),
        )
}
