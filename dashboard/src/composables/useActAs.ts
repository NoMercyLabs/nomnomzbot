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

import { useLocalStorage } from '@vueuse/core';
import { computed, type ComputedRef } from 'vue';
import { useRouter } from 'vue-router';

import { useAdminBeginAccess, useAdminImpersonate } from '@/api/admin';
import { acceptAccessToken, exitActAs, isSignedIn, useActingAs } from '@/api/auth';
import { viewAs } from '@/api/viewAs';
import { chosenChannelId } from '@/composables/useCurrentChannel';

export type ActAsRequest = {
  broadcasterId: string;
  userId: string;
  justification: string;
  minutes: number;
};

export type ActAs = {
  pending: ComputedRef<boolean>;
  start: (request: ActAsRequest) => Promise<void>;
};

export type ActAsExit = {
  acting: ComputedRef<boolean>;
  exit: () => Promise<void>;
  returnToAdmin: () => Promise<void>;
};

// The page the admin left and the channel they managed. Exit, or the session running out, brings them back.
// They live in storage because the act-as session outlives a reload.
const leftPage = useLocalStorage<string>('nnz.actAs.return', '');
const leftChannel = useLocalStorage<string>('nnz.actAs.channel', '');

// Act as one person of a channel: open a time-boxed support session for the channel and act as the person in
// it. The Act as dialog starts it; useActAsExit below is the shell's way out.
export function useActAs() {
  const router = useRouter();
  const begin = useAdminBeginAccess();
  const impersonate = useAdminImpersonate();

  async function start({
    broadcasterId,
    userId,
    justification,
    minutes,
  }: ActAsRequest): Promise<void> {
    const expiresAt = new Date(Date.now() + minutes * 60_000).toISOString();
    const grant = await begin.mutateAsync({
      broadcasterId,
      justification,
      breakGlass: false,
      expiresAt,
    });
    const minted = await impersonate.mutateAsync({
      userId,
      accessGrantId: grant.id,
      justification,
    });

    leftPage.value = router.currentRoute.value.fullPath;
    leftChannel.value = chosenChannelId.value;
    chosenChannelId.value = broadcasterId;
    // A preview of a smaller role belongs to the admin; the target sees their own dashboard.
    viewAs.value = '';
    acceptAccessToken(minted.accessToken);
    await router.push({ name: 'dashboard-home' });
  }

  return {
    pending: computed(() => begin.isPending.value || impersonate.isPending.value),
    start,
  } as const satisfies ActAs;
}

// The shell's side of acting as someone: whether it is on, the Exit, and the way back afterwards.
export function useActAsExit() {
  const router = useRouter();

  // The admin is themselves again: back to the channel and the page they left.
  async function returnToAdmin(): Promise<void> {
    const back = leftPage.value;

    chosenChannelId.value = leftChannel.value;
    leftPage.value = '';
    leftChannel.value = '';

    if (back === '' || !isSignedIn()) return;

    await router.push(back);
  }

  return {
    acting: useActingAs(),
    exit: exitActAs,
    returnToAdmin,
  } as const satisfies ActAsExit;
}
