<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';
import Cross from '~icons/nnz/cross';

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

const props = defineProps<{ channelId: string | null; channelName: string }>();
const emit = defineEmits<{ close: [] }>();

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

const modeKeys = [
  'slowMode', 'followersOnly', 'emotesOnly', 'subscriberOnly',
  'uniqueChatMode', 'nonModeratorChatDelay',
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
      ? error.message
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
      <Button variant="ghost" size="icon" :aria-label="$t('dashboard.chat.modTools.close')" @click="emit('close')">
        <Cross class="size-4" aria-hidden="true" />
      </Button>
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
          <div v-for="mode in modeKeys" :key="mode" class="grid gap-1">
            <div class="flex items-center justify-between gap-3">
              <label :for="`mode-${mode}`" class="text-sm font-medium">
                {{ $t(`dashboard.chat.modTools.mode.${mode}`) }}
              </label>
              <Switch
                :id="`mode-${mode}`"
                :data-mode="mode"
                :model-value="settings.data.value[mode]"
                :disabled="!canWrite || save.isPending.value"
                @update:model-value="toggle(mode, $event)"
              />
            </div>
            <p class="text-xs text-muted-foreground">
              {{ $t(`dashboard.chat.modTools.hint.${mode}`, {
                value: mode === 'slowMode' ? settings.data.value.slowModeDelay
                  : mode === 'followersOnly' ? settings.data.value.followersOnlyDuration
                    : settings.data.value.nonModeratorChatDelayDuration,
              }) }}
            </p>
            <Input
              v-if="mode === 'slowMode' || mode === 'followersOnly' || mode === 'nonModeratorChatDelay'"
              :data-duration="mode"
              type="number"
              :min="mode === 'followersOnly' ? 0 : 1"
              class="w-24"
              :aria-invalid="rangeError === mode"
              :aria-label="$t(`dashboard.chat.modTools.duration.${mode}`)"
              :model-value="mode === 'slowMode' ? settings.data.value.slowModeDelay
                : mode === 'followersOnly' ? settings.data.value.followersOnlyDuration
                  : settings.data.value.nonModeratorChatDelayDuration"
              :disabled="!canWrite || save.isPending.value"
              @change="setDuration(mode, $event)"
            />
            <p v-if="rangeError === mode" role="alert" :data-duration-error="mode" class="text-xs text-destructive">
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
