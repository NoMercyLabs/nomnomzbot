<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Every step a person can add (5-workbench.html:211-235): the checks and groups first, then each catalog
// category with its actions by name. A search on top narrows all of them; a pick adds the step.
import Cross from '~icons/nnz/cross';
import { computed, useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineActionDescriptor } from '@/api/pipelines';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Kbd } from '@/components/ui/kbd';
import { useLocalizedText } from '@/composables/useLocalizedText';
import { useShortcut } from '@/composables/useShortcut';

import { ADDABLE_BLOCKS, type AddableBlock } from './newSteps';
import { actionGroups } from './stepGroups';
import { blockIcon } from './stepIcons';

const props = defineProps<{
  actions: PipelineActionDescriptor[];
  readOnly: boolean;
  // Where a pick goes, in words, when the person chose a lane; null adds at the end.
  target: string | null;
}>();

defineEmits<{
  block: [kind: AddableBlock];
  action: [type: string];
  release: [];
}>();

const { t, te } = useI18n();
const text = useLocalizedText();
const page = 'dashboard.pipelines.workbench';

const blocks = computed(() =>
  ADDABLE_BLOCKS.map((kind) =>
    ({
      kind,
      title: t(`${page}.block.${kind}`),
      icon: blockIcon(kind),
    })));

const groups = computed(() =>
  actionGroups(props.actions, {
    t,
    te,
    text,
  }));

const root = useTemplateRef<InstanceType<typeof PanelCard>>('root');

// The page opens the palette to add a step; the search takes the keys at once.
function focus(): void {
  root.value?.$el.querySelector('input')?.focus();
}

// The step search is the search of this page, so it answers "/" like every list search.
const shortcut = { key: '/', label: 'common.shortcuts.search' } as const;

useShortcut(shortcut, focus);

defineExpose({ focus });
</script>

<template>
  <PanelCard
    ref="root"
    data-step-palette
    :title="$t('dashboard.pipelines.workbench.palette.label')"
    class="h-full"
  >
    <ReadOnlyScope
      :read-only="readOnly"
      :reason="$t('dashboard.pipelines.noWrite')"
      class="flex h-full min-h-0 flex-col"
    >
      <Command class="min-h-0 flex-1 bg-transparent">
        <CommandInput :placeholder="$t('dashboard.pipelines.workbench.palette.find')">
          <!-- The same quiet key cap as SearchField, curved to this field (8px minus the 4px inset). -->
          <Kbd class="rounded-[4px]! border border-border bg-transparent">{{ shortcut.key }}</Kbd>
        </CommandInput>
        <p
          v-if="target"
          data-palette-target
          class="mx-2 mt-2 flex items-center gap-2 rounded-md bg-muted py-1 pr-1 pl-3 text-xs font-medium"
        >
          <span class="min-w-0 flex-1">{{ target }}</span>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            :aria-label="$t('dashboard.pipelines.workbench.palette.release')"
            :title="$t('dashboard.pipelines.workbench.palette.release')"
            @click="$emit('release')"
          >
            <Cross aria-hidden="true" />
          </Button>
        </p>
        <CommandList class="max-h-none min-h-0 flex-1">
          <CommandEmpty>{{ $t('dashboard.pipelines.workbench.palette.noMatch') }}</CommandEmpty>
          <CommandGroup data-palette-group="blocks" :heading="$t('dashboard.pipelines.workbench.palette.blocks')">
            <CommandItem
              v-for="block in blocks"
              :key="block.kind"
              :data-palette-block="block.kind"
              :value="block.title"
              @select="$emit('block', block.kind)"
            >
              <component :is="block.icon" aria-hidden="true" />
              {{ block.title }}
            </CommandItem>
          </CommandGroup>
          <CommandGroup
            v-for="group in groups"
            :key="group.key"
            :data-palette-group="group.key"
            :heading="group.heading"
          >
            <CommandItem
              v-for="row in group.rows"
              :key="row.type"
              :data-palette-action="row.type"
              :value="`${row.title} ${group.heading}`"
              @select="$emit('action', row.type)"
            >
              <component :is="group.icon" aria-hidden="true" />
              {{ row.title }}
            </CommandItem>
          </CommandGroup>
        </CommandList>
      </Command>
    </ReadOnlyScope>
  </PanelCard>
</template>
