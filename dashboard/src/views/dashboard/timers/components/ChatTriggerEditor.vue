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
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import { changeOf, draftOf, matchTypes, roles, type ChatTriggerDraft } from './chatTriggerDraft';

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
      <Button type="button" variant="outline" size="sm" @click="confirmOpen = true">
        <Trash2 aria-hidden="true" />
        {{ $t('dashboard.timers.editor.delete') }}
      </Button>
    </template>
    <div class="grid gap-5">
      <RowGroup>
        <SettingRow
          :title="$t('dashboard.timers.triggers.editor.enabled')"
          :description="$t('dashboard.timers.triggers.editor.enabledHelp')"
          :model-value="row.isEnabled"
          @update:model-value="toggle"
        />
      </RowGroup>

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

      <RowGroup>
        <SettingRow
          v-model="draft.caseSensitive"
          :title="$t('dashboard.timers.triggers.editor.caseSensitive')"
          :description="$t('dashboard.timers.triggers.editor.caseSensitiveHelp')"
        />
      </RowGroup>

      <Field>
        <FieldLabel for="trigger-response">{{ $t('dashboard.timers.triggers.editor.response') }}</FieldLabel>
        <Textarea
          id="trigger-response"
          v-model="draft.response"
          rows="3"
          :aria-label="$t('dashboard.timers.triggers.editor.response')"
        />
      </Field>

      <Field>
        <FieldLabel for="trigger-cooldown">{{ $t('dashboard.timers.triggers.editor.cooldown') }}</FieldLabel>
        <Input
          id="trigger-cooldown"
          type="number"
          min="0"
          step="1"
          :aria-label="$t('dashboard.timers.triggers.editor.cooldown')"
          :model-value="draft.cooldownSeconds"
          @update:model-value="setCooldown"
        />
        <FieldDescription>{{ $t('dashboard.timers.triggers.editor.cooldownHelp') }}</FieldDescription>
      </Field>

      <Field>
        <FieldLabel>{{ $t('dashboard.timers.triggers.editor.role') }}</FieldLabel>
        <Select v-model="draft.minPermissionLevel">
          <SelectTrigger :aria-label="$t('dashboard.timers.triggers.editor.role')" class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="level in roles" :key="level" :value="level">
              {{ $t(`dashboard.timers.triggers.roles.${level}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>

      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
    <template #footer>
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
