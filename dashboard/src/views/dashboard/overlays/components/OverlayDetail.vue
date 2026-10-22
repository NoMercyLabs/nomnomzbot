<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One overlay: name, badges and what it is, the On/Off switch, the one primary action (copy the
// browser source address), then its settings beside its health and the events it reacts to.
// The address holds a secret token. Copy sends the full address to the clipboard; the page never shows it.
import Check from '~icons/nnz/check';
import Code from '~icons/nnz/code';
import Copy from '~icons/nnz/file-copy';
import Play from '~icons/nnz/play';
import Trash from '~icons/nnz/trash';
import { useTimeoutFn } from '@vueuse/core';
import { computed, ref } from 'vue';

import type { JsonObject } from '@/lib/json';
import { useI18n } from 'vue-i18n';

import {
  fetchWidgetBlastRadius,
  useDeleteWidget,
  useSetWidgetEnabled,
  useTestWidgetEvent,
  type Widget,
  type WidgetTestResult,
} from '@/api/widgets';
import BackLink from '@/components/layout/BackLink.vue';
import CodeEditor from '@/components/layout/CodeEditor.vue';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import DetailPane from '@/components/layout/DetailPane.vue';
import InlineFlow from '@/components/layout/InlineFlow.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useFormattedTime } from '@/composables/useTime';
import { errorText } from '@/lib/errors';
import { blastLines } from '@/views/dashboard/scripts/components/blastLines';

import OverlaySettings from './OverlaySettings.vue';
import { overlayKind, plainEvent, type OverlayKind } from './overlayKind';
import { useOverlayEditor } from './useOverlayEditor';

const COPIED_MS = 2000;

const props = defineProps<{ widget: Widget }>();

const emit = defineEmits<{ back: []; deleted: [] }>();

function deleted(): void {
  emit('deleted');
}

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const setEnabled = useSetWidgetEnabled(channelId);
const remove = useDeleteWidget(channelId, deleted);
const editor = useOverlayEditor(() => props.widget);

const kind = computed<OverlayKind>(() => overlayKind(props.widget.eventSubscriptions));
const builtIn = computed<boolean>(() => props.widget.source === 'first_party');
// The editor's preview reads the widget's saved settings; the API types them as a free-form object.
const editorWidget = computed(() => ({
  id: props.widget.id,
  name: props.widget.name,
  settings: (props.widget.settings ?? {}) as JsonObject,
}));
const ago = useFormattedTime(() => props.widget.lastRanAt, 'relative', { live: true });

const copied = ref(false);
const { start: resetCopied } = useTimeoutFn(
  () => {
    copied.value = false;
  },
  COPIED_MS,
  { immediate: false },
);

async function copyAddress(): Promise<void> {
  if (!props.widget.overlayUrl) return;

  await navigator.clipboard.writeText(props.widget.overlayUrl);
  copied.value = true;
  resetCopied();
}

function toggle(isEnabled: boolean): void {
  setEnabled.mutate({ widgetId: props.widget.id, isEnabled });
}

// Delete reads the blast list first and asks; nothing is deleted before the owner confirms.
const confirmOpen = ref(false);
const consequences = ref<string[]>([]);
const deleteError = ref<string | undefined>(undefined);

async function askDelete(): Promise<void> {
  deleteError.value = undefined;

  try {
    const lines = blastLines(
      await fetchWidgetBlastRadius(channelId.value as string, props.widget.id),
      t,
      te,
    );

    consequences.value = lines.length > 0
      ? lines
      : [t('dashboard.overlays.delete.nothing')];

    confirmOpen.value = true;
  } catch (failure) {
    deleteError.value = errorText(failure, t);
  }
}

// The hook reports "deleted" itself (see useDeleteWidget): by the time a callback here would run, the
// refreshed list has already unmounted this pane.
async function confirmDelete(): Promise<void> {
  try {
    await remove.mutateAsync(props.widget.id);
  } catch (failure) {
    deleteError.value = errorText(failure, t);
  }

  confirmOpen.value = false;
}

const runTone = computed<StatusTone>(() =>
  props.widget.lastRuntimeError
    ? 'bad'
    : 'ok');
const runTitle = computed<string>(() =>
  t(
    props.widget.lastRuntimeError
      ? 'dashboard.overlays.detail.health.failed'
      : 'dashboard.overlays.detail.health.works',
  ),
);
const runNote = computed<string>(() => {
  if (props.widget.lastRuntimeError)
    return props.widget.lastRuntimeError;

  if (!props.widget.lastRanAt)
    return t('dashboard.overlays.detail.health.neverRan');

  return t('dashboard.overlays.detail.health.worksRan', { ago: ago.value });
});

interface HealthLine {
  tone: StatusTone;
  title: string;
  note: string;
}
const attachLine = computed(() => {
  const base: string = 'dashboard.overlays.detail.health';

  if (!props.widget.isEnabled)
    return {
      tone: 'off',
      title: t(`${base}.switchedOff`),
      note: t(`${base}.switchedOffNote`),
    } as const satisfies HealthLine;

  if (props.widget.isAttached)
    return {
      tone: 'ok',
      title: t(`${base}.attached`),
      note: t(`${base}.attachedNote`),
    } as const satisfies HealthLine;

  return {
    tone: 'off',
    title: t(`${base}.detached`),
    note: t(`${base}.detachedNote`),
  } as const satisfies HealthLine;
});

const events = computed<string[]>(() => props.widget.eventSubscriptions);

// Test sends the event type to this overlay alone, so the bot fills in its own sample; each row shows
// what happened to this overlay, in the dashboard's own words.
const testEvent = useTestWidgetEvent(channelId);
const testResults = ref<Record<string, string>>({});
const testFailures = ref<Record<string, string>>({});

function testSummary(answer: WidgetTestResult): string {
  const base: string = 'dashboard.overlays.detail.events';

  if (!answer.delivered)
    return t(`${base}.testNotListening`);

  return answer.attached
    ? t(`${base}.testSent`)
    : t(`${base}.testSentDetached`);
}

async function sendTest(eventType: string): Promise<void> {
  try {
    const answer: WidgetTestResult = await testEvent.mutateAsync({
      widgetId: props.widget.id,
      eventType,
    });

    testResults.value = { ...testResults.value, [eventType]: testSummary(answer) };
  } catch (error) {
    testFailures.value = { ...testFailures.value, [eventType]: errorText(error, t) };
    throw error;
  }
}
</script>

<template>
  <DetailPane data-overlay-detail>
    <template #header>
      <div data-page-inset class="flex flex-wrap items-center gap-3">
        <BackLink
          class="md:hidden"
          :label="$t('dashboard.overlays.back')"
          @click="$emit('back')"
        />
        <!-- An icon tile shows only with a real icon, here and in the list. -->
        <span
          v-if="kind.icon"
          class="flex size-12 flex-none items-center justify-center rounded-lg bg-muted text-muted-foreground"
        >
          <component :is="kind.icon" class="size-5" aria-hidden="true" />
        </span>
        <!-- flex-1 with a basis: a long description wraps inside the block instead of pushing it onto its own line. -->
        <div class="grid min-w-0 flex-1 basis-64 gap-1">
          <h2 class="truncate text-lg font-semibold">{{ widget.name }}</h2>
          <div v-if="kind.icon || builtIn || widget.isCustomized" class="flex flex-wrap items-center gap-2">
            <!-- The catch-all kind ("Overlay") says nothing on the Overlays page, so only a known kind shows. -->
            <Badge v-if="kind.icon" variant="outline">{{
              $t(`dashboard.overlays.kinds.${kind.key}`)
            }}</Badge>
            <Badge v-if="builtIn" variant="outline">{{
              $t("dashboard.overlays.detail.builtIn")
            }}</Badge>
            <Badge v-if="widget.isCustomized" variant="outline">{{
              $t("dashboard.overlays.detail.edited")
            }}</Badge>
          </div>
          <p v-if="widget.description" class="text-sm text-muted-foreground">
            {{ widget.description }}
          </p>
        </div>
        <div class="ml-auto flex flex-wrap items-center gap-3">
          <label
            v-if="can('widget:write')"
            class="flex items-center gap-2 text-sm text-muted-foreground"
          >
            {{
              $t(
                widget.isEnabled ? "dashboard.overlays.detail.on" : "dashboard.overlays.detail.off",
              )
            }}
            <Switch
              :model-value="widget.isEnabled"
              :aria-label="$t('dashboard.overlays.detail.switchLabel')"
              @update:model-value="toggle(Boolean($event))"
            />
          </label>
          <Badge v-else data-overlay-state variant="outline">{{
            $t(widget.isEnabled ? "dashboard.overlays.detail.on" : "dashboard.overlays.detail.off")
          }}</Badge>
          <div v-if="can('widget:write')" class="grid justify-items-end gap-1">
            <Button
              data-edit-code
              variant="outline"
              :disabled="editor.opening.value"
              @click="editor.open"
            >
              <Code aria-hidden="true" />
              {{ $t("dashboard.overlays.detail.editCode") }}
            </Button>
            <span v-if="editor.failure.value" role="alert" class="text-sm text-destructive">{{
              editor.failure.value
            }}</span>
          </div>
          <div v-if="can('widget:write')" class="grid justify-items-end gap-1">
            <Button data-overlay-delete variant="outline" @click="askDelete">
              <Trash aria-hidden="true" />
              {{ $t("dashboard.overlays.delete.button") }}
            </Button>
            <span v-if="deleteError" role="alert" class="text-sm text-destructive">{{
              deleteError
            }}</span>
          </div>
          <div class="grid justify-items-end gap-1">
            <Button :disabled="!widget.overlayUrl" @click="copyAddress">
              <component :is="copied ? Check : Copy" aria-hidden="true" />
              {{
                $t(copied ? "dashboard.overlays.detail.copied" : "dashboard.overlays.detail.copy")
              }}
            </Button>
            <span v-if="!widget.overlayUrl" class="text-sm text-muted-foreground">{{
              $t("dashboard.overlays.detail.noAddress")
            }}</span>
          </div>
        </div>
      </div>
    </template>

    <!-- Settings lead, first in reading order; health and events sit beside them. A grid child does not
         shrink, so no panel scrolls inside itself. -->
    <div class="grid flex-none items-start gap-4 xl:grid-cols-5">
      <OverlaySettings class="xl:col-span-3" :widget="widget" />

      <div class="grid gap-4 xl:col-span-2">
        <PanelCard tier="quiet" pad :title="$t('dashboard.overlays.detail.health.title')">
          <div data-slot="overlay-health" class="grid gap-3 text-sm">
            <div class="grid gap-1">
              <StatusBadge :tone="runTone" :label="runTitle" class="font-medium" />
              <p class="text-muted-foreground wrap-break-word">{{ runNote }}</p>
            </div>
            <div class="grid gap-1">
              <StatusBadge :tone="attachLine.tone" :label="attachLine.title" class="font-medium" />
              <p class="text-muted-foreground">{{ attachLine.note }}</p>
            </div>
          </div>
        </PanelCard>

        <PanelCard
          :title="
            events.length === 1
              ? $t('dashboard.overlays.detail.events.titleOne')
              : $t('dashboard.overlays.detail.events.title', { count: events.length })
          "
          :state="events.length === 0 ? 'empty' : 'ready'"
          :empty-text="$t('dashboard.overlays.detail.events.none')"
        >
          <RowGroup>
            <SummaryRow
              v-for="key in events"
              :key="key"
              :data-event="key"
              :title="plainEvent(key)"
            >
              <template #end>
                <InlineFlow
                  v-if="can('widget:write')"
                  kind="run"
                  right
                  :label="
                    $t('dashboard.overlays.detail.events.testLabel', { event: plainEvent(key) })
                  "
                  :result="testResults[key]"
                  :failure="testFailures[key]"
                  :action="() => sendTest(key)"
                >
                  <Button data-test variant="outline" size="sm">
                    <Play aria-hidden="true" />
                    {{ $t("dashboard.overlays.detail.events.test") }}
                  </Button>
                </InlineFlow>
              </template>
            </SummaryRow>
          </RowGroup>
        </PanelCard>
      </div>
    </div>
    <CodeEditor
      v-if="editor.session.value"
      :title="widget.name"
      :files="editor.session.value.project.files"
      :entry="editor.session.value.project.manifest.entry"
      :language="widget.framework"
      :sdk-types="editor.session.value.sdkTypes ?? ''"
      :sdk-types-unavailable="editor.session.value.sdkTypes === null"
      :widget="editorWidget"
      :fire-samples="editor.session.value.samples"
      :fire-samples-error="editor.session.value.samplesError"
      :event-subscriptions="widget.eventSubscriptions"
      :initial-history="editor.session.value.versions"
      :history="editor.history"
      :save="editor.save"
      :preview-action="editor.previewAction"
      @close="editor.close"
    />
  </DetailPane>

  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t('dashboard.overlays.delete.title', { name: widget.name })"
    :description="$t('dashboard.overlays.delete.description')"
    :consequences="consequences"
    :action="$t('dashboard.overlays.delete.action')"
    :pending="remove.isPending.value"
    @confirm="confirmDelete"
  />
</template>
