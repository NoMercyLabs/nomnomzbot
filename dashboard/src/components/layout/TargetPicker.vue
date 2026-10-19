<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one place a surface picks what it runs: a command, a pipeline or one action with its settings (the
// fields come from the bot's catalog, the same form a pipeline step has). It can create an empty pipeline in
// place and clear the choice.
// A refusal from the server and a bound id that is missing from this channel show as one error line.
import Plus from '~icons/nnz/add';
import { computed, inject } from 'vue';
import { useI18n } from 'vue-i18n';
import { routerKey } from 'vue-router';

import { useCommands } from '@/api/commands';
import {
  useCreatePipeline,
  usePipelineCatalog,
  usePipelines,
  type Pipeline,
} from '@/api/pipelines';
import { Button } from '@/components/ui/button';
import { FieldError } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useLocalizedText } from '@/composables/useLocalizedText';
import type { ActionParams, PickedTarget, TargetKind } from '@/lib/bindingTarget';
import ActionParamsForm from '@/views/dashboard/pipelines/components/ActionParamsForm.vue';
import { actionGroups } from '@/views/dashboard/pipelines/components/stepGroups';
import { actionTitle } from '@/views/dashboard/pipelines/components/stepWords';

const target = defineModel<PickedTarget | null>('target', { required: true });

const props = withDefaults(
  defineProps<{
    kinds?: readonly TargetKind[];
    clearable?: boolean;
    createName?: string;
    disabled?: boolean;
    error?: string;
    label?: string;
  }>(),
  {
    kinds: () => ['command', 'pipeline'],
    clearable: false,
    createName: '',
    disabled: false,
    error: '',
    label: '',
  },
);

const CREATE = '__create';
const NOTHING = '__nothing';

const { t, te } = useI18n();
const text = useLocalizedText();
const { channelId } = useCurrentChannel();
const commands = useCommands(channelId);
const pipelines = usePipelines(channelId);
const catalog = usePipelineCatalog(channelId);
const create = useCreatePipeline(channelId);

const commandRows = computed(() => commands.data.value ?? []);
const pipelineRows = computed(() => pipelines.data.value ?? []);
const actionList = computed(() => catalog.data.value?.actions ?? []);
const showCommands = computed<boolean>(() => props.kinds.includes('command'));
const showPipelines = computed<boolean>(() => props.kinds.includes('pipeline'));
const showActions = computed<boolean>(() => props.kinds.includes('action'));

const groups = computed(() =>
  actionGroups(actionList.value, {
    t,
    te,
    text,
  }));

const descriptor = computed(() =>
  actionList.value.find((action) =>
    action.type === target.value?.actionType));

const params = computed<ActionParams>({
  get: () => target.value?.params ?? {},
  set: (next: ActionParams): void => {
    if (target.value?.kind === 'action') {
      target.value = { ...target.value, params: next };
    }
  },
});

function valueOf(picked: PickedTarget | null): string {
  if (picked === null)
    return NOTHING;

  return picked.kind === 'action'
    ? `action:${picked.actionType ?? ''}`
    : `${picked.kind}:${picked.id}`;
}

const known = computed<boolean>(() => {
  const picked = target.value;

  if (picked === null || picked.kind === 'action')
    return true;

  // Rows still loading are not proof the id is foreign.
  if (picked.kind === 'command' && commands.data.value === undefined)
    return true;

  if (picked.kind === 'pipeline' && pipelines.data.value === undefined)
    return true;

  return picked.kind === 'command'
    ? commandRows.value.some((row) => row.id === picked.id)
    : pipelineRows.value.some((row) => row.id === picked.id);
});

const shown = computed<string>(() => {
  const picked = target.value;

  if (picked === null)
    return '';

  if (picked.kind === 'action')
    return actionTitle(picked.actionType ?? '', { t, te });

  if (picked.kind === 'command') {
    const row = commandRows.value.find((candidate) => candidate.id === picked.id);

    return row
      ? `!${row.name}`
      : picked.id;
  }

  return pipelineRows.value.find((candidate) => candidate.id === picked.id)?.name ?? picked.id;
});

const message = computed<string>(() => {
  if (props.error !== '')
    return props.error;

  return known.value
    ? ''
    : t('common.targetPicker.missing');
});

const selected = computed<string>({
  get: () => valueOf(target.value),
  set: (value: string): void => {
    if (value === CREATE) {
      void createPipeline();

      return;
    }

    if (value === NOTHING) {
      target.value = null;

      return;
    }

    const split = value.indexOf(':');
    const kind = value.slice(0, split) as TargetKind;
    const rest = value.slice(split + 1);

    // Choosing the action already picked keeps its settings; another action starts with none.
    if (kind === 'action') {
      if (target.value?.actionType !== rest) {
        target.value = {
          kind,
          id: '',
          actionType: rest,
          params: {},
        };
      }

      return;
    }

    target.value = { kind, id: rest };
  },
});

// A plain link that routes through the router when one is installed, so a bare mount still has the href.
const router = inject(routerKey, null);
const editorPath = computed<string>(() => `/app/pipelines/${target.value?.id ?? ''}`);

function openEditor(): void {
  void router?.push(editorPath.value);
}

async function createPipeline(): Promise<void> {
  const name: string = props.createName || t('common.targetPicker.newName');
  const made: Pipeline = await create.mutateAsync({ name });

  target.value = { kind: 'pipeline', id: made.id };
}
</script>

<template>
  <div data-target-picker class="grid gap-1.5">
    <div class="flex items-center gap-2">
      <Select v-model="selected" :disabled="disabled || create.isPending.value">
        <SelectTrigger
          :aria-label="label || $t('common.targetPicker.label')"
          :aria-invalid="message !== ''"
          class="min-w-0 flex-1"
        >
          <SelectValue :placeholder="$t('common.targetPicker.placeholder')">
            <span v-if="shown !== ''" class="truncate">{{ shown }}</span>
          </SelectValue>
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-if="showPipelines" data-pipeline-create :value="CREATE">
            <Plus aria-hidden="true" />
            {{ $t('common.targetPicker.create') }}
          </SelectItem>
          <SelectItem v-if="clearable" :value="NOTHING">
            {{ $t('common.targetPicker.nothing') }}
          </SelectItem>
          <SelectSeparator v-if="showPipelines || clearable" />
          <SelectGroup v-if="showCommands">
            <SelectLabel>{{ $t('common.targetPicker.commands') }}</SelectLabel>
            <SelectItem v-for="row in commandRows" :key="row.id" :value="`command:${row.id}`">
              {{ `!${row.name}` }}
            </SelectItem>
          </SelectGroup>
          <SelectGroup v-if="showPipelines">
            <SelectLabel>{{ $t('common.targetPicker.pipelines') }}</SelectLabel>
            <SelectItem
              v-for="row in pipelineRows"
              :key="row.id"
              :value="`pipeline:${row.id}`"
            >
              {{ row.name }}
            </SelectItem>
          </SelectGroup>
          <SelectGroup v-if="showActions">
            <SelectLabel>{{ $t('common.targetPicker.actions') }}</SelectLabel>
            <template v-for="group in groups" :key="group.key">
              <SelectItem
                v-for="row in group.rows"
                :key="row.type"
                :value="`action:${row.type}`"
              >
                {{ `${group.heading}: ${row.title}` }}
              </SelectItem>
            </template>
          </SelectGroup>
        </SelectContent>
      </Select>
      <Button
        v-if="target?.kind === 'pipeline' && target.id !== ''"
        as-child
        type="button"
        variant="outline"
        class="flex-none"
      >
        <a :href="editorPath" @click.prevent="openEditor">
          {{ $t('common.targetPicker.open') }}
        </a>
      </Button>
    </div>
    <ActionParamsForm
      v-if="target?.kind === 'action' && target.actionType"
      v-model:params="params"
      :action-type="target.actionType"
      :descriptor="descriptor"
      class="mt-2"
    />
    <FieldError v-if="message !== ''">{{ message }}</FieldError>
  </div>
</template>
