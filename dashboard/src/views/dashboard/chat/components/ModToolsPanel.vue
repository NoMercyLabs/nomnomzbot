<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useChannelAccess } from '@/api/access';
import {
  useChatSettings,
  useClearChat,
  useMassBanRaiders,
  useRaidAssessments,
  useSetChatSettings,
  type ChatSettings,
} from '@/api/modTools';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Switch } from '@/components/ui/switch';
import { errorText } from '@/lib/errors';

const props = defineProps<{ channelId: string | null; channelName: string }>();

const { t } = useI18n();
const access = useChannelAccess(() => props.channelId);
const canRead = computed(() => access.can('moderation:chat:settings:read'));
const canWrite = computed(() => access.can('moderation:chat:settings:write'));
const canReadRaids = computed(() => access.can('moderation:queue:read'));
const canBan = computed(() => access.can('moderation:ban'));
const canClear = computed(() => access.can('moderation:delete_message'));
const settings = useChatSettings(() =>
  canRead.value
    ? props.channelId
    : null);
const save = useSetChatSettings(() => props.channelId);
const raids = useRaidAssessments(() =>
  canReadRaids.value
    ? props.channelId
    : null);
const ban = useMassBanRaiders(() => props.channelId);
const clear = useClearChat(() => props.channelId);

// The plain switches first, then the ones that carry a number.
const modeKeys = [
  'emotesOnly', 'subscriberOnly', 'uniqueChatMode',
  'slowMode', 'followersOnly', 'nonModeratorChatDelay',
] as const satisfies (keyof ChatSettings)[];

type Mode = (typeof modeKeys)[number];
const lengths = [
  {
    mode: 'slowMode',
    field: 'slowModeDelay',
    value: 30,
  },
  {
    mode: 'followersOnly',
    field: 'followersOnlyDuration',
    value: 10,
  },
  {
    mode: 'nonModeratorChatDelay',
    field: 'nonModeratorChatDelayDuration',
    value: 2,
  },
] as const satisfies {
  mode: Mode;
  field: keyof ChatSettings;
  value: number;
}[];

const hostile = computed(() => raids.data.value?.filter((raid) =>
  raid.verdict === 'hostile' && raid.isNetworkBlocked && !raid.isBanned) ?? []);
const targets = computed(() => [...new Set(hostile.value.map((raid) => raid.raiderTwitchUserId))]);
const banDisabled = computed(() =>
  targets.value.length === 0 ||
  raids.isPending.value ||
  raids.isError.value ||
  ban.isPending.value);

type DurationMode = (typeof lengths)[number]['mode'];

// The length a mode's hint names: its own setting field from `lengths`, or the chat delay for a mode without one.
function lengthOf(mode: Mode, current: ChatSettings): ChatSettings[(typeof lengths)[number]['field']] {
  const field = lengths.find((item) => item.mode === mode)?.field ?? 'nonModeratorChatDelayDuration';

  return current[field];
}

const rangeError = ref<DurationMode | null>(null);

function inRange(mode: DurationMode, value: number): boolean {
  if (mode === 'slowMode')
    return value >= 3 && value <= 120;

  if (mode === 'followersOnly')
    return value >= 0 && value <= 129600;

  return [2, 4, 6].includes(value);
}

const dialog = ref<'ban' | 'clear' | null>(null);
const failed = ref(false);

function toggle(mode: Mode, value: boolean): void {
  if (!canWrite.value || !settings.data.value) return;

  const next = { ...settings.data.value, [mode]: value };
  const length = lengths.find((item) => item.mode === mode);

  if (length && value && Number(next[length.field]) < (mode === 'followersOnly'
    ? 0
    : 1)) {
    next[length.field] = length.value;
  }

  void saveSettings(next);
}

// A refused save rolls the cache back to the saved value; the toast carries the bot's reason.
async function saveSettings(next: ChatSettings): Promise<boolean> {
  try {
    await save.mutateAsync(next);

    return true;
  } catch (error) {
    toast.error(error instanceof Error
      ? errorText(error, t)
      : t('dashboard.chat.modTools.saveFailed'));

    return false;
  }
}

async function setDuration(mode: DurationMode, event: Event): Promise<void> {
  if (!canWrite.value || !settings.data.value) return;

  const input = event.target;

  if (!(input instanceof HTMLInputElement)) return;

  const field = lengths.find((item) => item.mode === mode)?.field;

  if (!field) return;

  const value = Number(input.value);
  const valid = Number.isInteger(value) && inRange(mode, value);

  rangeError.value = valid
    ? null
    : mode;

  if (!valid) return;

  const saved = await saveSettings({ ...settings.data.value, [field]: value });

  if (!saved && settings.data.value) {
    input.value = String(settings.data.value[field]);
  }
}

function closeDialog(open: boolean): void {
  if (!open) {
    dialog.value = null;
  }
}

async function confirm(): Promise<void> {
  const action = dialog.value;

  if (!action) return;

  try {
    if (action === 'ban') {
      if (!canBan.value || targets.value.length === 0) return;

      await ban.mutateAsync(targets.value);
      await raids.refetch();
    } else {
      if (!canClear.value) return;

      await clear.mutateAsync();
    }

    dialog.value = null;
    failed.value = false;
  } catch {
    dialog.value = null;
    failed.value = true;
  }
}
</script>

<template>
  <PanelCard class="h-full" pad :title="$t('dashboard.chat.modTools.title')">
    <template #actions>
      <span class="text-sm text-muted-foreground">{{ $t('dashboard.chat.modTools.for', { name: channelName }) }}</span>
    </template>
    <div class="grid gap-6">
      <p v-if="failed" role="alert" class="text-sm text-destructive">{{ $t('dashboard.chat.modTools.failed') }}</p>
      <section v-if="canRead" class="grid gap-3">
        <h3 class="text-sm font-semibold">{{ $t('dashboard.chat.modTools.modes') }}</h3>
        <p v-if="settings.isPending.value" class="text-sm text-muted-foreground">
          {{ $t('dashboard.chat.modTools.loading') }}
        </p>
        <p v-else-if="settings.isError.value" role="alert" class="text-sm text-destructive">
          {{ $t('dashboard.chat.modTools.loadFailed') }}
        </p>
        <template v-else-if="settings.data.value">
          <!-- One row per mode: name and hint at the left; the number and the switch in fixed columns at the right,
               so every switch lines up (owner 2026-10-14: "sort these so they are visually better aligned"). -->
          <div
            v-for="mode in modeKeys"
            :key="mode"
            class="grid grid-cols-[minmax(0,1fr)_4.5rem_auto] items-center gap-x-3 gap-y-1"
          >
            <div class="min-w-0">
              <label :for="`mode-${mode}`" class="text-sm font-medium">
                {{ $t(`dashboard.chat.modTools.mode.${mode}`) }}
              </label>
              <p class="text-xs text-muted-foreground">
                {{ $t(`dashboard.chat.modTools.hint.${mode}`, { value: lengthOf(mode, settings.data.value) }) }}
              </p>
            </div>
            <Input
              v-if="mode === 'slowMode' || mode === 'followersOnly' || mode === 'nonModeratorChatDelay'"
              :data-duration="mode"
              type="number"
              :min="mode === 'followersOnly' ? 0 : 1"
              class="col-start-2 w-full"
              :aria-invalid="rangeError === mode"
              :aria-label="$t(`dashboard.chat.modTools.duration.${mode}`)"
              :model-value="lengthOf(mode, settings.data.value)"
              :disabled="!canWrite || save.isPending.value"
              @change="setDuration(mode, $event)"
            />
            <Switch
              :id="`mode-${mode}`"
              :data-mode="mode"
              class="col-start-3"
              :model-value="settings.data.value[mode]"
              :disabled="!canWrite || save.isPending.value"
              @update:model-value="toggle(mode, $event)"
            />
            <p
              v-if="rangeError === mode"
              role="alert"
              :data-duration-error="mode"
              class="col-span-3 text-xs text-destructive"
            >
              {{ $t(`dashboard.chat.modTools.range.${mode}`) }}
            </p>
          </div>
        </template>
        <p class="text-xs text-muted-foreground">{{ $t('dashboard.chat.modTools.shieldNote') }}</p>
      </section>
      <section v-if="canReadRaids" class="grid gap-2">
        <h3 class="text-sm font-semibold">{{ $t('dashboard.chat.modTools.raids') }}</h3>
        <p v-if="raids.isPending.value" class="text-sm text-muted-foreground">
          {{ $t('dashboard.chat.modTools.loadingRaids') }}
        </p>
        <p v-else-if="raids.isError.value" role="alert" class="text-sm text-destructive">
          {{ $t('dashboard.chat.modTools.raidsFailed') }}
        </p>
        <p v-else class="text-sm text-muted-foreground">
          {{ $t('dashboard.chat.modTools.raidCount', { count: targets.length }) }}
        </p>
        <Button
          v-if="canBan"
          variant="destructive"
          size="sm"
          class="justify-self-start"
          :disabled="banDisabled"
          @click="dialog = 'ban'"
        >
          {{ $t('dashboard.chat.modTools.massBan') }}
        </Button>
      </section>
      <section v-if="canClear" class="grid gap-2">
        <h3 class="text-sm font-semibold">{{ $t('dashboard.chat.modTools.cleanup') }}</h3>
        <p class="text-sm text-muted-foreground">
          {{ $t('dashboard.chat.modTools.clearHint', { name: channelName }) }}
        </p>
        <Button variant="destructive" size="sm" class="justify-self-start" @click="dialog = 'clear'">
          {{ $t('dashboard.chat.modTools.clear') }}
        </Button>
      </section>
    </div>
  </PanelCard>
  <ConfirmDialog
    :open="dialog !== null"
    :title="$t(`dashboard.chat.modTools.confirm.${dialog}.title`)"
    :description="$t(`dashboard.chat.modTools.confirm.${dialog}.description`, { count: targets.length, name: channelName })"
    :action="$t(`dashboard.chat.modTools.confirm.${dialog}.action`)"
    :pending="ban.isPending.value || clear.isPending.value"
    @update:open="closeDialog"
    @confirm="confirm"
  />
</template>
