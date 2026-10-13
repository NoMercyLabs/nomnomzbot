<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A new command: a name and what it does. A code command is not offered: no endpoint attaches a script.
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useCreateCommand, type NewCommand } from '@/api/commands'
import { usePipelines } from '@/api/pipelines'
import FormDialog from '@/components/layout/FormDialog.vue'
import TemplateField from '@/components/layout/TemplateField.vue'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

type Kind = 'template' | 'pipeline'

const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ created: [name: string] }>()

const { t } = useI18n()
const { channelId } = useCurrentChannel()
const create = useCreateCommand(channelId)
const pipelines = usePipelines(channelId)

const name: Ref<string> = ref('')
const kind: Ref<Kind> = ref<Kind>('template')
const reply: Ref<string> = ref('')
const pipelineId: Ref<string> = ref('')
const error: Ref<string | undefined> = ref(undefined)

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return
  name.value = ''
  kind.value = 'template'
  reply.value = ''
  pipelineId.value = ''
  error.value = undefined
})

function cleaned(raw: string): string {
  return raw.trim().replace(/^!+/, '').toLowerCase()
}

function bodyOf(clean: string): NewCommand {
  return kind.value === 'template'
    ? { name: clean, tier: 'template', templateResponse: reply.value }
    : { name: clean, tier: 'pipeline', pipelineId: pipelineId.value }
}

async function submit(): Promise<void> {
  const clean: string = cleaned(name.value)
  if (clean === '') {
    error.value = t('dashboard.commands.dialog.nameRequired')
    return
  }
  error.value = undefined
  try {
    await create.mutateAsync(bodyOf(clean))
  } catch (failure) {
    error.value = failure instanceof Error ? failure.message : String(failure)
    return
  }
  open.value = false
  emit('created', clean)
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
          <SelectItem value="pipeline">{{
            $t('dashboard.commands.dialog.typePipeline')
          }}</SelectItem>
        </SelectContent>
      </Select>
    </Field>
    <TemplateField
      v-if="kind === 'template'"
      v-model="reply"
      context="command"
      :label="$t('dashboard.commands.editor.reply')"
    />
    <Field v-else>
      <FieldLabel>{{ $t('dashboard.commands.editor.pipeline') }}</FieldLabel>
      <Select v-model="pipelineId">
        <SelectTrigger :aria-label="$t('dashboard.commands.editor.pipeline')" class="w-full">
          <SelectValue :placeholder="$t('dashboard.commands.editor.pipelinePlaceholder')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem
            v-for="pipeline in pipelines.data.value ?? []"
            :key="pipeline.id"
            :value="pipeline.id"
          >
            {{ pipeline.name }}
          </SelectItem>
        </SelectContent>
      </Select>
    </Field>
  </FormDialog>
</template>
