<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The editor of one custom command. The Enabled switch saves at once; every other field is a draft
// that Save sends as the changed fields only. A refused save keeps the draft and shows the bot's reason.
import ChevronDown from '~icons/nnz/chevron-down'
import Trash2 from '~icons/nnz/trash'
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  useToggleCommand,
  useUpdateCommand,
  type CommandChange,
  type CommandListItem,
} from '@/api/commands'
import { usePipelines } from '@/api/pipelines'
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SettingRow from '@/components/layout/SettingRow.vue'
import TemplateField from '@/components/layout/TemplateField.vue'
import { Button } from '@/components/ui/button'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

import CodeTierNote from './CodeTierNote.vue'
import { changeOf, draftOf, sameDraft, type CommandDraft } from './commandDraft'
import { permissionLevels } from './permissionLevels'
import RandomReplyList from './RandomReplyList.vue'

const props = defineProps<{ row: CommandListItem }>()
const emit = defineEmits<{ remove: [name: string] }>()

const { t } = useI18n()
const { channelId } = useCurrentChannel()
const update = useUpdateCommand(channelId)
const toggle = useToggleCommand(channelId)
const pipelines = usePipelines(channelId)

const draft: Ref<CommandDraft> = ref<CommandDraft>(draftOf(props.row))
// What the form was last loaded or saved with. A row that changes under an edited form never overwrites the edits.
const baseline: Ref<CommandDraft> = ref<CommandDraft>(draftOf(props.row))
const failure: Ref<string | null> = ref(null)
const confirmOpen: Ref<boolean> = ref(false)

watch(
  () => props.row,
  (row: CommandListItem): void => {
    if (!sameDraft(draft.value, baseline.value)) return
    draft.value = draftOf(row)
    baseline.value = draftOf(row)
  },
)

const change: ComputedRef<CommandChange> = computed<CommandChange>(() =>
  changeOf(draft.value, props.row),
)
const dirtyCount: ComputedRef<number> = computed<number>(() => Object.keys(change.value).length)
const unsaved: ComputedRef<string> = computed<string>(() =>
  dirtyCount.value === 0
    ? ''
    : t('dashboard.commands.editor.unsaved', { count: dirtyCount.value }, dirtyCount.value),
)
const random: ComputedRef<boolean> = computed<boolean>(() => draft.value.replies.length > 0)

function setRandom(on: boolean): void {
  draft.value.replies = on ? [''] : []
}

function setNumber(key: 'cooldownSeconds' | 'userCooldownSeconds', value: string | number): void {
  draft.value[key] = Number(value)
}

async function save(): Promise<void> {
  if (dirtyCount.value === 0) return
  failure.value = null
  try {
    await update.mutateAsync({ commandName: props.row.name, change: change.value })
    baseline.value = { ...draft.value, replies: [...draft.value.replies] }
  } catch (error) {
    failure.value = error instanceof Error ? error.message : String(error)
  }
}

function discard(): void {
  failure.value = null
  draft.value = draftOf(props.row)
  baseline.value = draftOf(props.row)
}

function remove(): void {
  confirmOpen.value = false
  emit('remove', props.row.name)
}
</script>

<template>
  <PanelCard data-command-editor tier="lead" pad :title="`!${row.name}`">
    <template #actions>
      <Button type="button" variant="destructive" size="sm" @click="confirmOpen = true">
        <Trash2 aria-hidden="true" />
        {{ $t('dashboard.commands.editor.delete') }}
      </Button>
    </template>
    <div class="grid gap-5">
      <RowGroup>
        <SettingRow
          :title="$t('dashboard.commands.editor.enabled')"
          :description="$t('dashboard.commands.editor.enabledHelp')"
          :model-value="row.isEnabled"
          @update:model-value="toggle.mutate({ commandName: row.name, isEnabled: $event })"
        />
      </RowGroup>

      <template v-if="row.tier === 'template'">
        <TemplateField
          v-if="!random"
          v-model="draft.templateResponse"
          context="command"
          :label="$t('dashboard.commands.editor.reply')"
          :placeholder="$t('dashboard.commands.editor.replyPlaceholder')"
        />
        <RandomReplyList v-else v-model="draft.replies" />
        <RowGroup>
          <SettingRow
            :title="$t('dashboard.commands.editor.random')"
            :description="$t('dashboard.commands.editor.randomHelp')"
            :model-value="random"
            @update:model-value="setRandom"
          />
        </RowGroup>
      </template>
      <Field v-else-if="row.tier === 'pipeline'">
        <FieldLabel>{{ $t('dashboard.commands.editor.pipeline') }}</FieldLabel>
        <Select v-model="draft.pipelineId">
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
        <FieldDescription v-if="draft.pipelineId === ''">{{
          $t('dashboard.commands.editor.noPipeline')
        }}</FieldDescription>
      </Field>
      <CodeTierNote v-else />

      <RowGroup>
        <SettingRow
          v-model="draft.speakWithTts"
          :title="$t('dashboard.commands.editor.speak')"
          :description="$t('dashboard.commands.editor.speakHelp')"
        />
      </RowGroup>

      <Field>
        <FieldLabel>{{ $t('dashboard.commands.editor.role') }}</FieldLabel>
        <Select v-model="draft.minPermissionLevel">
          <SelectTrigger :aria-label="$t('dashboard.commands.editor.role')" class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="level in permissionLevels" :key="level" :value="level">
              {{ $t(`dashboard.commands.roles.${level}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>

      <Field>
        <FieldLabel for="command-platforms">{{
          $t('dashboard.commands.editor.platforms')
        }}</FieldLabel>
        <Input
          id="command-platforms"
          disabled
          :model-value="$t('dashboard.commands.editor.platformsOnly')"
        />
      </Field>

      <Collapsible>
        <CollapsibleTrigger as-child>
          <Button type="button" variant="ghost" size="sm" class="-ml-2">
            <ChevronDown aria-hidden="true" />
            {{ $t('dashboard.commands.editor.moreOptions') }}
          </Button>
        </CollapsibleTrigger>
        <CollapsibleContent force-mount class="grid gap-4 pt-3 data-[state=closed]:hidden">
          <Field>
            <FieldLabel for="command-chat-cooldown">{{
              $t('dashboard.commands.editor.chatCooldown')
            }}</FieldLabel>
            <Input
              id="command-chat-cooldown"
              type="number"
              min="0"
              step="1"
              :aria-label="$t('dashboard.commands.editor.chatCooldown')"
              :model-value="draft.cooldownSeconds"
              @update:model-value="setNumber('cooldownSeconds', $event)"
            />
            <FieldDescription>{{
              $t('dashboard.commands.editor.chatCooldownHelp')
            }}</FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="command-person-cooldown">{{
              $t('dashboard.commands.editor.personCooldown')
            }}</FieldLabel>
            <Input
              id="command-person-cooldown"
              type="number"
              min="0"
              step="1"
              :aria-label="$t('dashboard.commands.editor.personCooldown')"
              :model-value="draft.userCooldownSeconds"
              @update:model-value="setNumber('userCooldownSeconds', $event)"
            />
            <FieldDescription>{{
              $t('dashboard.commands.editor.personCooldownHelp')
            }}</FieldDescription>
          </Field>
        </CollapsibleContent>
      </Collapsible>

      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
    <template #footer>
      <span data-unsaved class="mr-auto text-sm text-muted-foreground">{{ unsaved }}</span>
      <Button type="button" variant="outline" size="sm" @click="discard">{{
        $t('dashboard.commands.editor.discard')
      }}</Button>
      <Button
        type="button"
        size="sm"
        :disabled="dirtyCount === 0 || update.isPending.value"
        @click="save"
      >
        {{ $t('dashboard.commands.editor.save') }}
      </Button>
    </template>
    <ConfirmDialog
      v-model:open="confirmOpen"
      :title="$t('dashboard.commands.editor.confirmTitle', { name: row.name })"
      :description="$t('dashboard.commands.editor.confirmBody')"
      :action="$t('dashboard.commands.editor.confirmAction')"
      @confirm="remove"
    />
  </PanelCard>
</template>
