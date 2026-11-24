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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import type { ObsConnection } from '@/api/obs';
import { i18n } from '@/i18n';

import ObsConnectionBar from '../components/ObsConnectionBar.vue';
import ObsSetupGuide from '../components/ObsSetupGuide.vue';
import en from '../i18n/en.json';

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (): boolean => true }),
}));

vi.mock('@/api/obsControl', () => ({
  useObsSaveConnection: () => ({ isPending: ref(false), mutate: vi.fn<() => void>() }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

const base: ObsConnection = {
  mode: 'direct',
  host: 'localhost',
  port: 4455,
  hasPassword: true,
  hasBridgeToken: false,
  eventSubscriptionsMask: 5,
  isEnabled: true,
  lastError: null,
  lastConnectedAt: '2026-10-18T10:00:00Z',
  eventKinds: [],
};

let mounted: VueWrapper | null = null;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
});

function show(connection: ObsConnection, unreachable: boolean): VueWrapper {
  mounted = mount(ObsSetupGuide, {
    global: { plugins: [i18n] },
    props: { connection, unreachable },
  });

  return mounted;
}

const done = (view: VueWrapper): string[] =>
  view
    .findAll('[data-guide-step][data-done="true"]')
    .map((step) => step.attributes('data-guide-step') ?? '');

const fix = (view: VueWrapper): string => view.get('[data-guide-fix]').text();

describe('ObsSetupGuide', () => {
  it('lists the four numbered steps in order, with the OBS menu path in words', () => {
    const view = show({ ...base, lastError: 'Connection refused' }, true);
    const steps = view.findAll('[data-guide-step]').map((step) => step.attributes('data-guide-step'));

    expect(steps).toEqual(['server', 'password', 'save', 'connected']);
    expect(view.text()).toContain('Tools');
    expect(view.text()).toContain('WebSocket Server Settings');
    expect(view.text()).toContain('4455');
  });

  it('server off or wrong port: tells the user to check OBS and the port, and ticks only the saved link', () => {
    const view = show({ ...base, lastError: 'Connection refused' }, true);

    expect(view.get('[data-guide]').attributes('data-guide-state')).toBe('unreachable');
    expect(fix(view)).toBe(en.dashboard.obs.guide.fix.unreachable);
    expect(done(view)).toEqual(['save']);
  });

  it('unreachable with no error text yet is the same state', () => {
    const view = show(base, true);

    expect(view.get('[data-guide]').attributes('data-guide-state')).toBe('unreachable');
    expect(done(view)).toEqual(['save']);
  });

  it('wrong password: OBS answered, so the server step is done and the fix names the password', () => {
    const view = show({ ...base, lastError: 'OBS closed the socket during Identify (wrong password?).' }, false);

    expect(view.get('[data-guide]').attributes('data-guide-state')).toBe('password');
    expect(fix(view)).toBe(en.dashboard.obs.guide.fix.password);
    expect(done(view)).toEqual(['server', 'save']);
  });

  it('no password saved while OBS asks for one: the password fix, not the port fix', () => {
    const view = show(
      {
        ...base,
        hasPassword: false,
        lastError: 'OBS requires a password but none is configured.',
      },
      false,
    );

    expect(view.get('[data-guide]').attributes('data-guide-state')).toBe('password');
    expect(done(view)).toEqual(['server', 'save']);
  });

  it('switched off: nothing is ticked and the fix says to link and switch it on', () => {
    const view = show({ ...base, isEnabled: false }, false);

    expect(view.get('[data-guide]').attributes('data-guide-state')).toBe('off');
    expect(fix(view)).toBe(en.dashboard.obs.guide.fix.off);
    expect(done(view)).toEqual([]);
  });

  it('connected: every step is ticked and the fix line says nothing is left to do', () => {
    const view = show({ ...base }, false);

    expect(view.get('[data-guide]').attributes('data-guide-state')).toBe('connected');
    expect(fix(view)).toBe(en.dashboard.obs.guide.fix.connected);
    expect(done(view)).toEqual(['server', 'password', 'save', 'connected']);
    expect(view.find('[data-guide-retest]').exists()).toBe(false);
  });

  it('Test again asks the page to re-run its reads, and is an outline button', async () => {
    const view = show({ ...base, lastError: 'Connection refused' }, true);
    const button = view.get('[data-guide-retest]');

    expect(button.text()).toBe(en.dashboard.obs.guide.retest);
    expect(button.classes().join(' ')).toContain('border');

    await button.trigger('click');

    expect(view.emitted('retest')).toHaveLength(1);
  });

  it('never prints the password or a stored secret', () => {
    const view = show({ ...base, lastError: 'OBS closed the socket during Identify (wrong password?).' }, false);

    expect(view.find('input').exists()).toBe(false);
  });
});

describe('ObsConnectionBar with the guide', () => {
  it('mounts the guide under the bar and forwards Test again as retry', async () => {
    mounted = mount(ObsConnectionBar, {
      global: { plugins: [i18n] },
      props: {
        connection: { ...base, lastError: 'Connection refused' },
        bridge: null,
        unreachable: true,
        panelState: 'ready',
      },
    });

    expect(mounted.find('[data-guide]').exists()).toBe(true);

    await mounted.get('[data-guide-retest]').trigger('click');

    expect(mounted.emitted('retry')).toHaveLength(1);
  });
});
