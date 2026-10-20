<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import type { JsonValue } from '@/lib/json';
// The picked step in detail (5-workbench.html:344-379): Settings edits every field the bot's catalog gives the
// action, or the block's own settings; In plain words says what the step does from the same facts. Each edit,
// Duplicate and Remove change the draft; a role that cannot change pipelines sees them locked.
import FileCopy from '~icons/nnz/file-copy';
import Trash from '~icons/nnz/trash';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineActionDescriptor, PipelineStep } from '@/api/pipelines';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import { Button } from '@/components/ui/button';
import { FieldGroup } from '@/components/ui/field';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useLocalizedText } from '@/composables/useLocalizedText';

import type { EarlierText } from './stepChain';
import type { StepPatch } from './stepEdits';
import StepField from './StepField.vue';
import { actionRows, blockRows, choiceContext, type FieldRow, settingsAfter } from './stepFields';
import { conditionSentence, stepSentence, stepTitle } from './stepWords';

const props = defineProps<{
  step: PipelineStep;
  number: string;
  descriptor: PipelineActionDescriptor | undefined;
  readOnly: boolean;
  earlier?: EarlierText[];
  variables?: string[];
}>();

const emit = defineEmits<{
  duplicate: [];
  remove: [];
  patch: [patch: StepPatch];
}>();

const { t, te } = useI18n();
const text = useLocalizedText();
const page = 'dashboard.pipelines.workbench';

const tab = ref('settings');

const earlierOptions = computed(() =>
  (props.earlier ?? []).map((item) =>
    ({
      key: item.key,
      text: item.text,
      label: t(`${page}.inspector.earlier.from`, {
        number: item.number,
        title: stepTitle(item.step, { t, te }),
      }),
    })));

watch(
  () => props.step.id,
  () => {
    tab.value = 'settings';
  },
);

const title = computed(() => stepTitle(props.step, { t, te }));

const fields = computed(() =>
  props.step.block_kind === null
    ? actionRows(props.step, props.descriptor, {
        t,
        te,
        text,
      })
    : blockRows(props.step, { t, te }),
);

const context = computed(() =>
  choiceContext(fields.value));

const keepGoing = computed<FieldRow>(() => ({
  name: 'continue_on_error',
  label: t(`${page}.inspector.keepGoing`),
  kind: 'boolean',
  control: 'switch',
  value: props.step.continue_on_error,
  help: t(`${page}.inspector.keepGoingHelp`),
  choices: [],
  required: false,
  templated: false,
  target: 'step',
}));

// Stopping at a match needs a condition: without one the step always runs and there is nothing to match.
const stopOnMatch = computed<FieldRow>(() => {
  const hasCondition = props.step.condition !== null;

  return {
    name: 'stop_on_match',
    label: t(`${page}.inspector.stopOnMatch`),
    kind: 'boolean',
    control: 'switch',
    value: props.step.stop_on_match,
    help: hasCondition
      ? t(`${page}.inspector.stopOnMatchHelp`)
      : t(`${page}.inspector.stopOnMatchNeedsCondition`),
    choices: [],
    required: false,
    templated: false,
    target: 'step',
    disabled: !hasCondition,
  };
});

function change(row: FieldRow, value: JsonValue | undefined): void {
  if (row.target !== 'step') {
    emit('patch', { [row.target]: settingsAfter(fields.value, row, value) });

    return;
  }

  emit('patch', row.name === 'stop_on_match'
    ? { stop_on_match: value === true }
    : { continue_on_error: value === true });
}

const declares = computed(() =>
  (props.descriptor?.fields ?? []).some((field) => field.declaresVariable),
);

const condition = computed(() =>
  props.step.condition
    ? conditionSentence(props.step.condition, { t, te })
    : t(`${page}.inspector.always`),
);

const plainOnError = computed(() =>
  props.step.continue_on_error
    ? t(`${page}.inspector.plainGoesOn`)
    : t(`${page}.inspector.plainStops`),
);

const sentence = computed(() => stepSentence(props.descriptor, text));
</script>

<template>
  <PanelCard
    data-step-inspector
    :title="title"
    :description="$t('dashboard.pipelines.workbench.inspector.number', { number })"
    pad
    class="h-full"
  >
    <Tabs v-model="tab" class="flex min-h-0 flex-1 flex-col">
      <TabsList class="w-full">
        <TabsTrigger value="settings" data-inspector-tab="settings">
          {{ $t("dashboard.pipelines.workbench.inspector.settings") }}
        </TabsTrigger>
        <TabsTrigger value="plain" data-inspector-tab="plain">
          {{ $t("dashboard.pipelines.workbench.inspector.plain") }}
        </TabsTrigger>
      </TabsList>
      <TabsContent
        value="settings"
        class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1 pt-2 pb-1"
      >
        <ReadOnlyScope :read-only="readOnly" :reason="$t('dashboard.pipelines.noWrite')">
          <FieldGroup data-inspector-fields class="gap-4">
            <StepField
              v-for="row in fields"
              :key="`${step.id}:${row.name}`"
              :row="row"
              :context="context"
              :earlier="earlierOptions"
              :variables="variables ?? []"
              @change="change(row, $event)"
            />
            <p v-if="fields.length === 0" class="text-sm text-muted-foreground">
              {{ $t("dashboard.pipelines.workbench.inspector.noFields") }}
            </p>
            <StepField
              :key="`${step.id}:keep-going`"
              :row="keepGoing"
              @change="change(keepGoing, $event)"
            />
            <StepField
              :key="`${step.id}:stop-on-match`"
              :row="stopOnMatch"
              @change="change(stopOnMatch, $event)"
            />
          </FieldGroup>
        </ReadOnlyScope>
        <KeyValueList
          :items="[
            { label: $t('dashboard.pipelines.workbench.inspector.condition'), value: condition },
          ]"
        />
      </TabsContent>
      <TabsContent value="plain" data-inspector-plain class="flex flex-col gap-2 pt-2 text-sm">
        <p v-if="sentence">{{ sentence }}</p>
        <p>{{ condition }}</p>
        <p>{{ plainOnError }}</p>
        <p v-if="declares" class="text-muted-foreground">
          {{ $t("dashboard.pipelines.workbench.inspector.declares") }}
        </p>
      </TabsContent>
    </Tabs>
    <template v-if="!readOnly" #footer>
      <ReadOnlyScope
        :read-only="readOnly"
        :reason="$t('dashboard.pipelines.noWrite')"
        class="flex flex-wrap gap-2"
      >
        <Button
          data-step-duplicate
          type="button"
          variant="outline"
          size="sm"
          @click="$emit('duplicate')"
        >
          <FileCopy aria-hidden="true" />
          {{ $t("dashboard.pipelines.workbench.inspector.duplicate") }}
        </Button>
        <Button data-step-remove
                type="button"
                variant="outline"
                size="sm"
                @click="$emit('remove')"
        >
          <Trash aria-hidden="true" />
          {{ $t("dashboard.pipelines.workbench.inspector.remove") }}
        </Button>
      </ReadOnlyScope>
    </template>
  </PanelCard>
</template>
