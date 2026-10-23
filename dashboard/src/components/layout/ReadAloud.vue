<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Read the page aloud in the bot's own voice (useReadAloud). A header button starts it; a floating bar
// then holds play or pause, the previous and next paragraph and section, the speed and stop. Escape stops it,
// and leaving the page stops it, so the voice never outlives the page it reads. When the server voice cannot
// speak, the reading stops and a line says so. Put it on a page that has a <main> element: that is what is read.
import { onBeforeUnmount, onMounted } from 'vue';
import { useI18n } from 'vue-i18n';
import ChevronLeftDouble from '~icons/nnz/chevron-left-double';
import ChevronRightDouble from '~icons/nnz/chevron-right-double';
import Next from '~icons/nnz/next';
import Pause from '~icons/nnz/pause';
import Play from '~icons/nnz/play';
import Previous from '~icons/nnz/previous';
import Speaker from '~icons/nnz/speaker';
import Stop from '~icons/nnz/stop';

import { Button } from '@/components/ui/button';
import { useReadAloud } from '@/composables/useReadAloud';

const { t, locale } = useI18n();
const { status, rate, failed, supported, start, toggle, stop, step, cycleRate } = useReadAloud();

function begin(): void {
  const main: HTMLElement | null = document.querySelector('main');

  if (main) {
    start(main, locale.value);
  }
}

function onKey(event: KeyboardEvent): void {
  if (event.key !== 'Escape' || status.value === 'idle') return;

  const active = document.activeElement;

  if (active === null || active === document.body || active.closest('[data-read-bar]')) {
    stop();
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKey);
  window.addEventListener('pagehide', stop);
});

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKey);
  window.removeEventListener('pagehide', stop);
  stop();
});
</script>

<template>
  <p v-if="failed" data-read-failed role="alert" class="text-sm text-destructive">
    {{ t('common.readAloud.unavailable') }}
  </p>

  <Button v-if="supported && status === 'idle'"
          data-read-aloud-start
          type="button"
          variant="outline"
          @click="begin"
  >
    <Speaker class="size-5" />
    {{ t('common.readAloud.start') }}
  </Button>

  <div v-if="status !== 'idle'"
       data-read-bar
       role="group"
       :aria-label="t('common.readAloud.controls')"
       class="fixed inset-x-0 bottom-4 z-50 mx-auto flex w-fit max-w-[calc(100vw-1rem)] flex-wrap items-center justify-center gap-1 rounded-full border border-border bg-popover p-1 shadow-lg"
  >
    <Button variant="ghost"
            size="icon"
            type="button"
            :aria-label="t('common.readAloud.prevSection')"
            :title="t('common.readAloud.prevSection')"
            @click="step(-1, 'section')"
    >
      <ChevronLeftDouble class="size-5" />
    </Button>
    <Button variant="ghost"
            size="icon"
            type="button"
            :aria-label="t('common.readAloud.prevParagraph')"
            :title="t('common.readAloud.prevParagraph')"
            @click="step(-1, 'paragraph')"
    >
      <Previous class="size-5" />
    </Button>
    <Button size="icon"
            type="button"
            data-read-toggle
            :aria-label="status === 'playing' ? t('common.readAloud.pause') : t('common.readAloud.play')"
            :title="status === 'playing' ? t('common.readAloud.pause') : t('common.readAloud.play')"
            @click="toggle"
    >
      <Pause v-if="status === 'playing'" class="size-5" />
      <Play v-else class="size-5" />
    </Button>
    <Button variant="ghost"
            size="icon"
            type="button"
            :aria-label="t('common.readAloud.nextParagraph')"
            :title="t('common.readAloud.nextParagraph')"
            @click="step(1, 'paragraph')"
    >
      <Next class="size-5" />
    </Button>
    <Button variant="ghost"
            size="icon"
            type="button"
            :aria-label="t('common.readAloud.nextSection')"
            :title="t('common.readAloud.nextSection')"
            @click="step(1, 'section')"
    >
      <ChevronRightDouble class="size-5" />
    </Button>
    <Button variant="ghost"
            type="button"
            data-read-rate
            :aria-label="t('common.readAloud.speed', { rate: `${rate}×` })"
            :title="t('common.readAloud.speed', { rate: `${rate}×` })"
            @click="cycleRate"
    >
      <span class="tabular-nums">{{ `${rate}×` }}</span>
    </Button>
    <Button variant="ghost"
            size="icon"
            type="button"
            data-read-stop
            :aria-label="t('common.readAloud.stop')"
            :title="t('common.readAloud.stop')"
            @click="stop"
    >
      <Stop class="size-5" />
    </Button>
  </div>
</template>
