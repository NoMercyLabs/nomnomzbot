<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot, free software licensed under the GNU Affero
   General Public License v3.0 or later. You may redistribute and/or modify it
   under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

   SPDX-License-Identifier: AGPL-3.0-or-later
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// One button in the player button editor: its icon, its label, the action it runs and that action's own
// settings. The action is chosen from the same catalog, grouped the same way, as the step palette of the
// pipeline editor, and its settings use the pipeline editor's step fields.
import ArrowDown from '~icons/nnz/arrow-down';
import ArrowUp from '~icons/nnz/arrow-up';
import ChevronDown from '~icons/nnz/chevron-down';
import Trash from '~icons/nnz/trash';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineActionDescriptor } from '@/api/pipelines';
import { buttonProblems, MAX_BUTTON_LABEL } from '@/api/playerButtons';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { FieldError, FieldGroup } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { useLocalizedText } from '@/composables/useLocalizedText';
import type { JsonValue } from '@/lib/json';
import StepField from '@/views/dashboard/pipelines/components/StepField.vue';
import { newAction } from '@/views/dashboard/pipelines/components/newSteps';
import { actionGroups } from '@/views/dashboard/pipelines/components/stepGroups';
import {
  actionRows,
  choiceContext,
  settingsAfter,
  type FieldRow,
} from '@/views/dashboard/pipelines/components/stepFields';
import { actionTitle } from '@/views/dashboard/pipelines/components/stepWords';

import type { DraftButton } from './playerButtonDraft';
import { PLAYER_ICON_KEYS, playerIcon } from './playerIcons';

const props = defineProps<{
  button: DraftButton;
  index: number;
  count: number;
  actions: PipelineActionDescriptor[];
  // True once the person tried to save: a problem shows then, not while the button is still being filled in.
  checked: boolean;
}>();

const emit = defineEmits<{
  change: [button: DraftButton];
  move: [step: -1 | 1];
  remove: [];
}>();

const { t, te } = useI18n();
const text = useLocalizedText();
const page = 'dashboard.music.now.buttons.editor';

const problems = computed(() =>
  props.checked
    ? buttonProblems(props.button)
    : {});
const groups = computed(() =>
  actionGroups(props.actions, {
    t,
    te,
    text,
  }));
const descriptor = computed(() =>
  props.actions.find((action) =>
    action.type === props.button.actionType));
const actionName = computed(() =>
  props.button.actionType === ''
    ? t(`${page}.chooseAction`)
    : actionTitle(props.button.actionType, { t, te }));

const rows = computed(() => {
  const [step] = newAction(props.button.actionType, () => props.button.key);

  if (step === undefined)
    return [];

  step.action = { ...props.button.parameters, type: props.button.actionType };

  return actionRows(step, descriptor.value, {
    t,
    te,
    text,
  });
});
const context = computed(() =>
  choiceContext(rows.value));

function patch(over: Partial<DraftButton>): void {
  emit('change', { ...props.button, ...over });
}

function chooseAction(type: string): void {
  patch({ actionType: type, parameters: {} });
}

function setting(row: FieldRow, value: JsonValue | undefined): void {
  const written = settingsAfter(rows.value, row, value);
  const parameters = { ...props.button.parameters, ...written };

  for (const name of Object.keys(parameters)) {
    if (parameters[name] === undefined) {
      delete parameters[name];
    }
  }

  patch({ parameters });
}
</script>

<template>
  <li data-player-button-row class="flex flex-col gap-3 rounded-md border bg-card p-3">
    <div class="flex items-start gap-2">
      <Popover>
        <PopoverTrigger as-child>
          <Button
            type="button"
            variant="outline"
            data-button-icon
            class="group shrink-0 gap-1! px-2!"
            :aria-label="$t(`${page}.icon`)"
          >
            <component :is="playerIcon(button.icon)" class="size-5" aria-hidden="true" />
            <ChevronDown
              class="text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
              aria-hidden="true"
            />
          </Button>
        </PopoverTrigger>
        <PopoverContent align="start" class="grid w-auto grid-cols-7 gap-1 p-2">
          <Button
            v-for="key in PLAYER_ICON_KEYS"
            :key="key"
            type="button"
            size="icon"
            :variant="key === button.icon ? 'secondary' : 'ghost'"
            :data-icon-option="key"
            :aria-label="key"
            :title="key"
            @click="patch({ icon: key })"
          >
            <component :is="playerIcon(key)" class="size-5" aria-hidden="true" />
          </Button>
        </PopoverContent>
      </Popover>
      <div class="flex min-w-0 flex-1 flex-col gap-1">
        <Input
          data-button-label
          :model-value="button.label"
          :maxlength="MAX_BUTTON_LABEL + 20"
          :aria-label="$t(`${page}.label`)"
          :aria-invalid="problems.label !== undefined"
          :placeholder="$t(`${page}.labelPlaceholder`)"
          @update:model-value="patch({ label: String($event) })"
        />
        <FieldError v-if="problems.label" data-problem="label">
          {{ $t(problems.label, { max: MAX_BUTTON_LABEL }) }}
        </FieldError>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        data-button-up
        :disabled="index === 0"
        :aria-label="$t(`${page}.moveUp`)"
        :title="$t(`${page}.moveUp`)"
        @click="emit('move', -1)"
      >
        <ArrowUp class="size-5" aria-hidden="true" />
      </Button>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        data-button-down
        :disabled="index === count - 1"
        :aria-label="$t(`${page}.moveDown`)"
        :title="$t(`${page}.moveDown`)"
        @click="emit('move', 1)"
      >
        <ArrowDown class="size-5" aria-hidden="true" />
      </Button>
      <Button
        type="button"
        variant="destructive"
        size="icon"
        data-button-remove
        :aria-label="$t(`${page}.remove`)"
        :title="$t(`${page}.remove`)"
        @click="emit('remove')"
      >
        <Trash class="size-5" aria-hidden="true" />
      </Button>
    </div>
    <div class="flex flex-col gap-1">
      <Popover>
        <PopoverTrigger as-child>
          <Button
            type="button"
            variant="outline"
            role="combobox"
            data-button-action
            class="group w-full justify-between font-normal"
            :aria-invalid="problems.action !== undefined"
          >
            <span class="truncate" :class="button.actionType === '' && 'text-muted-foreground'">
              {{ actionName }}
            </span>
            <ChevronDown
              class="text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
              aria-hidden="true"
            />
          </Button>
        </PopoverTrigger>
        <PopoverContent align="start" class="w-(--reka-popover-trigger-width) min-w-64 p-0">
          <Command>
            <CommandInput :placeholder="$t('dashboard.pipelines.workbench.palette.find')" />
            <CommandList>
              <CommandEmpty>{{ $t('dashboard.pipelines.workbench.palette.noMatch') }}</CommandEmpty>
              <CommandGroup v-for="group in groups" :key="group.key" :heading="group.heading">
                <CommandItem
                  v-for="row in group.rows"
                  :key="row.type"
                  :data-action-option="row.type"
                  :value="`${row.title} ${group.heading}`"
                  @select="chooseAction(row.type)"
                >
                  <component :is="group.icon" aria-hidden="true" />
                  {{ row.title }}
                </CommandItem>
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
      <FieldError v-if="problems.action" data-problem="action">{{ $t(problems.action) }}</FieldError>
    </div>
    <FieldGroup v-if="rows.length > 0" class="gap-3">
      <StepField
        v-for="row in rows"
        :key="`${button.key}:${button.actionType}:${row.name}`"
        :row="row"
        :context="context"
        @change="setting(row, $event)"
      />
    </FieldGroup>
  </li>
</template>
