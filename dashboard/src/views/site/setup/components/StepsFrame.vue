<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Where the person is in the wizard: every step the bot lists plus Finish, the current one marked, the finished
// ones checked. Entries are plain text, not buttons: a required step cannot be jumped over. Below 640 px the
// row collapses to "Step N of M" and the current title.
import { computed } from 'vue';
import Check from '~icons/nnz/check';

import { merge } from '@/lib/array';
import { cn } from '@/lib/utils';

type FrameStep = {
  key: string;
  title: string;
  complete: boolean;
};

const props = defineProps<{
  steps: FrameStep[];
  finishTitle: string;
  current: number;
}>();

type EntryState = 'done' | 'current' | 'todo';

function stateOf(complete: boolean, index: number): EntryState {
  if (index === props.current)
    return 'current';

  return complete
    ? 'done'
    : 'todo';
}

const finish = computed<FrameStep[]>(() => [
  {
    key: 'finish',
    title: props.finishTitle,
    complete: false,
  },
]);
const all = computed(() => merge(props.steps, finish.value));
const entries = computed(() =>
  all.value.map((entry, index) => ({
    ...entry,
    number: index + 1,
    state: stateOf(entry.complete, index),
  })),
);

const currentTitle = computed(() => entries.value[props.current]?.title ?? '');
</script>

<template>
  <nav :aria-label="$t('site.setup.stepsLabel')" class="w-full max-w-3xl">
    <p data-steps-compact class="flex flex-col items-center gap-0.5 text-center sm:hidden">
      <span class="text-xs text-muted-foreground">
        {{ $t('site.setup.progress', { current: current + 1, total: entries.length }) }}
      </span>
      <span class="text-sm font-medium">{{ currentTitle }}</span>
    </p>

    <ol class="hidden items-start gap-3 sm:flex">
      <li
        v-for="entry in entries"
        :key="entry.key"
        :data-state="entry.state"
        :aria-current="entry.state === 'current' ? 'step' : undefined"
        class="flex min-w-0 flex-1 items-center gap-2"
      >
        <span
          data-step-number
          :class="cn(
            'flex size-6 shrink-0 items-center justify-center rounded-full border text-xs',
            entry.state === 'current' && 'border-foreground bg-foreground text-background',
            entry.state === 'done' && 'bg-muted text-foreground',
            entry.state === 'todo' && 'text-muted-foreground',
          )"
        >{{ entry.number }}</span>
        <span
          :class="cn(
            'truncate text-xs',
            entry.state === 'current' ? 'font-medium text-foreground' : 'text-muted-foreground',
          )"
          :title="entry.title"
        >{{ entry.title }}</span>
        <template v-if="entry.state === 'done'">
          <Check data-step-done class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <span class="sr-only">{{ $t('site.setup.done') }}</span>
        </template>
      </li>
    </ol>
  </nav>
</template>
