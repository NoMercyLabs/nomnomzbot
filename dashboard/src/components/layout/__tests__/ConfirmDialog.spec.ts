// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';

import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { i18n } from '@/i18n';

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function dialog(extra: Record<string, unknown> = {}): Promise<VueWrapper> {
  mounted = mount(ConfirmDialog, {
    props: {
      open: true,
      title: 'Delete !so?',
      description: 'This command ran 333 times.',
      action: 'Delete command',
      consequences: ['Chat stops answering !so.', 'The history stays.'],
      ...extra,
    },
    attachTo: document.body,
    global: { plugins: [i18n] },
  });

  await flushPromises();

  return mounted;
}

function actionButton(): HTMLButtonElement {
  return document.body.querySelector('[data-confirm-action]') as HTMLButtonElement;
}

describe('the confirm dialog', () => {
  it('shows the title, the description and every consequence in the blast box', async () => {
    await dialog();

    expect(document.body.querySelector('[role="alertdialog"]')?.textContent).toContain(
      'Delete !so?',
    );

    expect(document.body.textContent).toContain('This command ran 333 times.');
    const items: string[] = Array.from(
      document.body.querySelectorAll('[data-slot="blast-list"] li'),
    ).map((li: Element) => li.textContent ?? '');

    expect(items).toEqual(['Chat stops answering !so.', 'The history stays.']);

    expect(document.body.querySelector('[data-slot="blast-list"]')?.textContent).toContain(
      'What happens',
    );
  });

  it('leaves the blast box out when there are no consequences', async () => {
    await dialog({ consequences: [] });

    expect(document.body.querySelector('[role="alertdialog"]')).not.toBeNull();
    expect(document.body.querySelector('[data-slot="blast-list"]')).toBeNull();
  });

  it('names the action on the red button and puts Cancel before it', async () => {
    await dialog();

    expect(actionButton().textContent).toContain('Delete command');
    const buttons: HTMLButtonElement[] = Array.from(
      document.body.querySelectorAll('[data-slot="alert-dialog-footer"] button'),
    );

    expect(buttons.map((b: HTMLButtonElement) => b.textContent?.trim())).toEqual([
      'Cancel',
      'Delete command',
    ]);
  });

  it('emits confirm once when the action is pressed', async () => {
    const wrapper = await dialog();

    actionButton().click();
    await flushPromises();

    expect(wrapper.emitted('confirm')).toHaveLength(1);
  });

  it('closes without confirm on Cancel', async () => {
    const wrapper = await dialog();

    const cancel = document.body.querySelector(
      '[data-slot="alert-dialog-cancel"]',
    ) as HTMLButtonElement;

    cancel.click();
    await flushPromises();

    expect(wrapper.emitted('confirm')).toBeUndefined();
    expect(wrapper.emitted('update:open')?.at(-1)).toEqual([false]);
  });

  it('closes without confirm on Escape', async () => {
    const wrapper = await dialog();

    document.body
      .querySelector('[role="alertdialog"]')
      ?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    await flushPromises();

    expect(wrapper.emitted('confirm')).toBeUndefined();
    expect(wrapper.emitted('update:open')?.at(-1)).toEqual([false]);
  });

  it('disables the action and shows a spinner while pending, so a second press cannot fire', async () => {
    const wrapper: VueWrapper = await dialog({ pending: true });

    expect(actionButton().disabled).toBe(true);
    expect(actionButton().querySelector('[role="status"]')).not.toBeNull();
    actionButton().click();
    await flushPromises();

    expect(wrapper.emitted('confirm')).toBeUndefined();
  });

  it('shows no spinner when not pending', async () => {
    await dialog();

    expect(actionButton().disabled).toBe(false);
    expect(actionButton().querySelector('[role="status"]')).toBeNull();
  });
});
