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
import { loadIconMarkup } from "./iconAssets.js";

const SIZE = 144;
/** Icons are drawn on a 24x24 source viewBox scaled 3x (=72px) inside the 144px key, leaving an
 * even 36px (25%) margin on every side — the "a little padding" every key face needs. */
const ICON_SCALE = 3;
const ICON_OFFSET = (SIZE - 24 * ICON_SCALE) / 2;

/** The default key background — every action's Property Inspector exposes a color picker to override it. */
export const DEFAULT_BACKGROUND = "#1a1a1a";

/**
 * Renders the Play/Pause key as an inline SVG data URI — icon + live elapsed time in one image
 * (streamdeck-plugin.md P4). SVG rather than a canvas-rasterized PNG: the Elgato SDK's `setImage`
 * accepts `data:image/svg+xml` directly, and SVG needs no native image library / build toolchain,
 * which a canvas rasterizer would (a real constraint on a fresh install — no bundled Cairo/Skia).
 */
export function renderPlayPauseKey(state: NowPlayingState, backgroundColor: string = DEFAULT_BACKGROUND): string {
  // Icon block (y 16-64, 48 tall) and the time text below it are centered as ONE unit within the
  // 144px key, not independently — resizing either one means re-centering both together. Both glyphs'
  // bounding boxes are centered on x=72 (SIZE/2), not eyeballed.
  const isPlaying = state.current?.isPlaying ?? false;
  const glyph = isPlaying
    ? `<rect x="48" y="16" width="16" height="48" fill="#fff"/><rect x="80" y="16" width="16" height="48" fill="#fff"/>`
    : `<polygon points="50,12 50,68 96,40" fill="#fff"/>`;
  const time = escapeXml(state.formattedElapsed());

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}">
    <rect width="${SIZE}" height="${SIZE}" rx="24" fill="${backgroundColor}"/>
    ${glyph}
    <text x="${SIZE / 2}" y="126" font-family="sans-serif" font-size="42" font-weight="bold" fill="#fff" text-anchor="middle">${time}</text>
  </svg>`;

  return `data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}`;
}

/**
 * Renders any other action's key: the recolored (white) source icon, padded and centered, over a
 * configurable background — the generic renderer every non-live action goes through, so "padding"
 * and "background color" are one mechanism instead of a per-action special case.
 */
export function renderIconKey(iconName: string, backgroundColor: string = DEFAULT_BACKGROUND): string {
  const source = loadIconMarkup(iconName);
  const inner = source.replace(/^<svg[^>]*>/, "").replace(/<\/svg>\s*$/, "");

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}">
    <rect width="${SIZE}" height="${SIZE}" rx="24" fill="${backgroundColor}"/>
    <g transform="translate(${ICON_OFFSET},${ICON_OFFSET}) scale(${ICON_SCALE})">${inner}</g>
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
