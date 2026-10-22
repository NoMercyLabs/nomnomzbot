<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A new command: a name and what it does (a reply, a random reply, a pipeline or a code script).
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreateCommand, type NewCommand } from '@/api/commands';
import TargetPicker from '@/components/layout/TargetPicker.vue';
import type { PickedTarget } from '@/lib/bindingTarget';
import FormDialog from '@/components/layout/FormDialog.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import type { MatchMode, PrefixMode } from './commandDraft';
import RandomReplyList from './RandomReplyList.vue';
import TriggerFields from './TriggerFields.vue';
import CodeScriptPicker from './CodeScriptPicker.vue';

type Kind = 'template' | 'random' | 'pipeline' | 'code';

const open = defineModel<boolean>('open', { required: true });
const emit = defineEmits<{ created: [name: string] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateCommand(channelId);

const name = ref('');
const kind = ref<Kind>('template');
const reply = ref('');
const replies = ref<string[]>(['']);
const pipelineTarget = ref<PickedTarget | null>(null);
const codeScriptId = ref('');
const prefixMode = ref<PrefixMode>('Default');
const customPrefix = ref('');
const matchMode = ref<MatchMode>('StartsWith');
const matchPattern = ref('');
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  name.value = '';
  kind.value = 'template';
  reply.value = '';
  replies.value = [''];
  pipelineTarget.value = null;
  codeScriptId.value = '';
  prefixMode.value = 'Default';
  customPrefix.value = '';
  matchMode.value = 'StartsWith';
  matchPattern.value = '';
  error.value = undefined;
});

const triggerOk = computed<boolean>(
  () =>
    (prefixMode.value !== 'Custom' || customPrefix.value !== '') &&
    (matchMode.value !== 'Regex' || matchPattern.value !== ''),
);

function cleaned(raw: string): string {
  return raw.trim().replace(/^!+/, '').toLowerCase();
}

// Only the trigger options that differ from the bot's defaults travel with the new command.
function triggerOf() {
  return {
    ...(prefixMode.value === 'Default'
      ? {}
      : { prefixMode: prefixMode.value }),
    ...(prefixMode.value === 'Custom'
      ? { customPrefix: customPrefix.value }
      : {}),
    ...(matchMode.value === 'StartsWith'
      ? {}
      : { matchMode: matchMode.value }),
    ...(matchMode.value === 'Regex'
      ? { matchPattern: matchPattern.value }
      : {}),
  } as const satisfies Partial<NewCommand>;
}

function replyOf(): Partial<NewCommand> {
  if (kind.value === 'random')
    return { tier: 'template', templateResponses: replies.value };

  if (kind.value === 'pipeline')
    return { tier: 'pipeline', pipelineId: pipelineTarget.value?.id ?? '' };

  if (kind.value === 'code')
    return { tier: 'code', codeScriptId: codeScriptId.value };

  return { tier: 'template', templateResponse: reply.value };
}

function bodyOf(clean: string) {
  return {
    name: clean,
    ...replyOf(),
    ...triggerOf(),
  } as const satisfies NewCommand;
}

async function submit(): Promise<void> {
  const clean = cleaned(name.value);

  if (clean === '') {
    error.value = t('dashboard.commands.dialog.nameRequired');

    return;
  }

  if (!triggerOk.value) {
    error.value = t('dashboard.commands.dialog.triggerRequired');

    return;
  }

  if (kind.value === 'code' && codeScriptId.value === '') {
    error.value = t('dashboard.commands.dialog.scriptRequired');

    return;
  }

  error.value = undefined;

  try {
    await create.mutateAsync(bodyOf(clean));
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
  emit('created', clean);
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="$t('dashboard.commands.dialog.title')"
    :description="$t('dashboard.commands.dialog.description')"
    :action="$t('dashboard.commands.dialog.create')"
    :pending="create.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="new-command-name">{{ $t('dashboard.commands.dialog.name') }}</FieldLabel>
      <Input
        id="new-command-name"
        v-model="name"
        data-new-name
        autocomplete="off"
        :placeholder="$t('dashboard.commands.dialog.namePlaceholder')"
      />
    </Field>
    <Field>
      <FieldLabel>{{ $t('dashboard.commands.dialog.type') }}</FieldLabel>
      <Select v-model="kind">
        <SelectTrigger :aria-label="$t('dashboard.commands.dialog.type')" class="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="template">{{
            $t('dashboard.commands.dialog.typeTemplate')
          }}</SelectItem>
          <SelectItem value="random">{{
            $t('dashboard.commands.dialog.typeRandom')
          }}</SelectItem>
          <SelectItem value="pipeline">{{
            $t('dashboard.commands.dialog.typePipeline')
          }}</SelectItem>
          <SelectItem value="code">{{
            $t('dashboard.commands.dialog.typeCode')
          }}</SelectItem>
        </SelectContent>
      </Select>
    </Field>
    <TemplateField
      v-if="kind === 'template'"
      v-model="reply"
      context="command"
      :maxlength="500"
      :label="$t('dashboard.commands.editor.reply')"
    />
    <RandomReplyList v-else-if="kind === 'random'" v-model="replies" />
    <CodeScriptPicker v-else-if="kind === 'code'" v-model="codeScriptId" />
    <Field v-else>
      <FieldLabel>{{ $t('dashboard.commands.editor.pipeline') }}</FieldLabel>
      <TargetPicker
        v-model:target="pipelineTarget"
        :kinds="['pipeline']"
        :label="$t('dashboard.commands.editor.pipeline')"
      />
    </Field>
    <TriggerFields
      v-model:prefix-mode="prefixMode"
      v-model:custom-prefix="customPrefix"
      v-model:match-mode="matchMode"
      v-model:match-pattern="matchPattern"
    />
  </FormDialog>
</template>
