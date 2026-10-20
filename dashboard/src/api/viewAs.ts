// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useLocalStorage } from '@vueuse/core';
import { hasInjectionContext, type Ref } from 'vue';
import { useRouter } from 'vue-router';

import { recheckOpenPage } from '@/router/recheckPage';

import { queryClient } from './queryClient';

export type ViewAsRole = 'moderator' | 'viewer';

export const viewAsHeader = 'X-View-As';

// The role the owner looks through. Empty means no preview. The bot caps every answer at this role.
export const viewAs: Ref<ViewAsRole | ''> = useLocalStorage<ViewAsRole | ''>('nnz.viewAs', '');

// The page the owner left to look as a viewer. Exit goes back to it. Empty means the preview stayed on its page.
const viewAsReturn: Ref<string> = useLocalStorage<string>('nnz.viewAs.return', '');

// The viewer shell of the owner's own channel, where a viewer preview opens.
export type ViewAsShell = { login: string };

export type ViewAsControl = {
  role: Ref<ViewAsRole | ''>;
  start: (role: ViewAsRole, shell?: ViewAsShell) => Promise<void>;
  exit: () => Promise<void>;
};

// Every answer on screen depends on the role, so a change drops them all and the open pages ask again.
// Then the open page is checked again for the new role: a page it may not open gives way to one it may.
// A viewer preview with a shell opens that channel's viewer pages instead, and Exit returns to the page left.
export function useViewAs() {
  const router = hasInjectionContext()
    ? useRouter()
    : undefined;

  async function change(role: ViewAsRole | ''): Promise<void> {
    viewAs.value = role;
    await queryClient.invalidateQueries();

    if (router !== undefined) {
      await recheckOpenPage(router);
    }
  }

  async function start(role: ViewAsRole, shell?: ViewAsShell): Promise<void> {
    if (router === undefined || shell === undefined || role !== 'viewer') {
      await change(role);

      return;
    }

    viewAsReturn.value = router.currentRoute.value.fullPath;
    viewAs.value = role;
    await queryClient.invalidateQueries();
    await router.push({ name: 'viewer-me', params: { channel: shell.login } });
  }

  async function exit(): Promise<void> {
    const back = viewAsReturn.value;

    if (router === undefined || back === '') {
      await change('');

      return;
    }

    viewAsReturn.value = '';
    viewAs.value = '';
    await queryClient.invalidateQueries();
    await router.push(back);
  }

  return {
    role: viewAs,
    start,
    exit,
  } as const satisfies ViewAsControl;
}
