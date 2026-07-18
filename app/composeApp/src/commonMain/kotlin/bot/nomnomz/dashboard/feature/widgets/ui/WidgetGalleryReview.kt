// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.GalleryItemDetail
import bot.nomnomz.dashboard.core.network.GalleryItemSummary
import bot.nomnomz.dashboard.core.network.PinGalleryItemBody
import bot.nomnomz.dashboard.core.network.ReviewGalleryItemBody
import bot.nomnomz.dashboard.core.network.SubmitGalleryItemBody
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.widgets_review_action_error
import nomnomzbot.composeapp.generated.resources.widgets_review_back
import nomnomzbot.composeapp.generated.resources.widgets_review_close
import nomnomzbot.composeapp.generated.resources.widgets_review_empty
import nomnomzbot.composeapp.generated.resources.widgets_review_filter_in_review
import nomnomzbot.composeapp.generated.resources.widgets_review_filter_rejected
import nomnomzbot.composeapp.generated.resources.widgets_review_filter_submitted
import nomnomzbot.composeapp.generated.resources.widgets_review_filter_verified
import nomnomzbot.composeapp.generated.resources.widgets_review_mark_in_review
import nomnomzbot.composeapp.generated.resources.widgets_review_notes_label
import nomnomzbot.composeapp.generated.resources.widgets_review_reject
import nomnomzbot.composeapp.generated.resources.widgets_review_repin
import nomnomzbot.composeapp.generated.resources.widgets_review_repin_sha
import nomnomzbot.composeapp.generated.resources.widgets_review_repin_tag
import nomnomzbot.composeapp.generated.resources.widgets_review_repin_warning
import nomnomzbot.composeapp.generated.resources.widgets_review_repo
import nomnomzbot.composeapp.generated.resources.widgets_review_saas_label
import nomnomzbot.composeapp.generated.resources.widgets_review_sha
import nomnomzbot.composeapp.generated.resources.widgets_review_status
import nomnomzbot.composeapp.generated.resources.widgets_review_tag
import nomnomzbot.composeapp.generated.resources.widgets_review_title
import nomnomzbot.composeapp.generated.resources.widgets_review_verify
import nomnomzbot.composeapp.generated.resources.widgets_submit_cancel
import nomnomzbot.composeapp.generated.resources.widgets_submit_confirm
import nomnomzbot.composeapp.generated.resources.widgets_submit_description
import nomnomzbot.composeapp.generated.resources.widgets_submit_desc
import nomnomzbot.composeapp.generated.resources.widgets_submit_framework
import nomnomzbot.composeapp.generated.resources.widgets_submit_name
import nomnomzbot.composeapp.generated.resources.widgets_submit_repo
import nomnomzbot.composeapp.generated.resources.widgets_submit_sha
import nomnomzbot.composeapp.generated.resources.widgets_submit_success
import nomnomzbot.composeapp.generated.resources.widgets_submit_tag
import nomnomzbot.composeapp.generated.resources.widgets_submit_title
import org.jetbrains.compose.resources.stringResource

// The community widget-gallery submit + reviewer-queue surfaces (widgets-overlays.md §5c), rendered as dialogs
// off the Overlays page. Both are pure projections over the suspend lambdas the WidgetsController exposes — they
// hold only their own transient dialog state, never the page's. The frameworks a submission may declare.
private val SubmitFrameworks: List<String> = listOf("vanilla", "vue", "react", "svelte")

/**
 * The community submit form (any signed-in user): propose a GitHub-hosted widget for review. The backend
 * validates the FULL 40-hex [SubmitGalleryItemBody.pinnedCommitSha] and the `https://github.com/{owner}/{repo}`
 * [SubmitGalleryItemBody.gitHubRepoUrl] — its rejection surfaces inline as [error] rather than closing the dialog.
 */
@Composable
fun WidgetSubmitDialog(
    submit: suspend (SubmitGalleryItemBody) -> ApiResult<GalleryItemDetail>,
    onDismiss: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    var name: String by remember { mutableStateOf("") }
    var framework: String by remember { mutableStateOf(SubmitFrameworks.first()) }
    var repoUrl: String by remember { mutableStateOf("") }
    var sha: String by remember { mutableStateOf("") }
    var tag: String by remember { mutableStateOf("") }
    var description: String by remember { mutableStateOf("") }
    var submitting: Boolean by remember { mutableStateOf(false) }
    var error: String? by remember { mutableStateOf(null) }
    var submitted: Boolean by remember { mutableStateOf(false) }

    val canSubmit: Boolean =
        name.isNotBlank() && repoUrl.isNotBlank() && sha.length == 40 && !submitting

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.widgets_submit_title), style = typography.lg) },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth().verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                if (submitted) {
                    Text(
                        text = stringResource(Res.string.widgets_submit_success),
                        style = typography.base,
                        color = tokens.mutedForeground,
                    )
                } else {
                    Text(
                        text = stringResource(Res.string.widgets_submit_desc),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                    error?.let { ActionErrorBanner(message = it) }
                    AppTextField(
                        value = name,
                        onValueChange = { name = it },
                        label = stringResource(Res.string.widgets_submit_name),
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Text(
                        text = stringResource(Res.string.widgets_submit_framework),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        SubmitFrameworks.forEach { option ->
                            Badge(
                                selected = framework == option,
                                onClick = { framework = option },
                            ) {
                                Text(text = option)
                            }
                        }
                    }
                    AppTextField(
                        value = repoUrl,
                        onValueChange = { repoUrl = it },
                        label = stringResource(Res.string.widgets_submit_repo),
                        placeholder = "https://github.com/owner/repo",
                        modifier = Modifier.fillMaxWidth(),
                    )
                    AppTextField(
                        value = sha,
                        onValueChange = { sha = it.trim() },
                        label = stringResource(Res.string.widgets_submit_sha),
                        isError = sha.isNotEmpty() && sha.length != 40,
                        modifier = Modifier.fillMaxWidth(),
                    )
                    AppTextField(
                        value = tag,
                        onValueChange = { tag = it },
                        label = stringResource(Res.string.widgets_submit_tag),
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Textarea(
                        value = description,
                        onValueChange = { description = it },
                        label = stringResource(Res.string.widgets_submit_description),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
        },
        confirmButton = {
            if (submitted) {
                Button(onClick = onDismiss) { Text(text = stringResource(Res.string.widgets_review_close)) }
            } else {
                Button(
                    onClick = {
                        error = null
                        submitting = true
                        scope.launch {
                            val result: ApiResult<GalleryItemDetail> =
                                submit(
                                    SubmitGalleryItemBody(
                                        name = name.trim(),
                                        framework = framework,
                                        gitHubRepoUrl = repoUrl.trim(),
                                        pinnedCommitSha = sha.trim(),
                                        pinnedTag = tag.trim().ifBlank { null },
                                        description = description.trim().ifBlank { null },
                                    )
                                )
                            submitting = false
                            when (result) {
                                is ApiResult.Ok -> submitted = true
                                is ApiResult.Failure -> error = result.error.message
                            }
                        }
                    },
                    enabled = canSubmit,
                ) {
                    Text(text = stringResource(Res.string.widgets_submit_confirm))
                }
            }
        },
        dismissButton =
            if (submitted) null
            else {
                { TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.widgets_submit_cancel)) } }
            },
    )
}

/** The reviewer statuses the queue filters by. */
private val ReviewStatuses: List<String> = listOf("submitted", "in_review", "verified", "rejected")

/**
 * The reviewer queue (`gallery:review`): browse submissions by status, open one, and record a verdict
 * (`in_review` | `verified` | `rejected`) or re-pin it (which kicks it back to `in_review`). Every write refreshes
 * the queue. The backend is the real gate — a non-reviewer never reaches this (the page hides the entry point).
 */
@Composable
fun WidgetReviewSheet(
    loadQueue: suspend (String) -> ApiResult<List<GalleryItemSummary>>,
    loadDetail: suspend (String) -> ApiResult<GalleryItemDetail>,
    onReview: suspend (String, ReviewGalleryItemBody) -> ApiResult<GalleryItemDetail>,
    onPin: suspend (String, PinGalleryItemBody) -> ApiResult<GalleryItemDetail>,
    onDismiss: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    var status: String by remember { mutableStateOf(ReviewStatuses.first()) }
    var loading: Boolean by remember { mutableStateOf(true) }
    var queue: List<GalleryItemSummary> by remember { mutableStateOf(emptyList()) }
    var error: String? by remember { mutableStateOf(null) }
    var selected: GalleryItemDetail? by remember { mutableStateOf(null) }

    suspend fun refresh() {
        loading = true
        error = null
        when (val result: ApiResult<List<GalleryItemSummary>> = loadQueue(status)) {
            is ApiResult.Ok -> queue = result.value
            is ApiResult.Failure -> error = result.error.message
        }
        loading = false
    }

    LaunchedEffect(status) { refresh() }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.widgets_review_title), style = typography.lg) },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                val detail: GalleryItemDetail? = selected
                if (detail == null) {
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        ReviewStatuses.forEach { option ->
                            Badge(selected = status == option, onClick = { status = option }) {
                                Text(text = stringResource(reviewFilterLabel(option)))
                            }
                        }
                    }
                    error?.let { ActionErrorBanner(message = stringResource(Res.string.widgets_review_action_error, it)) }
                    when {
                        loading -> Spinner(modifier = Modifier.fillMaxWidth())
                        queue.isEmpty() ->
                            Text(
                                text = stringResource(Res.string.widgets_review_empty),
                                style = typography.base,
                                color = tokens.mutedForeground,
                            )
                        else ->
                            Card(modifier = Modifier.fillMaxWidth()) {
                                Column(modifier = Modifier.verticalScroll(rememberScrollState())) {
                                    queue.forEachIndexed { index, item ->
                                        if (index > 0) Separator()
                                        ReviewRow(
                                            item = item,
                                            onOpen = {
                                                scope.launch {
                                                    when (
                                                        val result: ApiResult<GalleryItemDetail> = loadDetail(item.id)
                                                    ) {
                                                        is ApiResult.Ok -> selected = result.value
                                                        is ApiResult.Failure -> error = result.error.message
                                                    }
                                                }
                                            },
                                        )
                                    }
                                }
                            }
                    }
                } else {
                    ReviewDetailPanel(
                        detail = detail,
                        onReview = { body ->
                            scope.launch {
                                when (val result: ApiResult<GalleryItemDetail> = onReview(detail.id, body)) {
                                    is ApiResult.Ok -> {
                                        selected = null
                                        refresh()
                                    }
                                    is ApiResult.Failure -> error = result.error.message
                                }
                            }
                        },
                        onPin = { body ->
                            scope.launch {
                                when (val result: ApiResult<GalleryItemDetail> = onPin(detail.id, body)) {
                                    is ApiResult.Ok -> {
                                        selected = null
                                        refresh()
                                    }
                                    is ApiResult.Failure -> error = result.error.message
                                }
                            }
                        },
                    )
                }
            }
        },
        confirmButton = {
            Button(onClick = onDismiss) { Text(text = stringResource(Res.string.widgets_review_close)) }
        },
        dismissButton =
            if (selected != null) {
                { TextButton(onClick = { selected = null }) { Text(text = stringResource(Res.string.widgets_review_back)) } }
            } else null,
    )
}

@Composable
private fun ReviewRow(item: GalleryItemSummary, onOpen: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    Row(
        modifier = Modifier.fillMaxWidth().padding(spacing.s3),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                Text(text = item.name, style = typography.base, color = tokens.cardForeground)
                Badge(variant = BadgeVariant.Secondary) { Text(text = item.framework) }
            }
            item.description?.let {
                Text(text = it, style = typography.sm, color = tokens.mutedForeground)
            }
            TextButton(onClick = onOpen) { Text(text = stringResource(Res.string.widgets_review_verify)) }
        }
    }
}

@Composable
private fun ReviewDetailPanel(
    detail: GalleryItemDetail,
    onReview: (ReviewGalleryItemBody) -> Unit,
    onPin: (PinGalleryItemBody) -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    var notes: String by remember(detail.id) { mutableStateOf(detail.reviewNotes.orEmpty()) }
    var availableInSaaS: Boolean by remember(detail.id) { mutableStateOf(detail.availableInSaaS) }
    var repinSha: String by remember(detail.id) { mutableStateOf(detail.pinnedCommitSha.orEmpty()) }
    var repinTag: String by remember(detail.id) { mutableStateOf(detail.pinnedTag.orEmpty()) }

    Column(
        modifier = Modifier.fillMaxWidth().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(text = detail.name, style = typography.lg, color = tokens.cardForeground)
        detail.gitHubRepoUrl?.let {
            Text(text = stringResource(Res.string.widgets_review_repo, it), style = typography.sm, color = tokens.mutedForeground)
        }
        detail.pinnedCommitSha?.let {
            Text(text = stringResource(Res.string.widgets_review_sha, it), style = typography.sm, color = tokens.mutedForeground)
        }
        detail.pinnedTag?.let {
            Text(text = stringResource(Res.string.widgets_review_tag, it), style = typography.sm, color = tokens.mutedForeground)
        }
        Text(
            text = stringResource(Res.string.widgets_review_status, detail.reviewStatus),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        Textarea(
            value = notes,
            onValueChange = { notes = it },
            label = stringResource(Res.string.widgets_review_notes_label),
            modifier = Modifier.fillMaxWidth(),
        )
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Switch(checked = availableInSaaS, onCheckedChange = { availableInSaaS = it })
            Text(
                text = stringResource(Res.string.widgets_review_saas_label),
                style = typography.sm,
                color = tokens.cardForeground,
            )
        }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Button(
                onClick = {
                    onReview(
                        ReviewGalleryItemBody(
                            reviewStatus = "verified",
                            reviewNotes = notes.ifBlank { null },
                            availableInSaaS = availableInSaaS,
                        )
                    )
                }
            ) {
                Text(text = stringResource(Res.string.widgets_review_verify))
            }
            Button(
                onClick = {
                    onReview(
                        ReviewGalleryItemBody(
                            reviewStatus = "in_review",
                            reviewNotes = notes.ifBlank { null },
                            availableInSaaS = availableInSaaS,
                        )
                    )
                },
                variant = ButtonVariant.Outline,
            ) {
                Text(text = stringResource(Res.string.widgets_review_mark_in_review))
            }
            Button(
                onClick = {
                    onReview(
                        ReviewGalleryItemBody(
                            reviewStatus = "rejected",
                            reviewNotes = notes.ifBlank { null },
                            availableInSaaS = availableInSaaS,
                        )
                    )
                },
                variant = ButtonVariant.Destructive,
            ) {
                Text(text = stringResource(Res.string.widgets_review_reject))
            }
        }

        Separator()

        Text(
            text = stringResource(Res.string.widgets_review_repin_warning),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        AppTextField(
            value = repinSha,
            onValueChange = { repinSha = it.trim() },
            label = stringResource(Res.string.widgets_review_repin_sha),
            isError = repinSha.isNotEmpty() && repinSha.length != 40,
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = repinTag,
            onValueChange = { repinTag = it },
            label = stringResource(Res.string.widgets_review_repin_tag),
            modifier = Modifier.fillMaxWidth(),
        )
        Button(
            onClick = {
                onPin(
                    PinGalleryItemBody(
                        pinnedCommitSha = repinSha.trim(),
                        pinnedTag = repinTag.trim().ifBlank { null },
                        note = notes.ifBlank { null },
                    )
                )
            },
            enabled = repinSha.length == 40,
            variant = ButtonVariant.Outline,
        ) {
            Text(text = stringResource(Res.string.widgets_review_repin))
        }
    }
}

private fun reviewFilterLabel(status: String) =
    when (status) {
        "submitted" -> Res.string.widgets_review_filter_submitted
        "in_review" -> Res.string.widgets_review_filter_in_review
        "verified" -> Res.string.widgets_review_filter_verified
        "rejected" -> Res.string.widgets_review_filter_rejected
        else -> Res.string.widgets_review_filter_submitted
    }
