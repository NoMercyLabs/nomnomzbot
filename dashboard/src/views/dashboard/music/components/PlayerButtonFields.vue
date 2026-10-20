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
// The edit options of one player button: its icon, its label and the settings of its action, the same
// settings the Stream Deck plugin key asks for. The settings use the pipeline editor's step fields, read from
// the bot's own action catalog.
import ChevronDown from '~icons/nnz/chevron-down';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineActionDescriptor } from '@/api/pipelines';
import { buttonProblems, MAX_BUTTON_LABEL } from '@/api/playerButtons';
import { Button } from '@/components/ui/button';
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { useLocalizedText } from '@/composables/useLocalizedText';
import type { JsonValue } from '@/lib/json';
import StepField from '@/views/dashboard/pipelines/components/StepField.vue';
import { newAction } from '@/views/dashboard/pipelines/components/newSteps';
import {
  actionRows,
  choiceContext,
  settingsAfter,
  type FieldRow,
} from '@/views/dashboard/pipelines/components/stepFields';

import type { DraftButton } from './playerButtonDraft';
import { PLAYER_ICON_KEYS, playerIcon } from './playerIcons';

const props = defineProps<{
  button: DraftButton;
  descriptor: PipelineActionDescriptor | undefined;
  // True once the person tried to save: a problem shows then, not while the button is still being filled in.
  checked: boolean;
}>();

const emit = defineEmits<{
  change: [button: DraftButton];
}>();

const { t, te } = useI18n();
const text = useLocalizedText();
const page = 'dashboard.music.now.buttons.editor';

const problems = computed(() =>
  props.checked
    ? buttonProblems(props.button)
    : {});

const rows = computed(() => {
  const [step] = newAction(props.button.actionType, () => props.button.key);

  if (step === undefined)
    return [];

  step.action = { ...props.button.parameters, type: props.button.actionType };

  return actionRows(step, props.descriptor, {
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
  <FieldGroup data-player-button-fields class="gap-4">
    <Field>
      <FieldLabel for="player-button-label">{{ $t(`${page}.label`) }}</FieldLabel>
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
        <Input
          id="player-button-label"
          data-button-label
          :model-value="button.label"
          :maxlength="MAX_BUTTON_LABEL + 20"
          :aria-invalid="problems.label !== undefined"
          :placeholder="$t(`${page}.labelPlaceholder`)"
          @update:model-value="patch({ label: String($event) })"
        />
      </div>
      <FieldError v-if="problems.label" data-problem="label">
        {{ $t(problems.label, { max: MAX_BUTTON_LABEL }) }}
      </FieldError>
    </Field>
    <StepField
      v-for="row in rows"
      :key="`${button.key}:${button.actionType}:${row.name}`"
      :row="row"
      :context="context"
      @change="setting(row, $event)"
    />
  </FieldGroup>
</template>
