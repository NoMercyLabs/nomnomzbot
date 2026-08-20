// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.io

/**
 * Opens [url] as a standalone, chrome-less browser window (Chrome/Edge `--app=` mode) sized to
 * [width]x[height] — for widgets whose audio OBS's own embedded browser-source can't reliably
 * capture (Spotify's Web Playback SDK audio is DRM/EME-protected; OBS's CEF audio hook drops it
 * after ~10s). The resulting OS window is captured in OBS as a normal Window Capture +
 * Application Audio Capture instead, which uses the regular Windows audio pipeline.
 *
 * Desktop-only: spawning an OS process is unavailable to a browser tab, so the web build always
 * returns `false`. Callers hide or disable the trigger there rather than showing a dead button.
 */
expect fun openCaptureWindow(url: String, width: Int, height: Int): Boolean

/** Whether [openCaptureWindow] can do anything on this platform — desktop only. */
expect fun captureWindowSupported(): Boolean
