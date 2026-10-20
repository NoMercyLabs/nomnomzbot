// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { beforeEach, describe, expect, it, vi } from 'vitest';

import { exportRecapPng, pngSize, recapResolution } from '../recapExport';

type CaptureOptions = {
  width?: number;
  height?: number;
  pixelRatio?: number;
};

// A PNG data URL whose header (the IHDR chunk) states the given size; the pixels are not needed to read the size.
function pngOf(width: number, height: number): string {
  const bytes = new Uint8Array(24);
  const view = new DataView(bytes.buffer);

  bytes.set([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a], 0);
  bytes.set([0x49, 0x48, 0x44, 0x52], 12);
  view.setUint32(16, width);
  view.setUint32(20, height);

  return `data:image/png;base64,${btoa(String.fromCharCode(...bytes))}`;
}

const capture = vi.fn<(node: HTMLElement, options: CaptureOptions) => Promise<string>>();

vi.mock('html-to-image', () => ({
  toPng: (node: HTMLElement, options: CaptureOptions) => capture(node, options),
}));

beforeEach(() => {
  capture.mockReset();
});

describe('recapResolution', () => {
  it('uses the stored output resolution', () => {
    expect(recapResolution({ outputWidth: 2560, outputHeight: 1440 })).toEqual({
      width: 2560,
      height: 1440,
      assumed: false,
    });
  });

  it('falls back to 1920 x 1080 and says it is assumed for an old recap', () => {
    expect(recapResolution({ outputWidth: null, outputHeight: null })).toEqual({
      width: 1920,
      height: 1080,
      assumed: true,
    });
  });
});

describe('exportRecapPng', () => {
  it('produces a PNG whose pixel size equals the stored resolution exactly', async () => {
    capture.mockImplementation(async (_node, options) =>
      pngOf(options.width! * options.pixelRatio!, options.height! * options.pixelRatio!));

    const url = await exportRecapPng(document.createElement('div'), { width: 2560, height: 1440 });

    expect(url.startsWith('data:image/png;base64,')).toBe(true);
    expect(pngSize(url)).toEqual({ width: 2560, height: 1440 });
    expect(capture.mock.calls[0]![1].pixelRatio).toBe(1);
  });

  it('refuses a capture whose size differs from the resolution', async () => {
    capture.mockResolvedValue(pngOf(1000, 500));

    await expect(
      exportRecapPng(document.createElement('div'), { width: 1920, height: 1080 }),
    ).rejects.toThrow(/1000x500.*1920x1080/);
  });
});
