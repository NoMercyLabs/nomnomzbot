<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one skin tone for every emoji in the picker, chosen from a column of hands; 0 is yellow. The picker puts it
// beside the search on a desktop and in the jump rail row on a phone, which has no search.
import { ref } from 'vue';
import ChevronDown from '~icons/nnz/chevron-down';

import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

import { skinTone, toneModifiers } from '../emotePicker';

const tone = defineModel<number>({ required: true });

const hands = [0, 1, 2, 3, 4, 5].map((index) =>
  skinTone({ insert: '✋', skins: toneModifiers.map((modifier) =>
    `✋${modifier}`) }, index));

const menu = ref(false);

function choose(index: number): void {
  tone.value = index;
  menu.value = false;
}
</script>

<template>
  <Popover v-model:open="menu">
    <PopoverTrigger as-child>
      <Button
        data-skin-tone-button
        type="button"
        variant="ghost"
        size="sm"
        class="group gap-0.5! rounded-[calc(var(--radius-md)-8px)] px-1.5!"
        :aria-label="$t('dashboard.chat.picker.toneForAll')"
      >
        <span class="text-xl leading-none" aria-hidden="true">{{ hands[tone] }}</span>
        <ChevronDown
          class="size-4 text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
          aria-hidden="true"
        />
      </Button>
    </PopoverTrigger>
    <PopoverContent
      data-tone-column
      side="top"
      align="end"
      class="flex w-auto flex-col gap-0.5 p-1"
      :aria-label="$t('dashboard.chat.picker.toneForAll')"
    >
      <button
        v-for="(hand, index) in hands"
        :key="hand"
        type="button"
        :data-tone-choice="index"
        :aria-label="$t(`dashboard.chat.picker.toneNames.${index}`)"
        :aria-pressed="index === tone"
        class="flex size-10 cursor-pointer items-center justify-center rounded-[calc(var(--radius-md)-4px)] text-2xl
          leading-none hover:bg-muted focus-visible:bg-muted aria-pressed:bg-muted"
        @click="choose(index)"
      >
        {{ hand }}
      </button>
    </PopoverContent>
  </Popover>
</template>
