<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
import { rungNames, rungOf, rungs as ladder } from '@/api/permissions';
import FormDialog from '@/components/layout/FormDialog.vue';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { merge } from '@/lib/array';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { usePlatformDefaultListError } from '@/composables/usePlatformDefaultListError';
import { usePlatformDefaultPreview } from '@/composables/usePlatformDefaultPreview';

import { useActionName } from '@/views/dashboard/settings/permissions/actionArea';

import PlatformDefaultPreview from './PlatformDefaultPreview.vue';

const SHIPPED = 'shipped';

const { t } = useI18n();
const { can } = usePlatformCan();
const nameOf = useActionName();

const actions = useAdminActionDefaults();
const save = useSaveActionDefault();

const editing = ref<ActionDefault | null>(null);
const choice = ref<string>(SHIPPED);
const failure = ref<string>();

// The server sends a ladder level (0, 2, 4, 6, 10, 20, 30, 40); the page words are keyed by the rung's place on the ladder.
const rungPlace = (level: number | string): number => rungNames.indexOf(rungOf(level));

const levelName = (level: number | string): string =>
  t(`dashboard.admin.platformDefaults.levels.${rungPlace(level)}`);

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

  return ladder.map((rung): number => rung.level).filter((level): boolean => level >= floor);
});

const shippedOption = computed<string>(() =>
  t('dashboard.admin.platformDefaults.actions.dialog.shipped', {
    role: levelName(editing.value?.shippedDefaultLevel ?? 0),
  }),
);

const levelChoices = computed(() =>
  merge(
    [{ value: SHIPPED, label: shippedOption.value }],
    rungs.value.map((rung) => ({ value: String(rung), label: levelName(rung) })),
  ));

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
            {{ nameOf(row) }}
            <StatusBadge
              v-if="row.platformDefaultLevel !== null && row.platformDefaultLevel !== undefined"
              data-changed
              tone="live"
              :label="$t('dashboard.admin.platformDefaults.actions.changed')"
            />
          </p>
          <p v-if="row.description && row.description !== nameOf(row)" class="text-sm text-muted-foreground">{{ row.description }}</p>
          <p data-detail class="flex flex-col text-xs text-muted-foreground">
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
        action: editing ? nameOf(editing) : '',
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
      <OptionSelect
        data-level
        :model-value="choice"
        :options="levelChoices"
        @update:model-value="pick"
      />
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
