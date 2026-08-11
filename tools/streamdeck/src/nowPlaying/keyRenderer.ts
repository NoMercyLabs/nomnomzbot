// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { NowPlayingState } from "./state.js";

const SIZE = 144;

/**
 * Renders the Play/Pause key as an inline SVG data URI — icon + live elapsed time in one image
 * (streamdeck-plugin.md P4). SVG rather than a canvas-rasterized PNG: the Elgato SDK's `setImage`
 * accepts `data:image/svg+xml` directly, and SVG needs no native image library / build toolchain,
 * which a canvas rasterizer would (a real constraint on a fresh install — no bundled Cairo/Skia).
 */
export function renderPlayPauseKey(state: NowPlayingState): string {
  const isPlaying = state.current?.isPlaying ?? false;
  const glyph = isPlaying
    ? `<rect x="48" y="30" width="14" height="50" fill="#fff"/><rect x="82" y="30" width="14" height="50" fill="#fff"/>`
    : `<polygon points="52,28 52,82 98,55" fill="#fff"/>`;
  const time = escapeXml(state.formattedElapsed());

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}">
    <rect width="${SIZE}" height="${SIZE}" fill="#1a1a1a"/>
    ${glyph}
    <text x="${SIZE / 2}" y="122" font-family="sans-serif" font-size="20" font-weight="bold" fill="#fff" text-anchor="middle">${time}</text>
  </svg>`;

  return `data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}`;
}

function escapeXml(value: string): string {
  return value.replace(/[<>&'"]/g, (char) => {
    switch (char) {
      case "<":
        return "&lt;";
      case ">":
        return "&gt;";
      case "&":
        return "&amp;";
      case "'":
        return "&apos;";
      default:
        return "&quot;";
    }
  });
}
