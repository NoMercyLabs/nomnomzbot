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

import MediaShare from '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/media_share.vue';

// The widget is an overlay SFC served by the bot. It talks to the typed global `NomNomz` the overlay host
// injects, and plays a Twitch clip through the vendored NoMercy video player (`window.NomNomzPlayer`).
// Both are stubbed here as the outside world: the stub player behaves like the real one (events on transport).
type Handler = (data: unknown) => void;

interface PlayerItem {
  id: string;
  title: string;
  url: string;
}

const handlers: Record<string, Handler> = {};
const reports: unknown[][] = [];

const sdk = {
  onSettings: (): void => {},
  on: (type: string, cb: Handler): void => {
    handlers[type] = cb;
  },
  off: (type: string): void => {
    delete handlers[type];
  },
  reportMediaSharePlayerState: vi.fn<(...args: unknown[]) => Promise<boolean>>((...args) => {
    reports.push(args);

    return Promise.resolve(true);
  }),
};

class StubPlayer {
  listeners: Record<string, Array<() => void>> = {};
  queued: PlayerItem[][] = [];
  calls: string[] = [];
  position = 0;
  length = 0;
  level = 100;
  plugins: unknown[] = [];
  endOnStop = false;
  playsAtOnce = true;

  addPlugin(plugin: unknown): this {
    this.plugins.push(plugin);

    return this;
  }

  setup(): this {
    return this;
  }

  on(event: string, cb: () => void): void {
    this.listeners[event] = [...(this.listeners[event] ?? []), cb];
  }

  emit(event: string): void {
    for (const cb of this.listeners[event] ?? []) {
      cb();
    }
  }

  queue(items: PlayerItem[]): void {
    this.queued.push(items);
    this.position = 0;
  }

  item(): PlayerItem | undefined {
    return this.queued.at(-1)?.[0];
  }

  play(): Promise<void> {
    this.calls.push('play');

    if (this.playsAtOnce) {
      this.emit('playing');
    }

    return Promise.resolve();
  }

  pause(): Promise<void> {
    this.calls.push('pause');
    this.emit('pause');

    return Promise.resolve();
  }

  stop(): Promise<void> {
    this.calls.push('stop');

    if (this.endOnStop) {
      this.emit('ended');
    }

    return Promise.resolve();
  }

  time(seconds?: number): number | Promise<void> {
    if (seconds === undefined)
      return this.position;

    this.calls.push(`time:${seconds}`);
    this.position = seconds;

    return Promise.resolve();
  }

  duration(): number {
    return this.length;
  }

  volume(level?: number): number | Promise<void> {
    if (level === undefined)
      return this.level;

    this.calls.push(`volume:${level}`);
    this.level = level;

    return Promise.resolve();
  }
}

let player: StubPlayer;
let wrapper: VueWrapper;

function loadClip(requestId: string, slug: string, volume = 80): void {
  handlers['media_share_command']!({
    command: 'load',
    requestId,
    sourceType: 'twitch_clip',
    mediaRef: slug,
    title: 'Made up clip',
    durationSeconds: 24,
    volume,
  });
}

function send(command: Record<string, unknown>): void {
  handlers['media_share_command']!(command);
}

function frameHidden(): boolean {
  return wrapper.find('.nnz-mediashare').classes().includes('is-hidden');
}

function lastReport(): unknown[] | undefined {
  return reports.at(-1);
}

beforeEach(() => {
  vi.useFakeTimers();
  reports.length = 0;
  player = new StubPlayer();
  player.length = 24;
  vi.stubGlobal('NomNomz', sdk);

  vi.stubGlobal('NomNomzPlayer', {
    nmplayer: () => player,
    TwitchPlugin: class TwitchPluginStub {},
  });

  wrapper = mount(MediaShare, { attachTo: document.body });
});

afterEach(() => {
  wrapper.unmount();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  Reflect.deleteProperty(window, 'NomNomzPlayer');
});

describe('media share widget: Twitch clip through the player', () => {
  it('queues the clip link on the player and plays it, with no iframe', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();

    expect(player.item()?.url).toBe('https://clips.twitch.tv/MadeUpClipSlug-abc');
    expect(player.calls).toContain('play');
    expect(wrapper.find('iframe').exists()).toBe(false);
  });

  it('plays at the commanded volume', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc', 35);
    await flushPromises();

    expect(player.level).toBe(35);
  });

  it('reports the real position and duration, not a wall-clock guess', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc', 60);
    await flushPromises();
    player.position = 7.4;
    player.length = 23.6;
    await vi.advanceTimersByTimeAsync(2100);

    expect(lastReport()).toEqual(['req-1', 'playing', 7, 24, 60]);
  });

  it('pause reaches the player and is reported as paused', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    player.position = 5;
    send({ command: 'pause' });
    await flushPromises();

    expect(player.calls).toContain('pause');
    expect(lastReport()).toEqual(['req-1', 'paused', 5, 24, 80]);
  });

  it('play resumes the player after a pause', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    send({ command: 'pause' });
    player.calls.length = 0;
    send({ command: 'play' });
    await flushPromises();

    expect(player.calls).toEqual(['play']);
    expect(lastReport()?.[1]).toBe('playing');
  });

  it('the ended event sends exactly one ended report with the request id, so the queue moves on', async () => {
    loadClip('req-9', 'MadeUpClipSlug-abc');
    await flushPromises();
    player.position = 24;
    player.emit('ended');
    player.emit('ended');
    await flushPromises();

    const ended = reports.filter((r) => r[1] === 'ended');

    expect(ended).toEqual([['req-9', 'ended', 24, 24, 80]]);
  });

  it('a load while a clip plays stops the old clip and plays the new one, with no late ended', async () => {
    loadClip('req-1', 'FirstMadeUpClip');
    await flushPromises();
    player.endOnStop = true;
    player.calls.length = 0;
    loadClip('req-2', 'SecondMadeUpClip', 45);
    await flushPromises();

    expect(player.calls.indexOf('stop')).toBeLessThan(player.calls.indexOf('play'));
    expect(player.item()?.url).toBe('https://clips.twitch.tv/SecondMadeUpClip');
    expect(player.level).toBe(45);
    expect(reports.some((r) => r[1] === 'ended')).toBe(false);

    player.endOnStop = false;
    player.emit('ended');
    await flushPromises();

    expect(reports.filter((r) => r[1] === 'ended').map((r) => r[0])).toEqual(['req-2']);
  });

  it('seek moves the clip player currentTime to the commanded second', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    send({ command: 'seek', positionSeconds: 12 });
    await flushPromises();

    expect(player.calls).toContain('time:12');
    expect(player.position).toBe(12);
  });

  it('volume sets the player level and the next report carries it', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    send({ command: 'volume', volume: 40 });
    await vi.advanceTimersByTimeAsync(2100);

    expect(player.level).toBe(40);
    expect(lastReport()?.[4]).toBe(40);
  });

  it('a commanded volume sticks: the next clip starts at it and reports the real player volume', async () => {
    loadClip('req-1', 'FirstMadeUpClip', 80);
    await flushPromises();
    send({ command: 'volume', volume: 33.4 });
    await flushPromises();

    expect(player.level).toBe(33);

    player.emit('ended');
    await flushPromises();

    send({
      command: 'load',
      requestId: 'req-2',
      sourceType: 'twitch_clip',
      mediaRef: 'SecondMadeUpClip',
      durationSeconds: 20,
    });

    await flushPromises();
    await vi.advanceTimersByTimeAsync(2100);

    expect(player.level).toBe(33);
    expect(lastReport()).toEqual(['req-2', 'playing', 0, 24, 33]);
  });

  it('stop halts the player, reports ended and hides the widget', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    send({ command: 'stop' });
    await flushPromises();

    expect(player.calls).toContain('stop');
    expect(lastReport()?.[1]).toBe('ended');
    expect(frameHidden()).toBe(true);
  });

  it('keeps the frame hidden until the player really plays, and reports playing only then', async () => {
    player.playsAtOnce = false;
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();

    expect(frameHidden()).toBe(true);
    expect(reports.some((r) => r[1] === 'playing')).toBe(false);

    player.emit('playing');
    await flushPromises();

    expect(frameHidden()).toBe(false);
    expect(lastReport()?.[1]).toBe('playing');
  });

  it('never shows the frame when the clip fails to load, and reports error', async () => {
    player.playsAtOnce = false;
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    player.emit('error');
    await flushPromises();

    expect(frameHidden()).toBe(true);
    expect(lastReport()?.[1]).toBe('error');
    expect(reports.some((r) => r[1] === 'playing')).toBe(false);
  });

  it('hides the frame again for a replay until the second load really plays', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    player.emit('ended');
    await flushPromises();
    player.playsAtOnce = false;
    loadClip('req-2', 'MadeUpClipSlug-abc');
    await flushPromises();

    expect(frameHidden()).toBe(true);

    player.emit('playing');
    await flushPromises();

    expect(frameHidden()).toBe(false);
  });

  it('reports ended only from the player ended event, never from a timer', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    await vi.advanceTimersByTimeAsync(90_000);

    expect(reports.some((r) => r[1] === 'ended')).toBe(false);

    player.position = 24;
    player.emit('ended');
    await flushPromises();

    expect(lastReport()).toEqual(['req-1', 'ended', 24, 24, 80]);
  });

  it('reports error when the player raises an error', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    player.emit('error');
    await flushPromises();

    expect(lastReport()?.[1]).toBe('error');
  });

  it('plays an already played clip again when it is loaded a second time', async () => {
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();
    player.emit('ended');
    await flushPromises();
    player.calls.length = 0;
    loadClip('req-2', 'MadeUpClipSlug-abc', 55);
    await flushPromises();

    expect(player.queued).toHaveLength(2);
    expect(player.item()?.url).toBe('https://clips.twitch.tv/MadeUpClipSlug-abc');
    expect(player.calls).toContain('play');
    expect(player.level).toBe(55);

    send({ command: 'pause' });
    send({ command: 'seek', positionSeconds: 3 });
    await flushPromises();

    expect(player.calls).toEqual(expect.arrayContaining(['pause', 'time:3']));
    expect(lastReport()?.[0]).toBe('req-2');
  });

  it('loads the vendored player script when it is not on the page yet', async () => {
    Reflect.deleteProperty(window, 'NomNomzPlayer');
    loadClip('req-1', 'MadeUpClipSlug-abc');
    await flushPromises();

    const script = document.head.querySelector<HTMLScriptElement>('script[src$="/vendor/nomercy-player.js"]');

    expect(script).not.toBeNull();

    script?.onerror?.(new Event('error'));
    await flushPromises();

    expect(lastReport()?.[1]).toBe('error');
  });
});
