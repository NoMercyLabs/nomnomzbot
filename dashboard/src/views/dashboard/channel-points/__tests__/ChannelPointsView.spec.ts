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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { chooseTarget, openTargetPicker } from '@/components/layout/__tests__/targetPickerDom';
import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';

import ChannelPointsView from '../ChannelPointsView.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
};

const calls: Call[] = [];
let held: string[] = [];

const ok = (data: unknown): { data: { data: unknown }; response: { status: number } } => ({
  data: { data },
  response: { status: 200 },
});

const baseReward = {
  id: 'r1',
  title: 'Hydrate',
  cost: 500,
  isEnabled: true,
  isManageable: true,
  isPaused: false,
  isOnTwitch: true,
  pipelineId: 'p1',
  response: null,
};

let reward: Record<string, unknown> = { ...baseReward };
let channelPointsAvailable = true;

const redemption = (id: string, status: string) => ({
  redemptionId: id,
  rewardId: 'r1',
  rewardTitle: 'Hydrate',
  userId: 'u1',
  userDisplayName: 'DukaSoft',
  cost: 500,
  status,
  redeemedAt: '2026-10-15T12:00:00Z',
}) as const satisfies Record<string, unknown>;

function read(path: string): unknown {
  if (path.endsWith('/roles/effective/me'))
    return ok({ heldActionKeys: held });

  if (path.endsWith('/blast-radius')) {
    return ok({
      categories: [
        {
          categoryKey: 'blast_radius_category_redemptions',
          count: 3,
          sample: ['DukaSoft', 'Bamo'],
        },
      ],
      isMinimum: false,
    });
  }

  if (path.endsWith('/rewards/availability')) {
    return ok({
      channelPointsAvailable,
      broadcasterType: channelPointsAvailable
        ? 'affiliate'
        : '',
    });
  }

  if (path.endsWith('/rewards/redemptions'))
    return ok([redemption('d1', 'unfulfilled'), redemption('d2', 'fulfilled')]);

  if (path.endsWith('/rewards'))
    return ok([reward]);

  if (path.endsWith('/commands'))
    return ok([{ id: 'c1', name: 'hello' }]);

  if (path.endsWith('/pipelines'))
    return { data: { data: [{ id: 'p1', name: 'Drink water' }], hasMore: false }, response: { status: 200 } };

  return ok(null);
}

function write(method: string, path: string, options?: Options): unknown {
  calls.push({
    method,
    path,
    body: options?.body,
  });

  if (method === 'PATCH') {
    reward = { ...reward, ...(options?.body as Record<string, unknown>) };
  }

  if (method === 'POST' && path.endsWith('/rewards'))
    return ok({ id: 'r2' });

  return { response: { status: 200 } };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => read(path),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    PATCH: async (path: string, options?: Options) => write('PATCH', path, options),
    DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

async function open(): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: ChannelPointsView }],
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  await router.push('/');

  const framed = defineComponent(() => () => h(TooltipProvider, () =>
    h(ChannelPointsView)));

  mounted = mount(framed, {
    global: {
      plugins: [i18n, router, [VueQueryPlugin, { queryClient }]],
    },
  });

  await flushPromises();

  return mounted;
}

beforeEach(() => {
  calls.length = 0;
  reward = { ...baseReward };
  channelPointsAvailable = true;
  held = ['reward:read', 'reward:manage'];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal('ResizeObserver', class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('ChannelPointsView', () => {
  describe('a channel that cannot have channel points', () => {
    beforeEach(() => {
      channelPointsAvailable = false;

      reward = {
        ...baseReward,
        isOnTwitch: false,
        isPaused: true,
      };
    });

    it('says why at the top and turns the pending panel off', async () => {
      const view = await open();
      const notice = view.get('[data-channel-points-unavailable]').text();

      expect(notice).toBe(en.dashboard.channelPoints.unavailable);
      expect(view.find('[data-redemption-item]').exists()).toBe(false);
      expect(view.text()).not.toContain(en.dashboard.channelPoints.redemptions.title);
    });

    it('shows a reward with no Twitch id as Not on Twitch, not Paused, with its switch off', async () => {
      const view = await open();
      const row = view.get('[data-reward-item]').element.parentElement!;

      expect(row.textContent).toContain('Not on Twitch');
      expect(row.textContent).not.toContain('Paused');
      expect(row.querySelector('[data-reward-enabled]')?.hasAttribute('disabled')).toBe(true);
    });
  });

  it('shows no notice and keeps the pending panel for an affiliate channel', async () => {
    const view = await open();

    expect(view.find('[data-channel-points-unavailable]').exists()).toBe(false);
    expect(view.findAll('[data-redemption-item]')).toHaveLength(1);
  });

  it('shows each reward with its cost, state and the pipeline it runs', async () => {
    const view = await open();
    const row = view.get('[data-reward-item]').element.parentElement!;

    expect(row.textContent).toContain('Hydrate');
    expect(row.textContent).toContain('Drink water');
    expect(row.textContent).toContain('500');
    expect(row.querySelector('[data-reward-enabled]')?.getAttribute('aria-checked')).toBe('true');
  });

  it('turns a reward off with the switch and the row shows it', async () => {
    const view = await open();

    await view.get('[data-reward-enabled]').trigger('click');
    await flushPromises();

    const patch = calls.find((call) => call.method === 'PATCH');

    expect(patch?.path).toContain('/rewards/{rewardId}');
    expect(patch?.body).toEqual({ isEnabled: false });
    expect(view.get('[data-reward-enabled]').attributes('aria-checked')).toBe('false');
  });

  it('keeps one Edit button per row and no inline cost field', async () => {
    const view = await open();

    expect(view.findAll('[data-reward-edit]')).toHaveLength(1);
    expect(view.find('[data-reward-cost]').exists()).toBe(false);
  });

  it('saves every field of the reward from the edit dialog', async () => {
    const view = await open();

    await view.get('[data-reward-edit]').trigger('click');
    await flushPromises();

    const field = (name: string): HTMLInputElement =>
      document.body.querySelector(`[data-field="${name}"]`) as HTMLInputElement;

    const type = async (name: string, value: string): Promise<void> => {
      field(name).value = value;
      field(name).dispatchEvent(new Event('input', { bubbles: true }));
      await flushPromises();
    };

    await type('title', 'Stay hydrated');
    await type('prompt', 'Drink up');
    await type('cost', '750');
    await type('backgroundColor', '#112233');
    (document.body.querySelector('[data-limits-trigger]') as HTMLButtonElement).click();
    await flushPromises();
    await type('maxPerStream', '5');
    await type('maxPerUserPerStream', '2');
    await type('globalCooldownSeconds', '60');
    await type('timerDurationSeconds', '30');
    (document.body.querySelector('[data-reward-dialog-enabled]') as HTMLButtonElement).click();
    field('isPaused').click();
    field('isUserInputRequired').click();
    await flushPromises();

    (document.body.querySelector('[data-form-action]') as HTMLButtonElement).click();
    await flushPromises();

    const patch = calls.find((call) => call.method === 'PATCH');

    expect(patch?.body).toEqual({
      title: 'Stay hydrated',
      prompt: 'Drink up',
      response: '',
      cost: 750,
      isEnabled: false,
      isPaused: true,
      isUserInputRequired: true,
      backgroundColor: '#112233',
      maxPerStream: 5,
      maxPerUserPerStream: 2,
      globalCooldownSeconds: 60,
      timerDurationSeconds: 30,
      pipelineId: 'p1',
    });
  });

  // Stoney 2026-10-23: every user buildable gets the same tooling - create, duplicate, delete.
  describe('the tools about the reward itself', () => {
    const press = async (selector: string): Promise<void> => {
      (document.body.querySelector(selector) as HTMLButtonElement).click();
      await flushPromises();
    };

    it('deletes only after the counted list is shown and the owner confirms', async () => {
      const view = await open();

      await view.get('[data-reward-edit]').trigger('click');
      await flushPromises();
      await press('[data-reward-delete]');

      expect(document.body.textContent).toContain('3 redemptions: DukaSoft, Bamo');
      expect(calls.some((call) => call.method === 'DELETE')).toBe(false);

      const confirm = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
        (button) => button.textContent?.trim() === 'Delete reward',
      );

      confirm!.click();
      await flushPromises();

      expect(calls.filter((call) => call.method === 'DELETE').map((call) => call.path)).toEqual([
        '/api/v{version}/channels/{channelId}/rewards/{rewardId}',
      ]);

      expect(document.body.querySelector('[data-reward-dialog]')).toBeNull();
    });

    it('duplicates the reward with its settings, a copy name, and switched off', async () => {
      reward = {
        ...baseReward,
        prompt: 'Drink up',
        maxPerStream: 5,
      };

      const view = await open();

      await view.get('[data-reward-edit]').trigger('click');
      await flushPromises();
      await press('[data-reward-duplicate]');

      const created = calls.find((call) => call.method === 'POST');
      const switchedOff = calls.find((call) => call.method === 'PATCH');

      expect(created?.body).toMatchObject({
        title: 'Hydrate copy',
        cost: 500,
        prompt: 'Drink up',
        maxPerStream: 5,
        pipelineId: 'p1',
      });

      expect(switchedOff?.path).toContain('/rewards/{rewardId}');
      expect(switchedOff?.body).toEqual({ isEnabled: false });
      expect(document.body.querySelector('[data-reward-dialog]')).toBeNull();
    });
  });

  it('saves the color picked with the color picker', async () => {
    const view = await open();

    await view.get('[data-reward-edit]').trigger('click');
    await flushPromises();

    const swatch = document.body.querySelector('[data-color-swatch]') as HTMLInputElement;

    swatch.value = '#aa00ff';
    swatch.dispatchEvent(new Event('input', { bubbles: true }));
    await flushPromises();

    const text = document.body.querySelector('[data-field="backgroundColor"]') as HTMLInputElement;

    expect(text.value).toBe('#AA00FF');

    (document.body.querySelector('[data-form-action]') as HTMLButtonElement).click();
    await flushPromises();

    const patch = calls.find((call) => call.method === 'PATCH');

    expect(patch?.body).toMatchObject({ backgroundColor: '#AA00FF' });
  });

  it('disables the dialog fields of a reward the bot cannot manage', async () => {
    reward = { ...reward, isManageable: false };

    const view = await open();

    await view.get('[data-reward-edit]').trigger('click');
    await flushPromises();

    expect((document.body.querySelector('[data-field="title"]') as HTMLInputElement).disabled).toBe(true);
    const segments = [...document.body.querySelectorAll<HTMLButtonElement>('[data-reply-mode] [data-segment]')];

    expect(segments.length).toBe(2);
    expect(segments.every((segment) => segment.disabled)).toBe(true);
    const reason = en.dashboard.channelPoints.rewards.dialog.unmanageable;

    expect(document.body.textContent).toContain(reason);
  });

  describe('edit dialog layout', () => {
    const pick = (selector: string): HTMLElement =>
      document.body.querySelector(selector) as HTMLElement;
    const has = (selector: string): boolean => document.body.querySelector(selector) !== null;

    async function openDialog(): Promise<void> {
      const view = await open();

      await view.get('[data-reward-edit]').trigger('click');
      await flushPromises();
    }

    async function chooseMode(mode: string): Promise<void> {
      pick(`[data-reply-mode] [data-segment][value="${mode}"]`).click();
      await flushPromises();
    }

    async function save(): Promise<Call | undefined> {
      pick('[data-form-action]').click();
      await flushPromises();

      return calls.find((call) => call.method === 'PATCH');
    }

    it('saves a command as the thing a redemption runs, as every other reply surface can', async () => {
      Element.prototype.hasPointerCapture = (): boolean => false;
      Element.prototype.releasePointerCapture = (): void => undefined;
      Element.prototype.scrollIntoView = (): void => undefined;

      await openDialog();
      await chooseMode('full');
      await openTargetPicker(pick('[data-reply-pipeline]'));
      await chooseTarget('!hello');
      await flushPromises();

      const body = (await save())?.body as Record<string, unknown>;

      expect(body.target).toEqual({ kind: 0, id: 'c1' });
      expect(body.response).toBe('');
    });

    it('counts each text field against its Twitch limit and stops typing at the limit', async () => {
      await openDialog();

      const title = pick('#reward-title') as HTMLInputElement;
      const titleCount = (): string =>
        title.closest('[data-slot="field"]')?.querySelector('[data-slot="field-count"]')?.textContent ?? '';

      expect(titleCount()).toBe(`${title.value.length}/45`);
      expect(title.maxLength).toBe(45);
      expect((pick('#reward-prompt') as HTMLTextAreaElement).maxLength).toBe(200);

      title.value = 'x'.repeat(45);
      title.dispatchEvent(new Event('input'));
      await flushPromises();

      expect(titleCount()).toBe('45/45');
      expect(title.closest('[data-slot="field"]')?.querySelector('[data-slot="field-count"]')?.classList.contains('text-destructive')).toBe(true);
    });

    it('leaves the color out of the save when the field is empty, because Twitch refuses an empty color', async () => {
      await openDialog();

      const color = pick('#reward-backgroundColor') as HTMLInputElement;

      color.value = '';
      color.dispatchEvent(new Event('input'));
      await flushPromises();

      const body = (await save())?.body as Record<string, string | number | boolean | null>;

      expect(body).toBeDefined();
      expect(body.backgroundColor).toBeUndefined();
    });

    it('toggles enabled with the switch in the header', async () => {
      await openDialog();

      const toggle = pick('[data-form-header] [data-reward-dialog-enabled]');

      expect(toggle.getAttribute('aria-checked')).toBe('true');
      expect(has('[data-field="isEnabled"]')).toBe(false);

      toggle.click();
      await flushPromises();

      expect(toggle.getAttribute('aria-checked')).toBe('false');

      const patch = await save();

      expect(patch?.body).toMatchObject({ isEnabled: false });
    });

    it('shows the pipeline picker for a bound reward and hides the chat reply', async () => {
      await openDialog();

      expect(has('[data-reply-pipeline]')).toBe(true);
      expect(has('[data-field="response"]')).toBe(false);
    });

    it('simple reply mode hides the pipeline, sends the reply and clears the pipeline', async () => {
      await openDialog();
      await chooseMode('simple');

      expect(has('[data-reply-pipeline]')).toBe(false);

      const reply = pick('[data-field="response"]') as HTMLTextAreaElement;

      reply.value = '{{user}} drank';
      reply.dispatchEvent(new Event('input', { bubbles: true }));
      await flushPromises();

      const patch = await save();

      expect(patch?.body).toMatchObject({
        response: '{{user}} drank',
        pipelineId: '00000000-0000-0000-0000-000000000000',
      });
    });

    it('pipeline mode hides the chat reply and sends the pipeline with an empty reply', async () => {
      reward = {
        ...reward,
        pipelineId: null,
        response: 'old reply',
      };

      await openDialog();

      expect((pick('[data-field="response"]') as HTMLTextAreaElement).value).toBe('old reply');

      await chooseMode('full');

      expect(has('[data-field="response"]')).toBe(false);
      expect(has('[data-reply-pipeline]')).toBe(true);
    });

    it('keeps the limits closed until opened and summarizes the active ones', async () => {
      reward = {
        ...reward,
        maxPerStream: 5,
        globalCooldownSeconds: 60,
      };

      await openDialog();

      expect(has('[data-field="maxPerStream"]')).toBe(false);
      expect(pick('[data-limits-trigger]').textContent).toContain('5');
      expect(pick('[data-limits-trigger]').textContent).toContain('60');

      pick('[data-limits-trigger]').click();
      await flushPromises();

      expect(has('[data-field="maxPerStream"]')).toBe(true);
      expect(has('[data-form-header] [data-field="isPaused"]')).toBe(true);
      expect(document.body.querySelectorAll('[data-field="isPaused"]')).toHaveLength(1);
    });

    it('shows a chevron on the limits trigger that rotates when opened', async () => {
      await openDialog();

      const trigger = pick('[data-limits-trigger]');
      const chevron = trigger.querySelector('[data-limits-chevron]') as Element;

      expect(chevron).not.toBeNull();
      // A tag with no import renders as an empty unknown element; the icon must be a drawn svg.
      expect(chevron.tagName.toLowerCase()).toBe('svg');
      expect(trigger.getAttribute('data-state')).toBe('closed');
      expect(chevron.getAttribute('class')).toContain('group-data-[state=open]:rotate-180');

      trigger.click();
      await flushPromises();

      expect(trigger.getAttribute('data-state')).toBe('open');
      expect(trigger.getAttribute('aria-expanded')).toBe('true');
    });

    it('labels the Enabled and Paused switches in the header and saves Paused', async () => {
      await openDialog();

      const header = pick('[data-form-header]');

      expect(header.textContent).toContain('Enabled');
      expect(header.textContent).toContain('Paused');

      const paused = pick('[data-form-header] [data-reward-dialog-paused]');

      expect(paused.getAttribute('aria-checked')).toBe('false');

      paused.click();
      await flushPromises();

      expect(paused.getAttribute('aria-checked')).toBe('true');
      expect((await save())?.body).toMatchObject({ isPaused: true });
    });

    it('labels the prompt field Description, apart from the reply setting', async () => {
      await openDialog();

      const label = pick('label[for="reward-prompt"]');

      expect(label.textContent).toBe('Description');
      expect(has('[data-reply-mode] [data-field="prompt"]')).toBe(false);
    });

    it('paints the swatch with the current color value', async () => {
      reward = { ...reward, backgroundColor: '#9146FF' };
      await openDialog();

      const chip = pick('[data-color-chip]');

      expect(chip.style.backgroundColor).toBe('rgb(145, 70, 255)');
    });

    it('draws the color swatch and hex field as one input group', async () => {
      await openDialog();

      const group = pick('[data-color-picker]');

      expect(group.querySelector('[data-color-swatch]')).not.toBeNull();
      expect(group.querySelector('[data-field="backgroundColor"]')).not.toBeNull();
      expect(group.getAttribute('data-slot')).toBe('input-group');
    });
  });

  it('lists only the waiting redemption and completes it', async () => {
    const view = await open();
    const rows = view.findAll('[data-redemption-item]');

    expect(rows).toHaveLength(1);
    expect(rows[0]!.text()).toContain('DukaSoft');

    await rows[0]!.get('[data-redemption-complete]').trigger('click');
    await flushPromises();

    const post = calls.find((call) => call.method === 'POST');

    expect(post?.path).toContain('/redemptions/{redemptionId}/fulfill');
  });

  it('hides the redemption actions from a role without reward:manage', async () => {
    held = ['reward:read'];

    const view = await open();

    expect(view.find('[data-redemption-complete]').exists()).toBe(false);
    expect(view.find('[data-redemption-reject]').exists()).toBe(false);
    expect(view.find('[data-reward-edit]').exists()).toBe(false);
    expect(view.find('[data-reward-enabled]').exists()).toBe(false);
    expect(view.find('[data-reward-item]').exists()).toBe(true);
  });

  it('shows the reward edit button and enabled switch with reward:manage', async () => {
    const view = await open();

    expect(view.find('[data-reward-edit]').exists()).toBe(true);
    expect(view.find('[data-reward-enabled]').exists()).toBe(true);
  });
});
