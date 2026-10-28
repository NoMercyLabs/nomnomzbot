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

import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { i18n } from '@/i18n';

import StepsFrame from '../components/StepsFrame.vue';
import words from '../i18n/en.json';

function mountFrame(current: number): VueWrapper {
  i18n.global.mergeLocaleMessage('en', words);

  return mount(StepsFrame, {
    props: {
      steps: [
        {
          key: 'twitch_app',
          title: 'Twitch app',
          complete: true,
        },
        {
          key: 'platform_bot',
          title: 'Bot account',
          complete: false,
        },
        {
          key: 'spotify',
          title: 'Spotify',
          complete: false,
        },
      ],
      finishTitle: 'Finish setup',
      current,
    },
    global: { plugins: [i18n] },
  });
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('the steps frame', () => {
  it('lists every step plus Finish with its number and title', () => {
    const entries = mountFrame(1).findAll('li');

    expect(entries.map((e) => e.text())).toEqual([
      expect.stringContaining('Twitch app'),
      expect.stringContaining('Bot account'),
      expect.stringContaining('Spotify'),
      expect.stringContaining('Finish setup'),
    ]);

    expect(entries.map((e) => e.get('[data-step-number]').text())).toEqual(['1', '2', '3', '4']);

    expect(entries.map((e) => e.find('[data-step-done]').exists())).toEqual([
      true,
      false,
      false,
      false,
    ]);
  });

  it('marks the current entry and the finished ones, and nothing else', () => {
    const entries = mountFrame(1).findAll('li');

    expect(entries.map((e) => e.attributes('aria-current'))).toEqual([
      undefined,
      'step',
      undefined,
      undefined,
    ]);

    expect(entries.map((e) => e.attributes('data-state'))).toEqual([
      'done',
      'current',
      'todo',
      'todo',
    ]);
  });

  it('marks the Finish entry as current on the last step', () => {
    const entries = mountFrame(3).findAll('li');

    expect(entries.map((e) => e.attributes('data-state'))).toEqual([
      'done',
      'todo',
      'todo',
      'current',
    ]);
  });

  it('draws without a Vue warning or an error, unlike the progress bar it replaces', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    mountFrame(2);

    expect(warn).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
  });

  it('is not a row of buttons, so a required step cannot be jumped over', () => {
    const wrapper = mountFrame(0);

    expect(wrapper.find('button').exists()).toBe(false);
    expect(wrapper.find('a').exists()).toBe(false);
  });

  it('shows the short label of each known key in full, never cut with an ellipsis', () => {
    i18n.global.mergeLocaleMessage('en', words);

    const wrapper = mount(StepsFrame, {
      props: {
        steps: [
          {
            key: 'twitch_app',
            title: 'Connect your Twitch application',
            complete: true,
          },
          {
            key: 'platform_bot',
            title: 'Authorize the bot account (optional)',
            complete: false,
          },
          {
            key: 'spotify',
            title: 'Spotify (optional)',
            complete: false,
          },
          {
            key: 'discord',
            title: 'Discord (optional)',
            complete: false,
          },
          {
            key: 'youtube',
            title: 'YouTube (optional)',
            complete: false,
          },
          {
            key: 'mystery',
            title: 'Something the frame does not know',
            complete: false,
          },
        ],
        finishTitle: 'Finish setup',
        current: 0,
      },
      global: { plugins: [i18n] },
    });

    const labels = wrapper.findAll('[data-step-label]');

    expect(labels.map((l) => l.text())).toEqual([
      'Twitch app',
      'Bot account',
      'Spotify',
      'Discord',
      'YouTube',
      'Something the frame does not know',
      'Finish setup',
    ]);

    labels.forEach((l) => expect(l.classes()).not.toContain('truncate'));
    expect(labels.every((l) => l.classes().includes('text-balance'))).toBe(true);
  });

  it('names a finished step "Set" for screen readers and marks the current one', () => {
    const entries = mountFrame(1).findAll('li');

    expect(entries.at(0)?.get('.sr-only').text()).toBe('Set');
    expect(entries.at(1)?.attributes('aria-current')).toBe('step');
  });

  it('says Step N of M and the current title in the compact form', () => {
    const compact = mountFrame(1).get('[data-steps-compact]');

    expect(compact.text()).toContain('Step 2 of 4');
    expect(compact.text()).toContain('Bot account');
  });
});
