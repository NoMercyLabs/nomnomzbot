<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform default role of every action. A channel without its own setting follows this value. Editing
// one opens a dialog: pick a role (or go back to the shipped default), read the counted blast radius, Apply.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  previewActionDefault,
  useAdminActionDefaults,
  useSaveActionDefault,
  type ActionDefault,
} from '@/api/adminPlatformDefaults';
import FormDialog from '@/components/layout/FormDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { usePlatformDefaultListError } from '@/composables/usePlatformDefaultListError';
import { usePlatformDefaultPreview } from '@/composables/usePlatformDefaultPreview';

import PlatformDefaultPreview from './PlatformDefaultPreview.vue';

const SHIPPED = 'shipped';
const LEVEL_COUNT = 8;

const { t } = useI18n();
const { can } = usePlatformCan();

const actions = useAdminActionDefaults();
const save = useSaveActionDefault();

const editing = ref<ActionDefault | null>(null);
const choice = ref<string>(SHIPPED);
const failure = ref<string>();

const levelName = (level: number | string): string =>
  t(`dashboard.admin.platformDefaults.levels.${Number(level)}`);

// A chosen value is a rung number, or the word for "back to the shipped default" (sent as a null level).
const chosenLevel = (): number | null =>
  choice.value === SHIPPED
    ? null
    : Number(choice.value);

const preview = usePlatformDefaultPreview(() =>
  previewActionDefault(editing.value?.actionKey ?? '', chosenLevel()),
);

const rungs = computed<number[]>(() => {
  const floor = Number(editing.value?.floorLevel ?? 0);

  return Array.from(Array(LEVEL_COUNT).keys()).filter((rung): boolean => rung >= floor);
});

const shippedOption = computed<string>(() =>
  t('dashboard.admin.platformDefaults.actions.dialog.shipped', {
    role: levelName(editing.value?.shippedDefaultLevel ?? 0),
  }),
);

const panelState = usePanelState(actions, (list: ActionDefault[]) =>
  list.length === 0);

const listError = usePlatformDefaultListError(() => actions.error.value);

const applyLabel = computed<string>(() =>
  t('dashboard.admin.platformDefaults.preview.apply', {
    channels: t(
      'dashboard.admin.platformDefaults.preview.channels',
      preview.channelsAffected.value,
    ),
  }),
);

function startEdit(row: ActionDefault): void {
  editing.value = row;
  failure.value = undefined;
  preview.reset();

  choice.value =
    row.platformDefaultLevel === null || row.platformDefaultLevel === undefined
      ? SHIPPED
      : String(Number(row.platformDefaultLevel));
}

function close(): void {
  editing.value = null;
  preview.reset();
}

function pick(value: string): void {
  choice.value = value;
  void preview.request();
}

function onPick(event: Event): void {
  pick((event.target as HTMLSelectElement).value);
}

function apply(): void {
  if (!editing.value || !preview.canApply.value) return;

  save.mutate(
    {
      actionKey: editing.value.actionKey,
      level: chosenLevel(),
      confirmedChannelsAffected: preview.channelsAffected.value,
      confirmDanger: preview.dangerConfirmed.value,
    },
    {
      onSuccess: () => {
        toast.success(t('dashboard.admin.platformDefaults.actions.saved'));
        close();
      },
      onError: (error: Error) => {
        failure.value = t('dashboard.admin.platformDefaults.actions.saveError', {
          reason: error.message,
        });

        void preview.request();
      },
    },
  );
}
</script>

<template>
  <PanelCard
    tier="lead"
    data-section="actions"
    :title="$t('dashboard.admin.platformDefaults.actions.title')"
    :description="$t('dashboard.admin.platformDefaults.actions.description')"
    :state="panelState"
    :empty-text="$t('dashboard.admin.platformDefaults.actions.empty')"
    :error-text="listError"
    @retry="actions.refetch()"
  >
    <ul class="divide-y divide-border">
      <li
        v-for="row in actions.data.value ?? []"
        :key="row.actionKey"
        :data-action="row.actionKey"
        class="flex items-center gap-4 px-4 py-3"
      >
        <div class="min-w-0 flex-1">
          <p class="flex flex-wrap items-center gap-2 text-sm font-medium">
            <code class="text-xs">{{ row.actionKey }}</code>
            <StatusBadge
              v-if="row.platformDefaultLevel !== null && row.platformDefaultLevel !== undefined"
              data-changed
              tone="live"
              :label="$t('dashboard.admin.platformDefaults.actions.changed')"
            />
          </p>
          <p v-if="row.description" class="text-sm text-muted-foreground">{{ row.description }}</p>
          <p class="flex flex-wrap gap-x-3 text-xs text-muted-foreground">
            <span>{{
              $t("dashboard.admin.platformDefaults.actions.shipped", {
                role: levelName(row.shippedDefaultLevel),
              })
            }}</span>
            <span>{{
              $t(
                "dashboard.admin.platformDefaults.actions.overrides",
                Number(row.channelOverrideCount),
              )
            }}</span>
          </p>
        </div>
        <span class="text-sm font-medium">{{ levelName(row.effectiveDefaultLevel) }}</span>
        <Button v-if="can('platform:defaults:manage')"
                variant="outline"
                size="sm"
                data-edit
                @click="startEdit(row)"
        >
          {{ $t("dashboard.admin.platformDefaults.actions.edit") }}
        </Button>
      </li>
    </ul>
  </PanelCard>

  <FormDialog
    :open="editing !== null"
    :title="
      $t('dashboard.admin.platformDefaults.actions.dialog.title', {
        action: editing?.actionKey ?? '',
      })
    "
    :description="$t('dashboard.admin.platformDefaults.actions.dialog.description')"
    :action="applyLabel"
    :pending="save.isPending.value"
    :disabled="!preview.canApply.value"
    :error="failure"
    @update:open="(open: boolean) => !open && close()"
    @submit="apply"
  >
    <label class="grid gap-1 text-xs text-muted-foreground">
      {{ $t("dashboard.admin.platformDefaults.actions.dialog.role") }}
      <select
        data-level
        :value="choice"
        class="h-9 rounded-md border border-input bg-transparent px-3 text-sm text-foreground"
        @change="onPick"
      >
        <option :value="SHIPPED">
          {{ shippedOption }}
        </option>
        <option v-for="rung in rungs" :key="rung" :value="String(rung)">
          {{ levelName(rung) }}
        </option>
      </select>
    </label>
    <PlatformDefaultPreview
      v-if="preview.pending.value || preview.failed.value || preview.preview.value"
      v-model:danger-confirmed="preview.dangerConfirmed.value"
      :preview="preview.preview.value"
      :pending="preview.pending.value"
      :failed="preview.failed.value"
      @retry="preview.request()"
    />
    <p v-else class="text-sm text-muted-foreground">
      {{ $t("dashboard.admin.platformDefaults.preview.pick") }}
    </p>
  </FormDialog>
</template>
