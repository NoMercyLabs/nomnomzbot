// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing

/** Test tag on every skeleton preset's container: present while a screen shows its loading placeholder. */
const val SkeletonLoadingTag: String = "skeleton-loading"

private val RowMarkSize: Dp = 32.dp
private val RowTitleHeight: Dp = 16.dp
private val RowDetailHeight: Dp = 12.dp
private val CardBlockHeight: Dp = 120.dp
private val FieldLabelHeight: Dp = 12.dp
private val FieldInputHeight: Dp = 40.dp
private const val RowTitleWidthFraction: Float = 0.4f
private const val RowDetailWidthFraction: Float = 0.7f
private const val FieldLabelWidthFraction: Float = 0.25f

/** Skeleton shaped like a list of rows: a leading mark, a title line and a shorter detail line each. */
@Composable
fun SkeletonList(modifier: Modifier = Modifier, rows: Int = 6) {
    val spacing = LocalSpacing.current

    Column(
        modifier = modifier.fillMaxSize().padding(spacing.s4).testTag(SkeletonLoadingTag),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        repeat(rows) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Skeleton(Modifier.size(RowMarkSize))
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s1_5)) {
                    Skeleton(Modifier.fillMaxWidth(RowTitleWidthFraction).height(RowTitleHeight))
                    Skeleton(Modifier.fillMaxWidth(RowDetailWidthFraction).height(RowDetailHeight))
                }
            }
        }
    }
}

/** Skeleton shaped like a grid of cards: [rows] rows of [columns] equal blocks. */
@Composable
fun SkeletonCardGrid(modifier: Modifier = Modifier, rows: Int = 2, columns: Int = 3) {
    val spacing = LocalSpacing.current

    Column(
        modifier = modifier.fillMaxSize().padding(spacing.s4).testTag(SkeletonLoadingTag),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        repeat(rows) {
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(spacing.s4)) {
                repeat(columns) { Skeleton(Modifier.weight(1f).height(CardBlockHeight)) }
            }
        }
    }
}

/** Skeleton shaped like a form: [fields] stacked label-plus-input pairs. */
@Composable
fun SkeletonForm(modifier: Modifier = Modifier, fields: Int = 4) {
    val spacing = LocalSpacing.current

    Column(
        modifier = modifier.fillMaxSize().padding(spacing.s4).testTag(SkeletonLoadingTag),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        repeat(fields) {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1_5)) {
                Skeleton(Modifier.fillMaxWidth(FieldLabelWidthFraction).height(FieldLabelHeight))
                Skeleton(Modifier.fillMaxWidth().height(FieldInputHeight))
            }
        }
    }
}
