<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform wording of every built-in reply slot. A channel without its own reply says this. Editing one
// opens a dialog: write the wording (or go back to the shipped wording), read the counted blast radius, Apply.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  previewBuiltinReplyDefault,
  useAdminBuiltinReplyDefaults,
  useSaveBuiltinReplyDefault,
  type BuiltinReplyDefault,
} from '@/api/adminPlatformDefaults';
import FormDialog from '@/components/layout/FormDialog.vue';
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

const replies = useAdminBuiltinReplyDefaults();
const save = useSaveBuiltinReplyDefault();

const editing = ref<BuiltinReplyDefault | null>(null);
const text = ref('');
const useShipped = ref(false);
const failure = ref<string>();

// The wording sent: null means "back to the shipped wording".
const chosenTemplate = (): string | null =>
  useShipped.value
    ? null
    : text.value;

const preview = usePlatformDefaultPreview(() =>
  previewBuiltinReplyDefault(
    editing.value?.builtinKey ?? '',
    editing.value?.slot ?? '',
    chosenTemplate(),
  ),
);

const panelState = usePanelState(replies, (list: BuiltinReplyDefault[]) =>
  list.length === 0);

const listError = usePlatformDefaultListError(() => replies.error.value);

const applyLabel = computed<string>(() =>
  t('dashboard.admin.platformDefaults.preview.apply', {
    channels: t(
      'dashboard.admin.platformDefaults.preview.channels',
      preview.channelsAffected.value,
    ),
  }),
);

const effective = (row: BuiltinReplyDefault): string =>
  row.platformTemplate ?? row.shippedTemplate ?? '';

const rowKey = (row: BuiltinReplyDefault): string => `${row.builtinKey}.${row.slot}`;

function startEdit(row: BuiltinReplyDefault): void {
  editing.value = row;
  text.value = effective(row);
  useShipped.value = false;
  failure.value = undefined;
  preview.reset();
}

function close(): void {
  editing.value = null;
  preview.reset();
}

function onType(value: string): void {
  text.value = value;
  useShipped.value = false;
  preview.schedule();
}

function chooseShipped(): void {
  useShipped.value = true;
  text.value = editing.value?.shippedTemplate ?? '';
  void preview.request();
}

function apply(): void {
  if (!editing.value || !preview.canApply.value) return;

  save.mutate(
    {
      builtinKey: editing.value.builtinKey,
      slot: editing.value.slot,
      template: chosenTemplate(),
      confirmedChannelsAffected: preview.channelsAffected.value,
    },
    {
      onSuccess: () => {
        toast.success(t('dashboard.admin.platformDefaults.replies.saved'));
        close();
      },
      onError: (error: Error) => {
        failure.value = t('dashboard.admin.platformDefaults.replies.saveError', {
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
    data-section="builtin-replies"
    :title="$t('dashboard.admin.platformDefaults.replies.title')"
    :description="$t('dashboard.admin.platformDefaults.replies.description')"
    :state="panelState"
    :empty-text="$t('dashboard.admin.platformDefaults.replies.empty')"
    :error-text="listError"
    @retry="replies.refetch()"
  >
    <ul class="divide-y divide-border">
      <li
        v-for="row in replies.data.value ?? []"
        :key="rowKey(row)"
        :data-builtin="rowKey(row)"
        class="flex items-center gap-4 px-4 py-3"
      >
        <div class="min-w-0 flex-1">
          <p class="flex flex-wrap items-center gap-2 text-sm font-medium">
            <code class="text-xs">{{ rowKey(row) }}</code>
            <StatusBadge
              v-if="row.platformTemplate"
              data-changed
              tone="live"
              :label="$t('dashboard.admin.platformDefaults.replies.changed')"
            />
          </p>
          <p class="break-words text-sm">{{ effective(row) }}</p>
          <p class="text-xs text-muted-foreground">
            {{
              $t(
                "dashboard.admin.platformDefaults.replies.overrides",
                Number(row.channelsWithOwnReply),
              )
            }}
          </p>
        </div>
        <Button v-if="can('platform:defaults:manage')"
                variant="outline"
                size="sm"
                data-edit
                @click="startEdit(row)"
        >
          {{ $t("dashboard.admin.platformDefaults.replies.edit") }}
        </Button>
      </li>
    </ul>
  </PanelCard>

  <FormDialog
    v-if="can('platform:defaults:manage')"
    :open="editing !== null"
    :title="
      $t('dashboard.admin.platformDefaults.replies.dialog.title', {
        reply: editing ? rowKey(editing) : '',
      })
    "
    :description="$t('dashboard.admin.platformDefaults.replies.dialog.description')"
    :action="applyLabel"
    :pending="save.isPending.value"
    :disabled="!preview.canApply.value"
    :error="failure"
    @update:open="(open: boolean) => !open && close()"
    @submit="apply"
  >
    <TemplateField
      :model-value="text"
      :label="$t('dashboard.admin.platformDefaults.replies.dialog.wording')"
      context="command"
      data-field="builtin-template"
      :maxlength="500"
      @update:model-value="onType"
    />
    <div class="flex items-center justify-between gap-3 text-xs text-muted-foreground">
      <span>{{
        $t("dashboard.admin.platformDefaults.replies.dialog.shippedIs", {
          text: editing?.shippedTemplate ?? "",
        })
      }}</span>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        data-use-shipped
        :disabled="useShipped"
        @click="chooseShipped"
      >
        {{ $t("dashboard.admin.platformDefaults.replies.dialog.useShipped") }}
      </Button>
    </div>
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
