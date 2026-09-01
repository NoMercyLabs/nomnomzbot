// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.songrequests.ui

import kotlin.test.Test
import kotlin.test.assertEquals

// Proves the Song Requests config editor's numeric steppers respect the REAL backend bounds
// ([Range(1, 500)] on MaxQueueSize, [Range(1, 50)] on MaxRequestsPerUser —
// server/src/NomNomzBot.Application/Music/Dtos/MusicConfigDtos.cs `UpdateMusicConfigDto`), not an
// invented client-side cap. A candidate value below or above the range is clamped into it; a value
// already inside the range passes through unchanged.
class SongRequestsScreenTest {

    @Test
    fun clampMaxQueueSize_cannot_go_below_the_backend_minimum_of_1() {
        assertEquals(1, clampMaxQueueSize(0))
        assertEquals(1, clampMaxQueueSize(-50))
    }

    @Test
    fun clampMaxQueueSize_cannot_go_above_the_backend_maximum_of_500() {
        assertEquals(500, clampMaxQueueSize(501))
        assertEquals(500, clampMaxQueueSize(100_000))
    }

    @Test
    fun clampMaxQueueSize_passes_through_a_value_already_inside_the_backend_range() {
        assertEquals(50, clampMaxQueueSize(50))
        assertEquals(1, clampMaxQueueSize(1))
        assertEquals(500, clampMaxQueueSize(500))
    }

    @Test
    fun clampMaxRequestsPerUser_cannot_go_below_the_backend_minimum_of_1() {
        assertEquals(1, clampMaxRequestsPerUser(0))
        assertEquals(1, clampMaxRequestsPerUser(-5))
    }

    @Test
    fun clampMaxRequestsPerUser_cannot_go_above_the_backend_maximum_of_50() {
        assertEquals(50, clampMaxRequestsPerUser(51))
        assertEquals(50, clampMaxRequestsPerUser(1_000))
    }

    @Test
    fun clampMaxRequestsPerUser_passes_through_a_value_already_inside_the_backend_range() {
        assertEquals(5, clampMaxRequestsPerUser(5))
        assertEquals(1, clampMaxRequestsPerUser(1))
        assertEquals(50, clampMaxRequestsPerUser(50))
    }
}
