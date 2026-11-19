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

import nmplayer from '@nomercy-entertainment/nomercy-video-player';
import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import StreamPlayer from '../components/StreamPlayer.vue';

// Stoney 2026-11-19: "my player embed in the chat page does not produce audio".
// The real player runs here. Only the network is cut: Twitch is never called from a test.
let mounted: VueWrapper | null = null;

function video(): HTMLVideoElement {
  return document.body.querySelector<HTMLVideoElement>('[data-stream-player] video')!;
}

function player(): ReturnType<typeof nmplayer> {
  return nmplayer(document.body.querySelector('[data-stream-player] > div')!.id);
}

async function open(): Promise<void> {
  mounted = mount(StreamPlayer, {
    attachTo: document.body,
    props: {
      login: 'stoney_eagle',
      title: 'Building the bot live',
      live: true,
      offlineLabel: 'Offline',
    },
  });

  await vi.waitFor(() =>
    expect(video()).not.toBeNull());
}

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn(() =>
    Promise.reject(new Error('no network in tests'))));
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  vi.unstubAllGlobals();
});

describe('StreamPlayer sound', () => {
  it('starts silent, and the player itself knows it is muted', async () => {
    await open();

    expect(video().muted).toBe(true);
    expect(player().volume()).toBe(0);
  });

  it('the volume slider gives sound: a level above zero unmutes the video', async () => {
    await open();

    await player().volume(40);

    expect(video().muted).toBe(false);
    expect(video().volume).toBeGreaterThan(0);
    expect(player().volume()).toBe(40);
  });

  it('one press on the mute button gives sound', async () => {
    await open();

    player().toggleMute();

    await vi.waitFor(() =>
      expect(video().muted).toBe(false));
  });
});
