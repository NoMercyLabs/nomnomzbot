// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';

import type { ObsStats } from '@/api/obsOutputs';

import type { ObsState } from '@/api/obs';

import {
  byteSample,
  liveOutputs,
  obsStatusFacts,
  withLiveOutputs,
} from '../obsStatusFacts';

// A payload as the server maps it from GetStats + GetStreamStatus + GetRecordStatus + GetVideoSettings,
// at the moment OBS's own status bar read: 0 dropped, 30/30 FPS, CPU 1.5%, both timers 01:55:40.
const recorded: ObsStats = {
  cpuUsage: 1.5,
  memoryUsage: 512,
  activeFps: 30,
  renderTotalFrames: 207_000,
  renderSkippedFrames: 57,
  outputTotalFrames: 207_000,
  outputSkippedFrames: 55,
  streamActive: true,
  streamReconnecting: false,
  streamCongestion: 0.25,
  streamBytes: 7_000_000_000,
  streamTotalFrames: 207_000,
  streamSkippedFrames: 0,
  streamTimecode: '01:55:40.123',
  recordActive: true,
  recordPaused: false,
  recordBytes: 9_000_000_000,
  recordTimecode: '01:55:40.456',
  targetFps: 30,
};

describe('obsStatusFacts', () => {
  it('reads dropped frames from the stream output, not from the GetStats output counters', () => {
    const facts = obsStatusFacts(recorded, null);

    expect(facts.dropped).toEqual({
      skipped: 0,
      total: 207_000,
      fraction: 0,
    });

    expect(facts.render).toEqual({
      skipped: 57,
      total: 207_000,
      fraction: 57 / 207_000,
    });
  });

  it('says live while the stream output is active, with the stream timer', () => {
    const facts = obsStatusFacts(recorded, null);

    expect(facts.stream).toEqual({ state: 'live', timer: '01:55:40' });
  });

  it('says reconnecting while OBS reconnects, and off when the output is not active', () => {
    const reconnecting = obsStatusFacts({ ...recorded, streamReconnecting: true }, null);
    const off = obsStatusFacts({
      ...recorded,
      streamActive: false,
      streamTimecode: null,
    }, null);

    expect(reconnecting.stream.state).toBe('reconnecting');
    expect(off.stream).toEqual({ state: 'off', timer: '' });
  });

  it('shows the recording timer, and paused when the recording is paused', () => {
    const running = obsStatusFacts(recorded, null);
    const paused = obsStatusFacts({ ...recorded, recordPaused: true }, null);

    expect(running.record).toEqual({ state: 'recording', timer: '01:55:40' });
    expect(paused.record.state).toBe('paused');
  });

  it('computes kbps from two byte samples: 1,125,000 bytes in 1 second is 9000 kbps', () => {
    const before = byteSample(recorded, 10_000);
    const later: ObsStats = { ...recorded, streamBytes: 7_000_000_000 + 1_125_000 };
    const facts = obsStatusFacts(later, before, 11_000);

    expect(facts.streamKbps).toBe(9000);
  });

  it('has no bitrate with one sample, when the stream is off, or when the byte counter reset', () => {
    const before = byteSample(recorded, 10_000);
    const reset: ObsStats = { ...recorded, streamBytes: 100 };

    expect(obsStatusFacts(recorded, null, 11_000).streamKbps).toBeNull();
    const stopped = obsStatusFacts({ ...recorded, streamActive: false }, before, 11_000);

    expect(stopped.streamKbps).toBeNull();
    expect(obsStatusFacts(reset, before, 11_000).streamKbps).toBeNull();
  });

  it('carries cpu, fps, target fps, memory and congestion as numbers', () => {
    const facts = obsStatusFacts(recorded, null);

    expect(facts.cpu).toBe(1.5);
    expect(facts.fps).toBe(30);
    expect(facts.targetFps).toBe(30);
    expect(facts.memory).toBe(512);
    expect(facts.congestion).toBe(0.25);
  });
});

describe('liveOutputs', () => {
  const idle: ObsState = {
    currentScene: 'Main',
    streaming: false,
    recording: false,
    recordPaused: false,
    replayBufferActive: false,
    recordTimecode: null,
    streamTimecode: null,
  };

  it('takes the stats read while it is the newest', () => {
    const live = liveOutputs({
      stats: recorded,
      statsAt: 200,
      state: idle,
      stateAt: 100,
    });

    expect(live?.streaming).toBe(true);
    expect(live?.streamTimecode).toBe('01:55:40.123');
  });

  it('takes a pushed state that is newer than the poll, so a stop shows at once', () => {
    const live = liveOutputs({
      stats: recorded,
      statsAt: 100,
      state: idle,
      stateAt: 200,
    });
    const facts = withLiveOutputs(obsStatusFacts(recorded, null), live);

    expect(facts.stream).toEqual({ state: 'off', timer: '' });
    expect(facts.record).toEqual({ state: 'off', timer: '' });
    expect(facts.cpu).toBe(1.5);
  });

  it('shows a pushed start as live with the timer at zero', () => {
    const started: ObsState = {
      ...idle,
      streaming: true,
      streamTimecode: '00:00:00.000',
    };
    const live = liveOutputs({
      stats: { ...recorded, streamActive: false },
      statsAt: 100,
      state: started,
      stateAt: 200,
    });
    const facts = withLiveOutputs(obsStatusFacts(recorded, null), live);

    expect(facts.stream).toEqual({ state: 'live', timer: '00:00:00' });
  });
});
