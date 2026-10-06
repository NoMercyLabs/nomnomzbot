// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.nestedscroll.NestedScrollConnection
import androidx.compose.ui.input.nestedscroll.NestedScrollSource
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.input.pointer.PointerEventType
import androidx.compose.ui.input.pointer.pointerInput
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import kotlinx.coroutines.delay
import kotlinx.datetime.Clock
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.chat_new_messages
import org.jetbrains.compose.resources.pluralStringResource

/**
 * The follow state of the [FollowingFeed] hosting the current composition, so a line menu deep inside a row can
 * hold the feed (see [FeedPauseEffect]) without every row threading it through. Null outside a feed.
 */
val LocalFeedFollowState = compositionLocalOf<FeedFollowState?> { null }

/** Hold the enclosing [FollowingFeed] for [reason] while [active] is true. A no-op outside a feed. */
@Composable
fun FeedPauseEffect(reason: FeedPauseReason, active: Boolean) {
    val follow: FeedFollowState = LocalFeedFollowState.current ?: return
    DisposableEffect(follow, reason, active) {
        if (active) follow.acquirePause(reason)
        onDispose { if (active) follow.releasePause(reason) }
    }
}

/**
 * A live chat feed (oldest first) that follows its newest line unless a moderator is working in it: while the
 * pointer is over it, the user has scrolled up, or a [FeedPauseReason] is active, the lines stay put and a pill at
 * the bottom counts the lines that arrived meanwhile. Clicking the pill shows the newest line. Shared by Chat and
 * Multi-Chat so both behave alike.
 */
@Composable
fun <T> FollowingFeed(
    items: List<T>,
    key: (index: Int, item: T) -> String,
    modifier: Modifier = Modifier,
    verticalArrangement: Arrangement.Vertical = Arrangement.Top,
    followState: FeedFollowState = rememberFeedFollowState(),
    row: @Composable (item: T) -> Unit,
) {
    val spacing = LocalSpacing.current
    val listState: LazyListState = rememberLazyListState()
    val scope = rememberCoroutineScope()
    val keys: List<String> = items.mapIndexed(key)
    val tailKey: String? = keys.lastOrNull()
    val following: Boolean = followState.isFollowing

    // Follow the tail when nothing holds the feed. Keyed on the tail key as well as the size: a capped buffer stops
    // changing size once full, and a size-only key would freeze following on a busy channel.
    LaunchedEffect(items.size, tailKey, following) {
        if (following && items.isNotEmpty()) {
            listState.scrollToItem(items.lastIndex)
            followState.markCaughtUp(tailKey)
        }
    }

    // The grace period ends by time alone: wake readers of isFollowing once it has passed.
    LaunchedEffect(followState.isPointerOver) {
        if (!followState.isPointerOver) {
            delay(followState.resumeDelayMs)
            followState.refresh()
        }
    }

    val userScrollWatcher: NestedScrollConnection = remember(followState, listState) {
        object : NestedScrollConnection {
            override fun onPreScroll(available: Offset, source: NestedScrollSource): Offset {
                if (source == NestedScrollSource.UserInput && available.y > 0f) followState.onUserScrolledUp()
                return Offset.Zero
            }

            override fun onPostScroll(consumed: Offset, available: Offset, source: NestedScrollSource): Offset {
                if (source == NestedScrollSource.UserInput && !listState.canScrollForward) {
                    followState.onUserReachedBottom()
                }
                return Offset.Zero
            }
        }
    }

    CompositionLocalProvider(LocalFeedFollowState provides followState) {
        Box(
            modifier = Modifier.pointerInput(followState) {
                awaitPointerEventScope {
                    while (true) {
                        when (awaitPointerEvent().type) {
                            PointerEventType.Enter -> followState.onPointerEnter()
                            PointerEventType.Exit -> followState.onPointerExit()
                            else -> Unit
                        }
                    }
                }
            },
        ) {
            LazyColumn(
                state = listState,
                modifier = modifier.nestedScroll(userScrollWatcher),
                verticalArrangement = verticalArrangement,
            ) {
                itemsIndexed(items = items, key = { index, _ -> keys[index] }) { _, item -> row(item) }
            }
            val newLines: Int = followState.newLineCount(keys)
            if (newLines > 0) {
                Button(
                    onClick = {
                        followState.resume()
                        scope.launch {
                            if (items.isNotEmpty()) listState.scrollToItem(items.lastIndex)
                            followState.markCaughtUp(tailKey)
                        }
                    },
                    modifier = Modifier.align(Alignment.BottomCenter).padding(bottom = spacing.s2),
                    variant = ButtonVariant.Secondary,
                    size = ButtonSize.Sm,
                ) {
                    Text(pluralStringResource(Res.plurals.chat_new_messages, newLines, newLines))
                }
            }
        }
    }
}

@Composable
fun rememberFeedFollowState(): FeedFollowState {
    return remember { FeedFollowState(clock = { Clock.System.now().toEpochMilliseconds() }) }
}
