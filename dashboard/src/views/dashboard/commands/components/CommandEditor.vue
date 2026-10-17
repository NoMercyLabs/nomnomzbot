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
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReplyModeField from '@/components/layout/ReplyModeField.vue';
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
import {
  changeOf,
  draftOf,
  sameDraft,
  triggerValid,
  type CommandDraft,
  type ReplyKind,
} from './commandDraft';
import { permissionLevels } from './permissionLevels';
import RandomReplyDialog, { type ReplySet } from './RandomReplyDialog.vue';
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

// One short line of the cooldowns that are set, so the folded group still tells what is on.
const limitsSummary = computed<string>(() => {
  const parts: string[] = [];

  if (draft.value.cooldownSeconds > 0) {
    parts.push(t('dashboard.commands.editor.limitChat', { count: draft.value.cooldownSeconds }));
  }

  if (draft.value.userCooldownSeconds > 0) {
    parts.push(t('dashboard.commands.editor.limitPerson', { count: draft.value.userCooldownSeconds }));
  }

  return parts.length === 0
    ? t('dashboard.commands.editor.limitsNone')
    : parts.join(' · ');
});
const unsaved = computed<string>(() =>
  dirtyCount.value === 0
    ? ''
    : t('dashboard.commands.editor.unsaved', { count: dirtyCount.value }, dirtyCount.value),
);
const valid = computed(() => triggerValid(draft.value));
const random = computed<boolean>(() => draft.value.kind === 'random');

// A switch changes only the kind; an empty side starts from the other one (the text seeds the
// first reply, the first reply seeds an empty text).
function setKind(kind: ReplyKind): void {
  if (kind === 'random' && draft.value.replies.length === 0) {
    draft.value.replies = [draft.value.templateResponse];
  }

  if (kind === 'text' && draft.value.templateResponse === '') {
    draft.value.templateResponse = draft.value.replies[0] ?? '';
  }

  draft.value.kind = kind;
}

const replyTypes = computed(
  () =>
    [
      { value: 'text', label: t('dashboard.commands.editor.typeText') },
      { value: 'random', label: t('dashboard.commands.editor.typeRandomReply') },
    ] as const satisfies { value: string; label: string }[],
);
const replyType = computed<string>({
  get: () => draft.value.kind,
  set: (type: string): void => {
    setKind(type === 'random'
      ? 'random'
      : 'text');
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
const repliesOpen = ref(false);
const replyCount = computed<number>(
  () => draft.value.replies.filter((line: string): boolean => line.trim() !== '').length,
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
      <LabeledSwitch
        :label="$t('dashboard.commands.editor.enabled')"
        :help="$t('dashboard.commands.editor.enabledHelp')"
        :model-value="row.isEnabled"
        @update:model-value="toggle.mutate({ commandName: row.name, isEnabled: $event })"
      />
    </template>
    <div class="grid gap-5">
      <!-- Who may trigger it comes first: it decides whether the reaction below ever runs. -->
      <div class="grid gap-4 md:grid-cols-2">
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
      </div>

      <ReplyModeField
        v-if="row.tier !== 'code'"
        v-model:mode="draft.mode"
        v-model:pipeline-id="draft.pipelineId"
        v-model:simple-kind="replyType"
        :simple-options="replyTypes"
        :new-pipeline-name="`!${row.name}`"
      >
        <!-- Text and Random reply share one grid cell; the hidden one still holds its height, so the
             switch between them never moves what sits below. -->
        <div class="grid *:[grid-area:1/1]">
          <TemplateField
            v-model="draft.templateResponse"
            context="command"
            :class="random ? 'invisible' : ''"
            :inert="random"
            :maxlength="500"
            :label="$t('dashboard.commands.editor.reply')"
            :placeholder="$t('dashboard.commands.editor.replyPlaceholder')"
          />
          <Field v-if="random" data-random-summary class="content-start">
            <FieldLabel>{{ $t("dashboard.commands.editor.randomPanel") }}</FieldLabel>
            <div class="flex items-center gap-3">
              <span class="text-sm text-muted-foreground">{{
                $t("dashboard.commands.editor.replyCount", replyCount)
              }}</span>
              <Button type="button" variant="outline" size="sm" @click="repliesOpen = true">
                {{ $t("dashboard.commands.editor.editReplies") }}
              </Button>
            </div>
          </Field>
        </div>
        <!-- Speech belongs to a text reply; a pipeline speaks through its own step. -->
        <LabeledSwitch
          v-model="draft.speakWithTts"
          :label="$t('dashboard.commands.editor.speak')"
          :help="$t('dashboard.commands.editor.speakHelp')"
        />
      </ReplyModeField>
      <CodeTierNote v-else />

      <Collapsible class="rounded-lg border border-border">
        <CollapsibleTrigger
          data-limits-trigger
          class="group flex w-full items-center justify-between gap-3 rounded-lg px-3 py-2 text-left text-sm"
        >
          <span class="font-medium">{{ $t("dashboard.commands.editor.limits") }}</span>
          <span class="ml-auto truncate text-muted-foreground">{{ limitsSummary }}</span>
          <ChevronDown
            aria-hidden="true"
            class="size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
          />
        </CollapsibleTrigger>
        <CollapsibleContent force-mount class="grid gap-4 border-t border-border p-3 data-[state=closed]:hidden">
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
      <!-- Delete sits apart at the far left, away from Save, quiet; its confirm carries the danger. -->
      <Button
        type="button"
        variant="outline"
        size="sm"
        @click="confirmOpen = true"
      >
        <Trash2 aria-hidden="true" />
        {{ $t("dashboard.commands.editor.delete") }}
      </Button>
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
  <RandomReplyDialog v-model:open="repliesOpen" v-model="draft.replies" :sets="sets" />
</template>
