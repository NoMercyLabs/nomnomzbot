// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useDebounceFn } from '@vueuse/core';
import { computed, ref, type ComputedRef, type Ref } from 'vue';

import type { PlatformDefaultBlastRadius } from '@/api/adminPlatformDefaults';

const TYPING_PAUSE_MS = 400;

export type PlatformDefaultPreviewState = {
  preview: Ref<PlatformDefaultBlastRadius | undefined>;
  pending: Ref<boolean>;
  failed: Ref<boolean>;
  dangerConfirmed: Ref<boolean>;
  channelsAffected: ComputedRef<number>;
  canApply: ComputedRef<boolean>;
  request: () => Promise<void>;
  schedule: () => void;
  reset: () => void;
};

// The preview of one pending platform-default change. Every edit asks for a fresh count of the channels it
// would change; a slow answer for an older draft is dropped (sequence guard), so Apply can only ever confirm
// the count of the draft on screen. The server refuses a count that moved, and the page shows that refusal.
export function usePlatformDefaultPreview(
  fetchPreview: () => Promise<PlatformDefaultBlastRadius>,
): PlatformDefaultPreviewState {
  const preview = ref<PlatformDefaultBlastRadius>();
  const pending = ref(false);
  const failed = ref(false);
  const dangerConfirmed = ref(false);

  let sequence = 0;

  const channelsAffected = computed<number>(() => Number(preview.value?.channelsAffected ?? 0));

  const canApply = computed<boolean>(
    () =>
      preview.value !== undefined &&
      !pending.value &&
      (!preview.value.requiresDangerConfirmation || dangerConfirmed.value),
  );

  function reset(): void {
    sequence += 1;
    preview.value = undefined;
    pending.value = false;
    failed.value = false;
    dangerConfirmed.value = false;
  }

  async function request(): Promise<void> {
    reset();

    const mine = sequence;

    pending.value = true;

    try {
      const answer = await fetchPreview();

      if (mine === sequence) {
        preview.value = answer;
      }
    } catch {
      if (mine === sequence) {
        failed.value = true;
      }
    } finally {
      if (mine === sequence) {
        pending.value = false;
      }
    }
  }

  // A close or a newer edit moves the sequence on, so a pause that ends after it asks nothing.
  const requestIfCurrent = useDebounceFn(async (scheduled: number): Promise<void> => {
    if (scheduled === sequence) {
      await request();
    }
  }, TYPING_PAUSE_MS);

  // A draft that is still being typed: the shown count is dropped at once, the new one is asked after a pause.
  function schedule(): void {
    reset();
    void requestIfCurrent(sequence);
  }

  return {
    schedule,
    preview,
    pending,
    failed,
    dangerConfirmed,
    channelsAffected,
    canApply,
    request,
    reset,
  };
}
