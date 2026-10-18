<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The editor of one chat trigger. The Enabled switch saves at once; every other field is a draft that
// Save sends as the changed fields only. A refused save keeps the draft and shows the bot's reason.
import Trash2 from '~icons/nnz/trash';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useUpdateChatTrigger, type ChatTrigger } from '@/api/chatTriggers';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReplyModeField from '@/components/layout/ReplyModeField.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import { Button } from '@/components/ui/button';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import { rungNames } from '@/api/permissions';
import { changeOf, draftOf, matchTypes, type ChatTriggerDraft } from './chatTriggerDraft';

const props = defineProps<{ row: ChatTrigger }>();
const emit = defineEmits<{ remove: [row: ChatTrigger] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const update = useUpdateChatTrigger(channelId);

const draft = ref<ChatTriggerDraft>(draftOf(props.row));
const failure = ref<string | null>(null);
const confirmOpen = ref(false);

const dirtyCount = computed<number>(() =>
  Object.keys(changeOf(draft.value, props.row)).length);
const unsaved = computed<string>(() =>
  dirtyCount.value === 0
    ? ''
    : t('dashboard.timers.editor.unsaved', { count: dirtyCount.value }, dirtyCount.value),
);

function setCooldown(value: string | number): void {
  draft.value.cooldownSeconds = Number(value);
}

async function save(): Promise<void> {
  if (dirtyCount.value === 0) return;

  failure.value = null;

  try {
    await update.mutateAsync({ triggerId: props.row.id, change: changeOf(draft.value, props.row) });
  } catch (error) {
    failure.value = errorText(error, t);
  }
}

function toggle(): void {
  update.mutate({ triggerId: props.row.id, change: { isEnabled: !props.row.isEnabled } });
}

function discard(): void {
  failure.value = null;
  draft.value = draftOf(props.row);
}

function remove(): void {
  confirmOpen.value = false;
  emit('remove', props.row);
}
</script>

<template>
  <PanelCard data-trigger-editor tier="lead" pad :title="row.pattern">
    <template #actions>
      <LabeledSwitch
        :label="$t('dashboard.timers.triggers.editor.enabled')"
        :help="$t('dashboard.timers.triggers.editor.enabledHelp')"
        :model-value="row.isEnabled"
        @update:model-value="toggle"
      />
    </template>
    <div class="grid gap-5">
      <!-- What to watch for: the words and how they match are one decision, so they share a row. -->
      <div class="grid gap-3">
        <div class="grid gap-4 md:grid-cols-[2fr_1fr]">
          <Field>
            <FieldLabel for="trigger-pattern">{{ $t('dashboard.timers.triggers.editor.pattern') }}</FieldLabel>
            <Input
              id="trigger-pattern"
              v-model="draft.pattern"
              autocomplete="off"
              :aria-label="$t('dashboard.timers.triggers.editor.pattern')"
            />
          </Field>
          <Field>
            <FieldLabel>{{ $t('dashboard.timers.triggers.editor.matchType') }}</FieldLabel>
            <Select v-model="draft.matchType">
              <SelectTrigger :aria-label="$t('dashboard.timers.triggers.editor.matchType')" class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="kind in matchTypes" :key="kind" :value="kind">
                  {{ $t(`dashboard.timers.triggers.match.${kind}`) }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
        </div>
        <LabeledSwitch
          v-model="draft.caseSensitive"
          :label="$t('dashboard.timers.triggers.editor.caseSensitive')"
          :help="$t('dashboard.timers.triggers.editor.caseSensitiveHelp')"
        />
      </div>

      <ReplyModeField
        v-model:mode="draft.mode"
        v-model:pipeline-id="draft.pipelineId"
        :new-pipeline-name="row.pattern"
      >
        <TemplateField
          id="trigger-response"
          v-model="draft.response"
          context="command"
          :rows="3"
          :maxlength="500"
          :label="$t('dashboard.timers.triggers.editor.response')"
        />
      </ReplyModeField>

      <div class="grid gap-4 md:grid-cols-2">
        <Field>
          <div class="flex items-center gap-1.5">
            <FieldLabel for="trigger-cooldown">{{ $t('dashboard.timers.triggers.editor.cooldown') }}</FieldLabel>
            <FieldInfo>{{ $t('dashboard.timers.triggers.editor.cooldownHelp') }}</FieldInfo>
          </div>
          <Input
            id="trigger-cooldown"
            type="number"
            min="0"
            step="1"
            :aria-label="$t('dashboard.timers.triggers.editor.cooldown')"
            :model-value="draft.cooldownSeconds"
            @update:model-value="setCooldown"
          />
        </Field>

        <Field>
          <FieldLabel>{{ $t('dashboard.timers.triggers.editor.role') }}</FieldLabel>
          <Select v-model="draft.minPermissionLevel">
            <SelectTrigger :aria-label="$t('dashboard.timers.triggers.editor.role')" class="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="level in rungNames" :key="level" :value="level">
                {{ $t(`dashboard.timers.triggers.roles.${level}`) }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field>
      </div>

      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
    <template #footer>
      <!-- Delete sits apart at the far left, away from Save, quiet; its confirm carries the danger. -->
      <Button
        type="button"
        variant="outline"
        size="sm"
        @click="confirmOpen = true"
      >
        <Trash2 aria-hidden="true" />
        {{ $t('dashboard.timers.editor.delete') }}
      </Button>
      <span data-unsaved class="mr-auto text-sm text-muted-foreground">{{ unsaved }}</span>
      <Button type="button" variant="outline" size="sm" @click="discard">{{
        $t('dashboard.timers.editor.discard')
      }}</Button>
      <Button
        type="button"
        size="sm"
        :disabled="dirtyCount === 0 || update.isPending.value"
        @click="save"
      >
        {{ $t('dashboard.timers.editor.save') }}
      </Button>
    </template>
    <ConfirmDialog
      v-model:open="confirmOpen"
      :title="$t('dashboard.timers.triggers.editor.confirmTitle', { name: row.pattern })"
      :description="$t('dashboard.timers.triggers.editor.confirmBody')"
      :action="$t('dashboard.timers.triggers.editor.confirmAction')"
      @confirm="remove"
    />
  </PanelCard>
</template>
