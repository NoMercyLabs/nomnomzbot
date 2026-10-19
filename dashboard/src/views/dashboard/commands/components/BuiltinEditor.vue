<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The detail of one built-in command. It opens view-only with the catalog defaults; Customize is the
// one opt-in that makes the fields the channel's own. Each field then says it overrides the default and
// has its own way back. A draft is sent by Save; a way back is sent at once. A command with overrides
// already saved opens customized. A reserved (data-rights) command stays locked, and so does a locked reply.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useBuiltinReplies,
  useSetBuiltinReply,
  useSetBuiltinTts,
  useToggleBuiltin,
  useUpdateBuiltinSettings,
  triggerWord,
  type BuiltinCommand,
  type BuiltinReply,
} from '@/api/commands';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { braced } from '@/components/layout/templatePlaceholders';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
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

import { rungNames } from '@/api/permissions';

const props = defineProps<{ builtin: BuiltinCommand }>();

type Draft = {
  cooldown: number;
  userCooldown: number;
  level: string;
  tts: boolean;
  texts: Record<string, string>;
};

const { t } = useI18n();
const textOf = useLocalizedText();
const { channelId } = useCurrentChannel();
const toggle = useToggleBuiltin(channelId);
const updateSettings = useUpdateBuiltinSettings(channelId);
const setReply = useSetBuiltinReply(channelId);
const setTts = useSetBuiltinTts(channelId);
const replyGroups = useBuiltinReplies(channelId);

const slots = computed<BuiltinReply[]>(
  () =>
    replyGroups.data.value?.find((group) => group.builtinKey === props.builtin.replyGroup)
      ?.replies ?? [],
);

const defaults = computed(() => ({
  cooldown: Number(props.builtin.defaultCooldownSeconds),
  userCooldown: Number(props.builtin.defaultUserCooldownSeconds),
  level: props.builtin.defaultMinPermissionLevel,
}));

// What the channel has saved right now, resolved against the defaults.
const saved = computed(
  () =>
    ({
      cooldown: Number(props.builtin.cooldownSecondsOverride ?? defaults.value.cooldown),
      userCooldown: Number(
        props.builtin.userCooldownSecondsOverride ?? defaults.value.userCooldown,
      ),
      level: props.builtin.minPermissionLevelOverride ?? defaults.value.level,
      tts: props.builtin.speakWithTts,
      texts: Object.fromEntries(slots.value.map((slot) => [slot.slot, slot.effectiveTemplate])),
    }) as const satisfies Draft,
);

const hasOverrides = computed<boolean>(
  () =>
    props.builtin.cooldownSecondsOverride != null ||
    props.builtin.userCooldownSecondsOverride != null ||
    props.builtin.minPermissionLevelOverride != null ||
    Number(props.builtin.replyOverrideCount) > 0 ||
    props.builtin.speakWithTts,
);

const customized = ref(hasOverrides.value);
const draft = ref<Draft>(structuredClone(saved.value));
const savedKey = computed<string>(() => JSON.stringify(saved.value));

const locked = computed<boolean>(() => props.builtin.isReserved);
const editable = computed<boolean>(() => customized.value && !locked.value);
const floor = computed<number>(() => Math.max(0, rungNames.indexOf(defaults.value.level)));
const levels = computed<readonly string[]>(() => rungNames.slice(floor.value));

const settingsDirty = computed<boolean>(
  () =>
    draft.value.cooldown !== saved.value.cooldown ||
    draft.value.userCooldown !== saved.value.userCooldown ||
    draft.value.level !== saved.value.level,
);
const dirtySlots = computed<BuiltinReply[]>(() =>
  slots.value.filter(
    (slot) =>
      !slot.isLocked &&
      draft.value.texts[slot.slot] !== undefined &&
      draft.value.texts[slot.slot] !== saved.value.texts[slot.slot],
  ),
);
const ttsDirty = computed<boolean>(() => draft.value.tts !== saved.value.tts);
const dirtyCount = computed<number>(
  () => Number(settingsDirty.value) + dirtySlots.value.length + Number(ttsDirty.value),
);

// A refetch that changes what is saved replaces the draft only while nothing is being edited.
watch(savedKey, () => {
  if (dirtyCount.value === 0) {
    draft.value = structuredClone(saved.value);
  }
});

watch(hasOverrides, (value) => {
  if (value) {
    customized.value = true;
  }
});

type Setting = string | number | boolean;

function hint(current: Setting, standard: Setting, shown: string = String(standard)): string {
  if (!customized.value)
    return t('dashboard.commands.builtin.default', { value: shown });

  return current === standard
    ? t('dashboard.commands.builtin.usingDefault')
    : t('dashboard.commands.builtin.overrides', { value: shown });
}

// The reply box already shows the default text, so it is only named again once it is overridden.
function replyHint(slot: BuiltinReply): string {
  const current = draft.value.texts[slot.slot] ?? '';

  if (!customized.value || current === slot.defaultTemplate)
    return '';

  return t('dashboard.commands.builtin.overrides', { value: slot.defaultTemplate });
}

function slotLabel(slot: BuiltinReply): string {
  return textOf(slot.label, slot.slot);
}

const onOff = (value: boolean): string =>
  value
    ? t('dashboard.commands.builtin.on')
    : t('dashboard.commands.builtin.off');

const settingsBody = (change: Partial<Record<'cooldown' | 'userCooldown' | 'level', boolean>>) => ({
  builtinKey: props.builtin.builtinKey,
  cooldownSeconds: change.cooldown
    ? null
    : ((props.builtin.cooldownSecondsOverride as number | null) ?? null),
  userCooldownSeconds: change.userCooldown
    ? null
    : ((props.builtin.userCooldownSecondsOverride as number | null) ?? null),
  minPermissionLevel: change.level
    ? null
    : (props.builtin.minPermissionLevelOverride ?? null),
});

function setNumber(key: 'cooldown' | 'userCooldown', value: string | number): void {
  const parsed = Number(value);

  draft.value[key] = Number.isFinite(parsed)
    ? Math.max(0, Math.trunc(parsed))
    : 0;
}

function backToDefault(key: 'cooldown' | 'userCooldown' | 'level'): void {
  draft.value[key] = defaults.value[key] as never;
  updateSettings.mutate(settingsBody({ [key]: true }));
}

function backToDefaultReply(slot: BuiltinReply): void {
  draft.value.texts[slot.slot] = slot.defaultTemplate;

  setReply.mutate({
    builtinKey: props.builtin.builtinKey,
    slot: slot.slot,
    template: null,
  });
}

function backToDefaultTts(): void {
  draft.value.tts = false;
  setTts.mutate({ builtinKey: props.builtin.builtinKey, enabled: false });
}

function save(): void {
  if (settingsDirty.value) {
    updateSettings.mutate({
      builtinKey: props.builtin.builtinKey,
      cooldownSeconds:
        draft.value.cooldown === defaults.value.cooldown
          ? null
          : draft.value.cooldown,
      userCooldownSeconds:
        draft.value.userCooldown === defaults.value.userCooldown
          ? null
          : draft.value.userCooldown,
      minPermissionLevel: draft.value.level === defaults.value.level
        ? null
        : draft.value.level,
    });
  }

  for (const slot of dirtySlots.value) {
    const text = draft.value.texts[slot.slot] ?? '';

    setReply.mutate({
      builtinKey: props.builtin.builtinKey,
      slot: slot.slot,
      template: text === slot.defaultTemplate
        ? null
        : text,
    });
  }

  if (ttsDirty.value) {
    setTts.mutate({ builtinKey: props.builtin.builtinKey, enabled: draft.value.tts });
  }
}

function discard(): void {
  draft.value = structuredClone(saved.value);
}

const pending = computed<boolean>(
  () => updateSettings.isPending.value || setReply.isPending.value || setTts.isPending.value,
);
const unsaved = computed<string>(() =>
  dirtyCount.value > 0
    ? t('dashboard.commands.builtin.unsaved', dirtyCount.value)
    : '',
);
</script>

<template>
  <PanelCard
    data-builtin-editor
    :title="`!${triggerWord(builtin)}`"
    :description="$t('dashboard.commands.list.kindBuiltin')"
  >
    <div class="grid gap-5">
      <RowGroup>
        <SettingRow
          :title="$t('dashboard.commands.builtin.enabled')"
          :disabled="locked"
          :model-value="builtin.isEnabled"
          @update:model-value="toggle.mutate({ builtinKey: builtin.builtinKey, enabled: $event })"
        />
      </RowGroup>

      <p class="min-h-5 text-sm text-muted-foreground">
        {{
          locked
            ? $t("dashboard.commands.builtin.reserved")
            : $t("dashboard.commands.builtin.customizeHelp")
        }}
      </p>

      <Field v-for="slot in slots" :key="slot.slot">
        <TemplateField
          v-model="draft.texts[slot.slot]"
          context="command"
          :maxlength="500"
          :label="slotLabel(slot)"
          :local-names="slot.variables.map((variable) => variable.name)"
          :disabled="!editable || slot.isLocked"
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
        <div
          v-if="slot.isLocked || replyHint(slot) !== '' || (slot.isOverridden && editable)"
          class="flex min-h-9 items-center justify-between gap-2"
        >
          <span class="text-sm text-muted-foreground">
            {{ slot.isLocked ? $t("dashboard.commands.builtin.replyLocked") : replyHint(slot) }}
          </span>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            :class="slot.isOverridden && editable && !slot.isLocked ? '' : 'invisible'"
            :aria-label="$t('dashboard.commands.builtin.backTo', { field: slotLabel(slot) })"
            @click="backToDefaultReply(slot)"
          >
            {{ $t("dashboard.commands.builtin.back") }}
          </Button>
        </div>
      </Field>

      <Field>
        <FieldLabel>{{ $t("dashboard.commands.builtin.role") }}</FieldLabel>
        <Select v-model="draft.level" :disabled="!editable">
          <SelectTrigger :aria-label="$t('dashboard.commands.builtin.role')" class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="level in levels" :key="level" :value="level">
              {{ $t(`dashboard.commands.roles.${level}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
        <div class="flex min-h-9 items-center justify-between gap-2">
          <span class="text-sm text-muted-foreground">{{ hint(draft.level, defaults.level) }}</span>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            :class="builtin.minPermissionLevelOverride != null && editable ? '' : 'invisible'"
            :aria-label="
              $t('dashboard.commands.builtin.backTo', {
                field: $t('dashboard.commands.builtin.role'),
              })
            "
            @click="backToDefault('level')"
          >
            {{ $t("dashboard.commands.builtin.back") }}
          </Button>
        </div>
      </Field>

      <Field>
        <FieldLabel>{{ $t("dashboard.commands.builtin.globalCooldown") }}</FieldLabel>
        <Input
          type="number"
          min="0"
          step="1"
          :disabled="!editable"
          :aria-label="$t('dashboard.commands.builtin.globalCooldown')"
          :model-value="draft.cooldown"
          @update:model-value="setNumber('cooldown', $event)"
        />
        <div class="flex min-h-9 items-center justify-between gap-2">
          <span class="text-sm text-muted-foreground">{{
            hint(draft.cooldown, defaults.cooldown)
          }}</span>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            :class="builtin.cooldownSecondsOverride != null && editable ? '' : 'invisible'"
            :aria-label="
              $t('dashboard.commands.builtin.backTo', {
                field: $t('dashboard.commands.builtin.globalCooldown'),
              })
            "
            @click="backToDefault('cooldown')"
          >
            {{ $t("dashboard.commands.builtin.back") }}
          </Button>
        </div>
      </Field>

      <Field>
        <FieldLabel>{{ $t("dashboard.commands.builtin.userCooldown") }}</FieldLabel>
        <Input
          type="number"
          min="0"
          step="1"
          :disabled="!editable"
          :aria-label="$t('dashboard.commands.builtin.userCooldown')"
          :model-value="draft.userCooldown"
          @update:model-value="setNumber('userCooldown', $event)"
        />
        <div class="flex min-h-9 items-center justify-between gap-2">
          <span class="text-sm text-muted-foreground">{{
            hint(draft.userCooldown, defaults.userCooldown)
          }}</span>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            :class="builtin.userCooldownSecondsOverride != null && editable ? '' : 'invisible'"
            :aria-label="
              $t('dashboard.commands.builtin.backTo', {
                field: $t('dashboard.commands.builtin.userCooldown'),
              })
            "
            @click="backToDefault('userCooldown')"
          >
            {{ $t("dashboard.commands.builtin.back") }}
          </Button>
        </div>
      </Field>

      <Field>
        <div class="flex min-h-9 items-center justify-between gap-3">
          <FieldLabel>{{ $t("dashboard.commands.builtin.speak") }}</FieldLabel>
          <Switch
            v-model="draft.tts"
            :disabled="!editable"
            :aria-label="$t('dashboard.commands.builtin.speak')"
          />
        </div>
        <div class="flex min-h-9 items-center justify-between gap-2">
          <span class="text-sm text-muted-foreground">{{
            hint(draft.tts, false, onOff(false))
          }}</span>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            :class="builtin.speakWithTts && editable ? '' : 'invisible'"
            :aria-label="
              $t('dashboard.commands.builtin.backTo', {
                field: $t('dashboard.commands.builtin.speak'),
              })
            "
            @click="backToDefaultTts"
          >
            {{ $t("dashboard.commands.builtin.back") }}
          </Button>
        </div>
      </Field>
    </div>
    <template #footer>
      <span class="mr-auto text-sm text-muted-foreground">{{ unsaved }}</span>
      <Button
        v-if="!customized"
        type="button"
        variant="outline"
        size="sm"
        :disabled="locked"
        @click="customized = true"
      >
        {{ $t("dashboard.commands.builtin.customize") }}
      </Button>
      <template v-else>
        <Button
          type="button"
          variant="outline"
          size="sm"
          :disabled="dirtyCount === 0"
          @click="discard"
        >
          {{ $t("dashboard.commands.builtin.discard") }}
        </Button>
        <Button
          type="button"
          size="sm"
          :disabled="dirtyCount === 0 || pending || locked"
          @click="save"
        >
          {{ $t("dashboard.commands.builtin.save") }}
        </Button>
      </template>
    </template>
  </PanelCard>
</template>
