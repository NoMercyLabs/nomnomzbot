// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { toPng } from 'html-to-image';

export type RecapSize = { width: number; height: number };

export type RecapResolution = RecapSize & { assumed: boolean };

// An old recap stored no output resolution; this is the size the page assumes (and labels as assumed).
const FALLBACK = { width: 1920, height: 1080 } as const satisfies RecapSize;

// The IHDR chunk starts at byte 8 of a PNG; its width and height are the 4-byte numbers at bytes 16 and 20.
const IHDR_WIDTH = 16;
const IHDR_HEIGHT = 20;

export function recapResolution(recap: {
  outputWidth?: number | null;
  outputHeight?: number | null;
}): RecapResolution {
  const { outputWidth: width, outputHeight: height } = recap;

  return width && height
    ? {
        width,
        height,
        assumed: false,
      }
    : { ...FALLBACK, assumed: true };
}

export function pngSize(dataUrl: string) {
  const start = dataUrl.indexOf(',') + 1;
  const header = dataUrl.slice(start, start + 32);
  const binary = atob(header);
  const bytes = Uint8Array.from(binary, (char) =>
    char.charCodeAt(0));
  const view = new DataView(bytes.buffer);
  const width = view.getUint32(IHDR_WIDTH);
  const height = view.getUint32(IHDR_HEIGHT);

  return { width, height } as const satisfies RecapSize;
}

// Captures the node at exactly `size` pixels (one image pixel per CSS pixel) and checks the PNG that came back.
export async function exportRecapPng(node: HTMLElement, size: RecapSize): Promise<string> {
  const url = await toPng(node, {
    width: size.width,
    height: size.height,
    pixelRatio: 1,
    cacheBust: true,
    style: { transform: 'none' },
  });
  const got = pngSize(url);

  if (got.width !== size.width || got.height !== size.height)
    throw new Error(`The PNG is ${got.width}x${got.height}, not ${size.width}x${size.height}.`);

  return url;
}
