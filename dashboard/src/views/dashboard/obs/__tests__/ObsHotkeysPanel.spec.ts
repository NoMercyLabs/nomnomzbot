// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import ObsHotkeysPanel from '../components/ObsHotkeysPanel.vue';
import en from '../i18n/en.json';

type Post = { path: string; body: unknown };

const posts: Post[] = [];
const mockNames: string[] = ['OBSBasic.StartStreaming', 'OBSBasic.StartRecording'];

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      const rowsByPath: Record<string, unknown[]> = {
        '/obs/scenes': [{ name: 'Main', isCurrent: true }],
        '/obs/scene-items': [{
          sceneItemId: 5,
          sourceName: 'Webcam',
          enabled: true,
          locked: false,
        }],
      };
      const suffix = Object.keys(rowsByPath).find((key) => path.endsWith(key));

      return {
        data: { data: suffix
          ? rowsByPath[suffix]
          : [...mockNames] },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, init: { body?: unknown }) => {
      posts.push({ path, body: init.body });

      return { data: { data: {} }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  posts.length = 0;
  mockNames.splice(0, mockNames.length, 'OBSBasic.StartStreaming', 'OBSBasic.StartRecording');
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('ObsHotkeysPanel', () => {
  it('lists the hotkeys OBS knows and fires the one clicked by its name', async () => {
    mounted = mount(ObsHotkeysPanel, {
      attachTo: document.body,
      global: { plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }], i18n] },
      props: { offline: false },
    });

    await flushPromises();

    const buttons = mounted.findAll('[data-obs-hotkey]');

    expect(buttons.map((button) => button.attributes('data-obs-hotkey'))).toEqual([
      'OBSBasic.StartStreaming',
      'OBSBasic.StartRecording',
    ]);

    await buttons[1]?.trigger('click');
    await flushPromises();

    expect(posts).toEqual([
      {
        path: '/api/v{version}/channels/{channelId}/obs/hotkeys/trigger',
        body: { hotkeyName: 'OBSBasic.StartRecording' },
      },
    ]);
  });

  it('groups hotkeys by purpose, hides ones that name no source behind Other, and searches', async () => {
    mockNames.splice(
      0,
      mockNames.length,
      'OBSBasic.StartStreaming',
      'OBSBasic.StartRecording',
      'libobs.mute',
      'libobs.mute',
      'libobs.show_scene_item.5',
      'MyPlugin.do_the_thing',
    );

    mounted = mount(ObsHotkeysPanel, {
      attachTo: document.body,
      global: { plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }], i18n] },
      props: { offline: false },
    });

    await flushPromises();

    expect(mounted.text()).toContain('Press an OBS hotkey from here');

    expect(mounted.findAll('[data-obs-hotkey-group]').map((group) => group.attributes('data-obs-hotkey-group'))).toEqual([
      'stream',
      'recording',
      'more',
    ]);

    expect(mounted.findAll('[data-obs-hotkey]').map((button) => button.text())).toEqual([
      'Start streaming',
      'Start recording',
      'My plugin do the thing',
      'Show Webcam',
    ]);

    const other = mounted.find('[data-obs-hotkey-other]');

    expect(other.text()).toContain('Other (1)');

    await other.trigger('click');
    await flushPromises();

    const unresolved = [...document.querySelectorAll('[data-obs-hotkey-unresolved]')];

    expect(unresolved.map((button) => button.getAttribute('data-obs-hotkey-unresolved'))).toEqual(['libobs.mute']);
    expect(unresolved.map((button) => button.textContent?.trim())).toEqual(['Mute a source']);

    const scene = mounted.find('[data-obs-hotkey-scene="Main"]');

    expect(scene.text()).toContain('Scene: Main');
    expect(scene.findAll('button').map((button) => button.text())).toEqual(['Show Webcam']);
    expect(mounted.text()).not.toContain('libobs.');
    expect(document.body.textContent).not.toMatch(/scene item \d/i);

    await mounted.find('input[type="search"]').setValue('record');

    expect(mounted.findAll('[data-obs-hotkey]').map((button) => button.text())).toEqual(['Start recording']);
  });
});
