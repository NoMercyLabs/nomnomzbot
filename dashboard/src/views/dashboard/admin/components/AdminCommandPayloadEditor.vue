<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// A command definition's draft with the fields of the channel's built-in editor: the reply wording, who can use
// it, the two cooldowns and reading it aloud. The payload is the overrides blob a channel starts from, so a field
// the payload leaves out inherits the catalog default, shown on the field. Save keeps the whole payload as a new
// version and is the one primary action; Edit JSON is the raw fallback.
import { computed, ref, type ComputedRef, type Ref, type WritableComputedRef } from 'vue';
import { useI18n } from 'vue-i18n';

import { useBuiltinReplies, useBuiltins, type BuiltinReply } from '@/api/commands';
import { rungNames } from '@/api/permissions';
import DurationInput from '@/components/layout/DurationInput.vue';
import { formatClock } from '@/components/layout/formatCountdown';
import { braced } from '@/components/layout/templatePlaceholders';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogFooter,
} from '@/components/ui/dialog';
import { Field, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useLocalizedText } from '@/composables/useLocalizedText';
import { ensureWords } from '@/i18n';

import AdminCommandFieldHint from './AdminCommandFieldHint.vue';
import AdminPayloadDialogHeader from './AdminPayloadDialogHeader.vue';
import { commandWords, writeCommandPayload, type CommandPayload } from './commandPayload';

const props = defineProps<{
  title: string;
  // The key of the definition: the built-in whose catalog defaults the fields show.
  commandKey: string;
  payload: CommandPayload;
  // Keeps the payload; resolves to the message shown after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{
  close: [];
  // Hands the draft over as JSON for the raw editor.
  editJson: [payloadJson: string];
}>();

const { t } = useI18n();
const textOf = useLocalizedText();
const { channelId } = useCurrentChannel();
const builtins = useBuiltins(channelId);
const replyGroups = useBuiltinReplies(channelId);

const responses = ref<Record<string, string>>({ ...props.payload.responses });
const speak = ref(props.payload.speakWithTts);
const cooldown = ref(props.payload.cooldownSeconds);
const userCooldown = ref(props.payload.userCooldownSeconds);
const level = ref(props.payload.level);

const wordsReady = ref(false);
const message = ref('');
const saving = ref(false);

void ensureWords(commandWords).then(() => {
  wordsReady.value = true;
});

const builtin = computed(() =>
  builtins.data.value?.find((row) => row.builtinKey === props.commandKey));
const slots = computed<BuiltinReply[]>(
  () =>
    replyGroups.data.value?.find(
      (group) =>
        group.builtinKey === props.commandKey || group.commandKeys.includes(props.commandKey),
    )?.replies ?? [],
);

const defaultCooldown = computed(() => Number(builtin.value?.defaultCooldownSeconds ?? 0));
const defaultUserCooldown = computed(() => Number(builtin.value?.defaultUserCooldownSeconds ?? 0));
const cooldownLength = lengthModel(cooldown, defaultCooldown);
const userCooldownLength = lengthModel(userCooldown, defaultUserCooldown);
const clock = (seconds: number): string => formatClock(seconds);
const defaultLevel = computed(() => builtin.value?.defaultMinPermissionLevel ?? rungNames[0] ?? '');
const floor = computed<number>(() => Math.max(0, rungNames.indexOf(defaultLevel.value)));
const levels = computed<readonly string[]>(() => rungNames.slice(floor.value));
const roleLabel = (name: string): string => t(`dashboard.commands.roles.${name}`);

function hintOf(current: boolean, shown: string): string {
  return current
    ? t('dashboard.commands.builtin.overrides', { value: shown })
    : t('dashboard.commands.builtin.default', { value: shown });
}

function slotLabel(slot: BuiltinReply): string {
  return textOf(slot.label, slot.slot);
}

function replyText(slot: BuiltinReply): string {
  return responses.value[slot.slot] ?? slot.defaultTemplate;
}

// A reply equal to the catalog wording is no override, so it leaves the payload.
function setReply(slot: BuiltinReply, text: string): void {
  if (text === slot.defaultTemplate) {
    backToDefaultReply(slot);
  } else {
    responses.value = { ...responses.value, [slot.slot]: text };
  }
}

function backToDefaultReply(slot: BuiltinReply): void {
  const kept = Object.entries(responses.value).filter(([name]) => name !== slot.slot);

  responses.value = Object.fromEntries(kept);
}

// The duration field shows the override, or the catalog default while there is none. A length equal to the
// default is no override; text that is not a length (NaN) leaves the override as it was.
function lengthModel(
  override: Ref<number | null>,
  standard: ComputedRef<number>,
): WritableComputedRef<number> {
  return computed({
    get: () => override.value ?? standard.value,
    set: (value) => {
      if (!Number.isFinite(value)) return;

      override.value = value === standard.value
        ? null
        : value;
    },
  });
}

function currentJson(): string {
  return writeCommandPayload({
    responses: responses.value,
    speakWithTts: speak.value,
    cooldownSeconds: cooldown.value,
    userCooldownSeconds: userCooldown.value,
    level: level.value,
  });
}

async function save(): Promise<void> {
  saving.value = true;
  message.value = await props.keep(currentJson());
  saving.value = false;
}
</script>

<template>
  <Dialog :open="true" @update:open="emit('close')">
    <DialogContent
      data-command-payload-editor
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-xl"
    >
      <AdminPayloadDialogHeader
        :title="title"
        :description="$t('dashboard.admin.content.commandEditor.description')"
      />
      <div v-if="wordsReady" class="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-6 pb-4">
        <Field v-for="slot in slots" :key="slot.slot">
          <TemplateField
            context="command"
            :maxlength="500"
            :label="slotLabel(slot)"
            :local-names="slot.variables.map((variable) => variable.name)"
            :model-value="replyText(slot)"
            @update:model-value="setReply(slot, $event)"
          />
          <p
            v-if="slot.variables.length > 0"
            class="flex flex-wrap gap-x-2 text-xs text-muted-foreground"
          >
            <span>{{ $t("dashboard.commands.builtin.variables") }}</span>
            <span
              v-for="variable in slot.variables"
              :key="variable.name"
              class="font-mono"
              :title="variable.description.text ?? ''"
            >{{ braced(variable.name) }}</span>
          </p>
          <AdminCommandFieldHint
            :hint="hintOf(slot.slot in responses, slot.defaultTemplate)"
            :field="slotLabel(slot)"
            :overridden="slot.slot in responses"
            @back="backToDefaultReply(slot)"
          />
        </Field>

        <Field>
          <FieldLabel>{{ $t("dashboard.commands.builtin.role") }}</FieldLabel>
          <Select v-model="level">
            <SelectTrigger :aria-label="$t('dashboard.commands.builtin.role')" class="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="name in levels" :key="name" :value="name">
                {{ roleLabel(name) }}
              </SelectItem>
            </SelectContent>
          </Select>
          <AdminCommandFieldHint
            :hint="hintOf(level !== null, roleLabel(defaultLevel))"
            :field="$t('dashboard.commands.builtin.role')"
            :overridden="level !== null"
            @back="level = null"
          />
        </Field>

        <Field>
          <FieldLabel>{{ $t("dashboard.commands.builtin.globalCooldown") }}</FieldLabel>
          <DurationInput
            v-model="cooldownLength"
            data-command-cooldown
            :aria-label="$t('dashboard.commands.builtin.globalCooldown')"
          />
          <AdminCommandFieldHint
            :hint="hintOf(cooldown !== null, clock(defaultCooldown))"
            :field="$t('dashboard.commands.builtin.globalCooldown')"
            :overridden="cooldown !== null"
            @back="cooldown = null"
          />
        </Field>

        <Field>
          <FieldLabel>{{ $t("dashboard.commands.builtin.userCooldown") }}</FieldLabel>
          <DurationInput
            v-model="userCooldownLength"
            data-command-user-cooldown
            :aria-label="$t('dashboard.commands.builtin.userCooldown')"
          />
          <AdminCommandFieldHint
            :hint="hintOf(userCooldown !== null, clock(defaultUserCooldown))"
            :field="$t('dashboard.commands.builtin.userCooldown')"
            :overridden="userCooldown !== null"
            @back="userCooldown = null"
          />
        </Field>

        <Field>
          <div class="flex min-h-9 items-center justify-between gap-3">
            <FieldLabel>{{ $t("dashboard.commands.builtin.speak") }}</FieldLabel>
            <Switch v-model="speak" :aria-label="$t('dashboard.commands.builtin.speak')" />
          </div>
          <AdminCommandFieldHint
            :hint="hintOf(speak, t('dashboard.commands.builtin.off'))"
            :field="$t('dashboard.commands.builtin.speak')"
            :overridden="speak"
            @back="speak = false"
          />
        </Field>
      </div>
      <p
        v-if="message"
        data-command-message
        class="flex-none px-6 pb-4 text-sm text-muted-foreground"
        role="status"
      >
        {{ message }}
      </p>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button
          data-command-edit-json
          type="button"
          variant="outline"
          class="sm:mr-auto"
          @click="emit('editJson', currentJson())"
        >
          {{ $t("dashboard.admin.content.commandEditor.editJson") }}
        </Button>
        <Button
          data-command-save
          type="button"
          :disabled="saving"
          @click="save"
        >
          {{ $t("dashboard.admin.content.draft.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
