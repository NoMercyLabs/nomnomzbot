<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform response to every event type: on or off, the text, and whether it is spoken. A channel that
// follows the platform default says this. Editing one opens a dialog with the counted blast radius first.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  previewEventResponseDefault,
  useAdminEventResponseDefaults,
  useSaveEventResponseDefault,
  type EventResponseDefault,
  type EventResponseDefaultChange,
} from '@/api/adminPlatformDefaults';
import FormDialog from '@/components/layout/FormDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { usePlatformDefaultListError } from '@/composables/usePlatformDefaultListError';
import { usePlatformDefaultPreview } from '@/composables/usePlatformDefaultPreview';

import PlatformDefaultPreview from './PlatformDefaultPreview.vue';

const { t } = useI18n();
const { can } = usePlatformCan();

const events = useAdminEventResponseDefaults();
const save = useSaveEventResponseDefault();

const editing = ref<EventResponseDefault | null>(null);
const failure = ref<string>();

const draft = ref<EventResponseDefaultChange>({
  isEnabled: false,
  message: '',
  speakWithTts: false,
});

const preview = usePlatformDefaultPreview(() =>
  previewEventResponseDefault(editing.value?.eventType ?? '', { ...draft.value }),
);

const panelState = usePanelState(events, (list: EventResponseDefault[]) =>
  list.length === 0);

const listError = usePlatformDefaultListError(() => events.error.value);

const applyLabel = computed<string>(() =>
  t('dashboard.admin.platformDefaults.preview.apply', {
    channels: t(
      'dashboard.admin.platformDefaults.preview.channels',
      preview.channelsAffected.value,
    ),
  }),
);

const variableNames = computed<string>(() =>
  (editing.value?.variables ?? []).map((name: string) => `{${name}}`).join(' '),
);

function startEdit(row: EventResponseDefault): void {
  editing.value = row;
  failure.value = undefined;
  preview.reset();

  draft.value = {
    isEnabled: row.isEnabled,
    message: row.message ?? '',
    speakWithTts: row.speakWithTts,
  };
}

function close(): void {
  editing.value = null;
  preview.reset();
}

function change(part: Partial<EventResponseDefaultChange>): void {
  draft.value = { ...draft.value, ...part };
  preview.schedule();
}

function apply(): void {
  if (!editing.value || !preview.canApply.value) return;

  save.mutate(
    {
      eventType: editing.value.eventType,
      ...draft.value,
      confirmedChannelsAffected: preview.channelsAffected.value,
    },
    {
      onSuccess: () => {
        toast.success(t('dashboard.admin.platformDefaults.events.saved'));
        close();
      },
      onError: (error: Error) => {
        failure.value = t('dashboard.admin.platformDefaults.events.saveError', {
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
    data-section="event-responses"
    :title="$t('dashboard.admin.platformDefaults.events.title')"
    :description="$t('dashboard.admin.platformDefaults.events.description')"
    :state="panelState"
    :empty-text="$t('dashboard.admin.platformDefaults.events.empty')"
    :error-text="listError"
    @retry="events.refetch()"
  >
    <ul class="divide-y divide-border">
      <li
        v-for="row in events.data.value ?? []"
        :key="row.eventType"
        :data-event="row.eventType"
        class="flex items-center gap-4 px-4 py-3"
      >
        <div class="min-w-0 flex-1">
          <p class="flex flex-wrap items-center gap-2 text-sm font-medium">
            <code class="text-xs">{{ row.eventType }}</code>
            <StatusBadge
              v-if="row.isEnabled"
              data-enabled
              tone="live"
              :label="$t('dashboard.admin.platformDefaults.events.enabled')"
            />
          </p>
          <p v-if="row.message" class="break-words text-sm">{{ row.message }}</p>
          <p class="flex flex-wrap gap-x-3 text-xs text-muted-foreground">
            <span>{{
              $t("dashboard.admin.platformDefaults.events.following", Number(row.channelsFollowing))
            }}</span>
            <span>{{
              $t(
                "dashboard.admin.platformDefaults.events.overrides",
                Number(row.channelsWithOwnResponse),
              )
            }}</span>
          </p>
        </div>
        <Button v-if="can('platform:defaults:manage')"
                variant="outline"
                size="sm"
                data-edit
                @click="startEdit(row)"
        >
          {{ $t("dashboard.admin.platformDefaults.events.edit") }}
        </Button>
      </li>
    </ul>
  </PanelCard>

  <FormDialog
    :open="editing !== null"
    :title="
      $t('dashboard.admin.platformDefaults.events.dialog.title', {
        event: editing?.eventType ?? '',
      })
    "
    :description="$t('dashboard.admin.platformDefaults.events.dialog.description')"
    :action="applyLabel"
    :pending="save.isPending.value"
    :disabled="!preview.canApply.value"
    :error="failure"
    @update:open="(open: boolean) => !open && close()"
    @submit="apply"
  >
    <LabeledSwitch
      :model-value="draft.isEnabled"
      :label="$t('dashboard.admin.platformDefaults.events.dialog.enabled')"
      @update:model-value="(value: boolean) => change({ isEnabled: value })"
    />
    <TemplateField
      :model-value="draft.message ?? ''"
      :label="$t('dashboard.admin.platformDefaults.events.dialog.message')"
      context="eventResponse"
      :event-type="editing?.eventType"
      data-field="event-message"
      :maxlength="500"
      @update:model-value="(value: string) => change({ message: value })"
    />
    <p v-if="variableNames" class="text-xs text-muted-foreground">
      {{ $t("dashboard.admin.platformDefaults.events.dialog.variables", { names: variableNames }) }}
    </p>
    <LabeledSwitch
      :model-value="draft.speakWithTts"
      :label="$t('dashboard.admin.platformDefaults.events.dialog.speak')"
      @update:model-value="(value: boolean) => change({ speakWithTts: value })"
    />
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
