// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import MusicPlayer from '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/music_player.vue';

// The widget is an overlay SFC served by the bot. The typed global `NomNomz` and the YouTube IFrame player are the
// outside world and are stubbed. The stub player only records the volume it is given: no sound is made.
type Handler = (data: unknown) => void;

const handlers: Record<string, Handler> = {};

const sdk = {
  settings: {
    volume: 100,
    muted: false,
    enableAudio: true,
    showIndicator: false,
  },
  onSettings: (cb: Handler): void => {
    handlers['settings'] = cb;
  },
  on: (type: string, cb: Handler): void => {
    handlers[type] = cb;
  },
  off: (type: string): void => {
    delete handlers[type];
  },
  data: { nowPlaying: (): Promise<never> => Promise.reject(new Error('offline')) },
  reportYouTubePlayerState: vi.fn<(...args: unknown[]) => Promise<boolean>>(() =>
    Promise.resolve(true),
  ),
};

class StubYouTubePlayer {
  volumes: number[] = [];
  setVolume(level: number): void {
    this.volumes.push(level);
  }

  loadVideoById(): void {}
  pauseVideo(): void {}
  playVideo(): void {}
  stopVideo(): void {}
  seekTo(): void {}
  getCurrentTime(): number {
    return 0;
  }

  getDuration(): number {
    return 0;
  }

  getPlayerState(): number {
    return 1;
  }

  getVideoData(): { video_id: string } {
    return { video_id: 'abc' };
  }

  isMuted(): boolean {
    return false;
  }

  unMute(): void {}
}

let player: StubYouTubePlayer;
let wrapper: VueWrapper;

function send(type: string, data: unknown): void {
  handlers[type]?.(data);
}

async function mountWithAVideo(): Promise<void> {
  wrapper = mount(MusicPlayer);
  await flushPromises();
  send('youtube.play', { videoId: 'abc' });
  await flushPromises();
  player.volumes.length = 0;
}

beforeEach(() => {
  vi.useFakeTimers();
  player = new StubYouTubePlayer();
  vi.stubGlobal('NomNomz', sdk);
  const stubYt = {
    Player: class {
      constructor(
        _el: HTMLElement,
        options: { events: { onReady: (e: { target: StubYouTubePlayer }) => void } },
      ) {
        options.events.onReady({ target: player });
      }
    },
  };

  vi.stubGlobal('YT', stubYt);
  (window as unknown as { YT: unknown }).YT = stubYt;
});

afterEach(() => {
  wrapper.unmount();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  delete (window as unknown as { YT?: unknown }).YT;
});

describe('music_player crossfade (youtube.volume with fadeMs)', () => {
  it('a fade up ramps the video from silence to the level in the page and ends at that level', async () => {
    await mountWithAVideo();
    send('youtube.volume', { percent: 0, fadeMs: 0 });
    player.volumes.length = 0;

    send('youtube.volume', { percent: 100, fadeMs: 3000 });
    vi.advanceTimersByTime(1500);
    const half: number[] = [...player.volumes];

    vi.advanceTimersByTime(1600);

    expect(half[0]).toBe(0);
    expect(half.at(-1)).toBeGreaterThanOrEqual(45);
    expect(half.at(-1)).toBeLessThanOrEqual(55);
    expect(half).toEqual([...half].sort((a, b) => a - b));
    expect(player.volumes.at(-1)).toBe(100);
  });

  it('a fade to 0 ramps down, holds the silence, and the next video plays at the level the streamer set', async () => {
    await mountWithAVideo();

    send('youtube.volume', { percent: 0, fadeMs: 3000 });
    vi.advanceTimersByTime(3200);
    const faded: number[] = [...player.volumes];

    vi.advanceTimersByTime(10000);

    expect(faded[0]).toBe(100);
    expect(faded.at(-1)).toBe(0);
    expect(faded).toEqual([...faded].sort((a, b) => b - a));
    expect(player.volumes.at(-1)).toBe(0);

    send('youtube.play', { videoId: 'next' });
    send('youtube.volume', { percent: 100, fadeMs: 0 });

    expect(player.volumes.at(-1)).toBe(100);
  });

  it('a plain volume change in the middle of a fade stops the fade at once', async () => {
    await mountWithAVideo();
    send('youtube.volume', { percent: 0, fadeMs: 3000 });
    vi.advanceTimersByTime(1000);

    send('youtube.volume', { percent: 60, fadeMs: 0 });
    const after = player.volumes.length;

    vi.advanceTimersByTime(5000);

    expect(player.volumes.at(-1)).toBe(60);
    expect(player.volumes.length).toBe(after);
  });

  it('a fade never lifts a muted page above 0', async () => {
    await mountWithAVideo();
    send('settings', { muted: true });
    player.volumes.length = 0;
    send('youtube.volume', { percent: 100, fadeMs: 3000 });
    vi.advanceTimersByTime(3200);

    expect(Math.max(...player.volumes)).toBe(0);
  });
});
