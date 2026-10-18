// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { ObsStats } from '@/api/obsOutputs';
import { i18n } from '@/i18n';

import ObsStatusBar from '../components/ObsStatusBar.vue';
import en from '../i18n/en.json';

// The server writes a double that OBS could not measure as the text "NaN" (a numeric string in the schema).
const unmeasured: ObsStats = {
  cpuUsage: 'NaN',
  memoryUsage: 'NaN',
  activeFps: 30,
  renderTotalFrames: 100,
  renderSkippedFrames: 1,
  outputTotalFrames: 100,
  outputSkippedFrames: 1,
  streamActive: false,
  streamReconnecting: false,
  streamCongestion: 'NaN',
  streamBytes: 0,
  streamTotalFrames: 0,
  streamSkippedFrames: 0,
  recordActive: false,
  recordPaused: false,
  recordBytes: 0,
  targetFps: 'NaN',
};

describe('ObsStatusBar', () => {
  beforeEach(() => {
    i18n.global.mergeLocaleMessage('en', en);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('never hands NaN to the number formatter and shows a dash for a figure OBS did not measure', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    const bar = mount(ObsStatusBar, {
      props: {
        stats: unmeasured,
        readAt: 1,
        live: null,
        streamTimer: '',
        recordTimer: '',
      },
      global: { plugins: [i18n] },
    });

    const notices = warn.mock.calls.map((call) => String(call[0]));

    expect(notices.filter((text) => text.includes('number formatting'))).toEqual([]);
    expect(bar.text()).not.toContain('NaN');
    expect(bar.text()).toContain('30.0');
  });
});
