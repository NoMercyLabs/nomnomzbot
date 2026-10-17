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
import ChevronDown from '~icons/nnz/chevron-down';
import Trash2 from '~icons/nnz/trash';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useCommands,
  useToggleCommand,
  useUpdateCommand,
  type CommandChange,
  type CommandListItem,
} from '@/api/commands';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReplyModeField from '@/components/layout/ReplyModeField.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
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

import CodeTierNote from './CodeTierNote.vue';
import { changeOf, draftOf, sameDraft, triggerValid, type CommandDraft } from './commandDraft';
import { permissionLevels } from './permissionLevels';
import RandomReplyPanel, { type ReplySet } from './RandomReplyPanel.vue';
import TriggerFields from './TriggerFields.vue';

const props = defineProps<{ row: CommandListItem }>();
const emit = defineEmits<{ remove: [name: string] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const update = useUpdateCommand(channelId);
const toggle = useToggleCommand(channelId);
const commands = useCommands(channelId);

const draft = ref<CommandDraft>(draftOf(props.row));
// What the form was last loaded or saved with. A row that changes under an edited form never overwrites the edits.
const baseline = ref<CommandDraft>(draftOf(props.row));
const failure = ref<string | null>(null);
const confirmOpen = ref(false);

watch(
  () => props.row,
  (row: CommandListItem): void => {
    if (!sameDraft(draft.value, baseline.value)) return;

    draft.value = draftOf(row);
    baseline.value = draftOf(row);
  },
);

const change = computed<CommandChange>(() => changeOf(draft.value, props.row));
const dirtyCount = computed<number>(() => Object.keys(change.value).length);
const unsaved = computed<string>(() =>
  dirtyCount.value === 0
    ? ''
    : t('dashboard.commands.editor.unsaved', { count: dirtyCount.value }, dirtyCount.value),
);
const valid = computed(() => triggerValid(draft.value));
const random = computed<boolean>(() => draft.value.replies.length > 0);

// Text becomes the first random reply; the first random reply becomes the text again.
function setRandom(on: boolean): void {
  if (on) {
    draft.value.replies = [draft.value.templateResponse];

    return;
  }

  draft.value.templateResponse = draft.value.replies[0] ?? '';
  draft.value.replies = [];
}

const replyTypes = computed(
  () =>
    [
      { value: 'text', label: t('dashboard.commands.editor.typeText') },
      { value: 'random', label: t('dashboard.commands.editor.typeRandomReply') },
    ] as const satisfies { value: string; label: string }[],
);
const replyType = computed<string>({
  get: () =>
    random.value
      ? 'random'
      : 'text',
  set: (type: string): void => {
    if ((type === 'random') !== random.value) {
      setRandom(type === 'random');
    }
  },
});
// The reply lists of the channel's other random-reply commands, to start a list from.
const sets = computed<ReplySet[]>(() =>
  (commands.data.value ?? [])
    .filter(
      (item: CommandListItem): boolean =>
        item.name !== props.row.name && (item.templateResponses?.length ?? 0) > 0,
    )
    .map((item: CommandListItem): ReplySet => ({
      name: item.name,
      replies: item.templateResponses ?? [],
    })),
);
const randomShown = computed<boolean>(
  () => props.row.tier !== 'code' && draft.value.mode === 'simple' && random.value,
);

function setNumber(key: 'cooldownSeconds' | 'userCooldownSeconds', value: string | number): void {
  draft.value[key] = Number(value);
}

async function save(): Promise<void> {
  if (dirtyCount.value === 0) return;

  failure.value = null;

  try {
    await update.mutateAsync({ commandName: props.row.name, change: change.value });
    baseline.value = { ...draft.value, replies: [...draft.value.replies] };
  } catch (error) {
    failure.value = errorText(error, t);
  }
}

function discard(): void {
  failure.value = null;
  draft.value = draftOf(props.row);
  baseline.value = draftOf(props.row);
}

function remove(): void {
  confirmOpen.value = false;
  emit('remove', props.row.name);
}
</script>

<template>
  <PanelCard data-command-editor tier="lead" pad :title="`!${row.name}`">
    <template #actions>
      <Button type="button" variant="outline" size="sm" @click="confirmOpen = true">
        <Trash2 aria-hidden="true" />
        {{ $t("dashboard.commands.editor.delete") }}
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

      <ReplyModeField
        v-if="row.tier !== 'code'"
        v-model:mode="draft.mode"
        v-model:pipeline-id="draft.pipelineId"
        v-model:simple-kind="replyType"
        :simple-options="replyTypes"
        :new-pipeline-name="`!${row.name}`"
      >
        <TemplateField
          v-if="!random"
          v-model="draft.templateResponse"
          context="command"
          :maxlength="500"
          :label="$t('dashboard.commands.editor.reply')"
          :placeholder="$t('dashboard.commands.editor.replyPlaceholder')"
        />
        <p v-else class="text-sm text-muted-foreground">
          {{ $t("dashboard.commands.editor.randomMoved") }}
        </p>
      </ReplyModeField>
      <CodeTierNote v-else />

      <RowGroup v-if="draft.mode !== 'full'">
        <SettingRow
          v-model="draft.speakWithTts"
          :title="$t('dashboard.commands.editor.speak')"
          :description="$t('dashboard.commands.editor.speakHelp')"
        />
      </RowGroup>

      <Field>
        <FieldLabel>{{ $t("dashboard.commands.editor.role") }}</FieldLabel>
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
          $t("dashboard.commands.editor.platforms")
        }}</FieldLabel>
        <Input
          id="command-platforms"
          disabled
          :model-value="$t('dashboard.commands.editor.platformsOnly')"
        />
      </Field>

      <Collapsible>
        <CollapsibleTrigger as-child>
          <Button type="button" variant="ghost" size="sm" class="group -ml-2">
            <ChevronDown
              class="transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
              aria-hidden="true"
            />
            {{ $t("dashboard.commands.editor.moreOptions") }}
          </Button>
        </CollapsibleTrigger>
        <CollapsibleContent force-mount class="grid gap-4 pt-3 data-[state=closed]:hidden">
          <TriggerFields
            v-model:prefix-mode="draft.prefixMode"
            v-model:custom-prefix="draft.customPrefix"
            v-model:match-mode="draft.matchMode"
            v-model:match-pattern="draft.matchPattern"
          />
          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="command-chat-cooldown">{{
                $t("dashboard.commands.editor.chatCooldown")
              }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.commands.editor.chatCooldownHelp") }}</FieldInfo>
            </div>
            <Input
              id="command-chat-cooldown"
              type="number"
              min="0"
              step="1"
              :aria-label="$t('dashboard.commands.editor.chatCooldown')"
              :model-value="draft.cooldownSeconds"
              @update:model-value="setNumber('cooldownSeconds', $event)"
            />
          </Field>
          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="command-person-cooldown">{{
                $t("dashboard.commands.editor.personCooldown")
              }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.commands.editor.personCooldownHelp") }}</FieldInfo>
            </div>
            <Input
              id="command-person-cooldown"
              type="number"
              min="0"
              step="1"
              :aria-label="$t('dashboard.commands.editor.personCooldown')"
              :model-value="draft.userCooldownSeconds"
              @update:model-value="setNumber('userCooldownSeconds', $event)"
            />
          </Field>
        </CollapsibleContent>
      </Collapsible>

      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
    <template #footer>
      <span data-unsaved class="mr-auto text-sm text-muted-foreground">{{ unsaved }}</span>
      <Button type="button" variant="outline" size="sm" @click="discard">{{
        $t("dashboard.commands.editor.discard")
      }}</Button>
      <Button
        type="button"
        size="sm"
        :disabled="dirtyCount === 0 || !valid || update.isPending.value"
        @click="save"
      >
        {{ $t("dashboard.commands.editor.save") }}
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
  <RandomReplyPanel v-if="randomShown" v-model="draft.replies" :sets="sets" />
</template>
