<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Display drawer: how the chat feed looks to this viewer. Every choice is kept on this device.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import ArrowCounterClockwise from '~icons/nnz/arrow-counter-clockwise';
import ArrowRight from '~icons/nnz/arrow-right';
import Check from '~icons/nnz/check';

import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';

import { chatDisplayDefaults, type ChatDisplay } from '../chatDisplay';

type SwitchName = 'badges' | 'avatars' | 'platform' | 'alternating' | 'narrow' | 'mentions';
type SegmentName = 'timestamps' | 'nameColors' | 'spacing' | 'emoteSize' | 'deleted';

const display = defineModel<ChatDisplay>({ required: true });

const { t } = useI18n();

// The label key of each value, under dashboard.chat.display.
const segmentKeys = {
  timestamps: {
    off: 'off',
    24: 'hour24',
    12: 'hour12',
  },
  nameColors: {
    sent: 'asSent',
    readable: 'readable',
    off: 'off',
  },
  spacing: {
    tight: 'tight',
    normal: 'normal',
    roomy: 'roomy',
  },
  emoteSize: {
    s: 'small',
    m: 'medium',
    l: 'large',
  },
  deleted: {
    hide: 'hide',
    strike: 'strike',
    show: 'show',
  },
} as const satisfies Record<SegmentName, Record<string, string>>;

const sections = [
  {
    key: 'show',
    switches: ['badges', 'avatars', 'platform'],
    segments: ['timestamps', 'nameColors'],
  },
  {
    key: 'layout',
    switches: ['alternating', 'narrow'],
    segments: ['spacing'],
  },
  {
    key: 'emotes',
    switches: [],
    segments: ['emoteSize'],
  },
  {
    key: 'deleted',
    switches: ['mentions'],
    segments: ['deleted'],
  },
] as const satisfies readonly {
  key: string;
  switches: readonly SwitchName[];
  segments: readonly SegmentName[];
}[];

function choices(name: SegmentName): { value: string; label: string }[] {
  const keys: Record<string, string> = segmentKeys[name];

  return Object.keys(keys).map((value: string) => ({
    value,
    label: t(`dashboard.chat.display.${keys[value]}`),
  }));
}

const segmentChoices = computed(() => ({
  timestamps: choices('timestamps'),
  nameColors: choices('nameColors'),
  spacing: choices('spacing'),
  emoteSize: choices('emoteSize'),
  deleted: choices('deleted'),
}));

// Three or more options do not fit beside their label in a 320px drawer: the control goes
// under the label, full width, instead of being cut off.
function stacked(name: SegmentName): boolean {
  return Object.keys(segmentKeys[name]).length > 2;
}

function segmentRowClass(name: SegmentName): string {
  return stacked(name)
    ? 'flex-col items-stretch'
    : 'items-center';
}

function set(name: SwitchName | SegmentName, value: unknown): void {
  display.value = { ...display.value, [name]: value };
}

function reset(): void {
  display.value = { ...chatDisplayDefaults };
}
</script>

<template>
  <PanelCard :title="$t('dashboard.chat.display.title')" pad class="h-full">
    <template #actions>
      <Button data-display-reset variant="ghost" size="sm" @click="reset">
        <ArrowCounterClockwise class="size-4" aria-hidden="true" />
        {{ $t('dashboard.chat.display.reset') }}
      </Button>
    </template>
    <div class="grid min-w-0 grid-cols-[minmax(0,1fr)] gap-4">
      <div class="flex items-center gap-2 text-sm">
        <span class="min-w-0 flex-1">
          <span class="block font-medium">{{ $t('dashboard.chat.display.readability') }}</span>
          <span class="block text-muted-foreground">{{
            $t('dashboard.chat.display.readabilityHelp')
          }}</span>
        </span>
        <Button as-child variant="ghost" size="sm" class="flex-none">
          <RouterLink data-display-readability :to="{ name: 'dashboard-settings' }">
            {{ $t('dashboard.chat.display.change') }}
            <ArrowRight class="size-4" aria-hidden="true" />
          </RouterLink>
        </Button>
      </div>
      <section v-for="section in sections" :key="section.key" class="grid gap-1">
        <h3 class="px-1 text-sm font-medium text-muted-foreground">
          {{ $t(`dashboard.chat.display.section.${section.key}`) }}
        </h3>
        <RowGroup>
          <SettingRow
            v-for="name in section.switches"
            :key="name"
            :data-option="name"
            :title="$t(`dashboard.chat.display.option.${name}`)"
            :model-value="display[name]"
            @update:model-value="set(name, $event)"
          />
          <div
            v-for="name in section.segments"
            :key="name"
            :data-option="name"
            class="flex min-h-11 gap-2 px-3 py-2"
            :class="segmentRowClass(name)"
          >
            <span class="min-w-0 text-sm font-medium" :class="{ 'flex-1': !stacked(name) }">{{
              $t(`dashboard.chat.display.option.${name}`)
            }}</span>
            <SegmentedControl
              :class="{ 'w-full': stacked(name) }"
              :label="$t(`dashboard.chat.display.option.${name}`)"
              :options="segmentChoices[name]"
              :model-value="String(display[name])"
              @update:model-value="set(name, $event)"
            />
          </div>
        </RowGroup>
      </section>
    </div>
    <template #footer>
      <Check class="size-4 flex-none text-muted-foreground" aria-hidden="true" />
      <span class="text-sm text-muted-foreground">{{ $t('dashboard.chat.display.kept') }}</span>
    </template>
  </PanelCard>
</template>
