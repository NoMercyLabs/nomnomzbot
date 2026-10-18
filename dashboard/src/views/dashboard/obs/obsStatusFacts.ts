// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ObsState } from '@/api/obs';
import type { ObsStats } from '@/api/obsOutputs';

import { wholeSeconds } from './outputFormat';

export type StreamState = 'live' | 'reconnecting' | 'off';
export type RecordState = 'recording' | 'paused' | 'off';

// Skipped over total, as a fraction (0.01 is 1%); the page formats it in the reader's locale.
export type FrameShare = {
  skipped: number;
  total: number;
  fraction: number;
};

export type ObsStatusFacts = {
  stream: { state: StreamState; timer: string };
  record: { state: RecordState; timer: string };
  streamKbps: number | null;
  dropped: FrameShare;
  render: FrameShare;
  cpu: number;
  fps: number;
  targetFps: number;
  memory: number;
  congestion: number;
};

// The stream byte counter at one moment, kept by the page to turn two reads into a bitrate.
export type ByteSample = { at: number; bytes: number };

export function byteSample(stats: ObsStats, at: number): ByteSample {
  return { at, bytes: Number(stats.streamBytes) };
}

function share(skipped: number | string, total: number | string): FrameShare {
  const all = Number(total);

  return {
    skipped: Number(skipped),
    total: all,
    fraction: all > 0
      ? Number(skipped) / all
      : 0,
  };
}

function streamState(stats: ObsStats): StreamState {
  if (!stats.streamActive)
    return 'off';

  return stats.streamReconnecting
    ? 'reconnecting'
    : 'live';
}

function recordState(stats: ObsStats): RecordState {
  if (!stats.recordActive)
    return 'off';

  return stats.recordPaused
    ? 'paused'
    : 'recording';
}

// OBS's status bar rate: bytes sent between two reads, in kilobits per second. No rate without an earlier read,
// while the stream is off, or when the counter went backwards (a new stream).
function bitrate(stats: ObsStats, previous: ByteSample | null, at: number): number | null {
  if (!previous || !stats.streamActive)
    return null;

  const seconds = (at - previous.at) / 1000;
  const bytes = Number(stats.streamBytes) - previous.bytes;

  if (seconds <= 0 || bytes < 0)
    return null;

  return Math.round((bytes * 8) / 1000 / seconds);
}

// What OBS's own status bar shows. "Dropped frames" there is the stream output's own skipped/total
// (GetStreamStatus), not the output counters of GetStats, which are a different count.
export function obsStatusFacts(
  stats: ObsStats,
  previous: ByteSample | null,
  at: number = Date.now(),
): ObsStatusFacts {
  return {
    stream: {
      state: streamState(stats),
      timer: stats.streamActive
        ? wholeSeconds(stats.streamTimecode)
        : '',
    },
    record: {
      state: recordState(stats),
      timer: stats.recordActive
        ? wholeSeconds(stats.recordTimecode)
        : '',
    },
    streamKbps: bitrate(stats, previous, at),
    dropped: share(stats.streamSkippedFrames, stats.streamTotalFrames),
    render: share(stats.renderSkippedFrames, stats.renderTotalFrames),
    cpu: Number(stats.cpuUsage),
    fps: Number(stats.activeFps),
    targetFps: Number(stats.targetFps),
    memory: Number(stats.memoryUsage),
    congestion: Number(stats.streamCongestion),
  };
}

// The stream and recording facts as the page knows them right now. The OBS stats read is polled; a pushed
// change lands in the state read at once, so whichever read is newer wins until the next poll.
export type LiveOutputs = {
  streaming: boolean;
  reconnecting: boolean;
  streamTimecode: string | null;
  recording: boolean;
  paused: boolean;
  recordTimecode: string | null;
};

export type LiveSources = {
  stats: ObsStats | null | undefined;
  statsAt: number;
  state: ObsState | null | undefined;
  stateAt: number;
};

function fromState(state: ObsState, reconnecting: boolean): LiveOutputs {
  return {
    streaming: state.streaming,
    reconnecting: state.streaming && reconnecting,
    streamTimecode: state.streamTimecode ?? null,
    recording: state.recording,
    paused: state.recording && state.recordPaused,
    recordTimecode: state.recordTimecode ?? null,
  };
}

function fromStats(stats: ObsStats): LiveOutputs {
  return {
    streaming: stats.streamActive,
    reconnecting: stats.streamActive && stats.streamReconnecting,
    streamTimecode: stats.streamTimecode ?? null,
    recording: stats.recordActive,
    paused: stats.recordActive && stats.recordPaused,
    recordTimecode: stats.recordTimecode ?? null,
  };
}

export function liveOutputs(sources: LiveSources): LiveOutputs | null {
  const { stats, statsAt, state, stateAt } = sources;

  if (state && (!stats || stateAt > statsAt))
    return fromState(state, stats?.streamReconnecting === true);

  return stats
    ? fromStats(stats)
    : null;
}

function liveStreamState(live: LiveOutputs): StreamState {
  if (!live.streaming)
    return 'off';

  return live.reconnecting
    ? 'reconnecting'
    : 'live';
}

function liveRecordState(live: LiveOutputs): RecordState {
  if (!live.recording)
    return 'off';

  return live.paused
    ? 'paused'
    : 'recording';
}

// The strip's stream and recording cells follow the newest read, so a push shows before the next poll.
export function withLiveOutputs(facts: ObsStatusFacts, live: LiveOutputs | null): ObsStatusFacts {
  if (!live)
    return facts;

  return {
    ...facts,
    stream: {
      state: liveStreamState(live),
      timer: live.streaming
        ? wholeSeconds(live.streamTimecode)
        : '',
    },
    record: {
      state: liveRecordState(live),
      timer: live.recording
        ? wholeSeconds(live.recordTimecode)
        : '',
    },
  };
}
