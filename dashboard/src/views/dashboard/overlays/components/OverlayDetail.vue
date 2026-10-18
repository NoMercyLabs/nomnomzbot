<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The head of one overlay: name and badges, the On/Off switch, the one primary action (copy the
// browser source address), Health, what it reacts to, and the module facts.
// The address holds a secret token. Copy sends the full address to the clipboard; the page only ever shows it cut.
import Check from '~icons/nnz/check';
import Code from '~icons/nnz/code';
import Copy from '~icons/nnz/file-copy';
import Play from '~icons/nnz/play';
import { useTimeoutFn } from '@vueuse/core';
import { computed, ref } from 'vue';

import type { JsonObject } from '@/lib/json';
import { useI18n } from 'vue-i18n';

import {
  useSetWidgetEnabled,
  useTestWidgetEvent,
  type Widget,
  type WidgetTestResult,
} from '@/api/widgets';
import BackLink from '@/components/layout/BackLink.vue';
import CodeEditor from '@/components/layout/CodeEditor.vue';
import DetailPane from '@/components/layout/DetailPane.vue';
import InlineFlow from '@/components/layout/InlineFlow.vue';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
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
import { numberOrNull } from '@/lib/number';

import OverlaySettings from './OverlaySettings.vue';
import { maskedAddress, overlayKind, plainEvent, type OverlayKind } from './overlayKind';
import { useOverlayEditor } from './useOverlayEditor';

const COPIED_MS = 2000;

const props = defineProps<{ widget: Widget }>();

defineEmits<{ back: [] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const setEnabled = useSetWidgetEnabled(channelId);
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
const attachLine = computed<HealthLine>(() => {
  const base: string = 'dashboard.overlays.detail.health';

  if (!props.widget.isEnabled)
    return {
      tone: 'off',
      title: t(`${base}.switchedOff`),
      note: t(`${base}.switchedOffNote`),
    };

  if (props.widget.isAttached)
    return {
      tone: 'ok',
      title: t(`${base}.attached`),
      note: t(`${base}.attachedNote`),
    };

  return {
    tone: 'off',
    title: t(`${base}.detached`),
    note: t(`${base}.detachedNote`),
  };
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

// The preview cannot render the overlay yet; it names the size the overlay is set to when it has one.
const size = computed<string | null>(() => {
  const width = numberOrNull(props.widget.settings?.width as number | string | undefined);
  const height = numberOrNull(props.widget.settings?.height as number | string | undefined);

  return width === null || height === null
    ? null
    : `${width} × ${height}`;
});

const module = computed<KeyValueItem[]>(() => {
  const base: string = 'dashboard.overlays.detail.module';
  const address: string | null = props.widget.overlayUrl
    ? maskedAddress(props.widget.overlayUrl)
    : null;

  return [
    { label: t(`${base}.what`), value: props.widget.description || t(`${base}.noDescription`) },
    { label: t(`${base}.framework`), value: props.widget.framework },
    { label: t(`${base}.triggers`), value: events.value.length },
    {
      label: t(`${base}.address`),
      value:
        address === null
          ? t(`${base}.noAddress`)
          : t(`${base}.addressNote`, { path: address }),
    },
  ];
});
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
        <span
          class="flex size-12 flex-none items-center justify-center rounded-lg bg-muted text-muted-foreground"
        >
          <component :is="kind.icon" v-if="kind.icon" class="size-5" aria-hidden="true" />
        </span>
        <div class="grid min-w-0 gap-1">
          <h2 class="truncate text-lg font-semibold">{{ widget.name }}</h2>
          <div class="flex flex-wrap items-center gap-2">
            <Badge variant="outline">{{ $t(`dashboard.overlays.kinds.${kind.key}`) }}</Badge>
            <Badge v-if="builtIn" variant="outline">{{
              $t("dashboard.overlays.detail.builtIn")
            }}</Badge>
            <Badge v-if="widget.isCustomized" variant="outline">{{
              $t("dashboard.overlays.detail.edited")
            }}</Badge>
            <span class="text-sm text-muted-foreground">{{ widget.framework }}</span>
          </div>
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

    <!-- The mockup's two columns (12-overlays.html:355-367): a grid child does not shrink, so no panel
         scrolls inside itself. -->
    <div class="grid flex-none items-start gap-4 xl:grid-cols-2">
      <div class="grid gap-4">
        <PanelCard tier="lead" pad :title="$t('dashboard.overlays.detail.health.title')">
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

        <PanelCard tier="quiet" pad :title="$t('dashboard.overlays.detail.preview.title')">
          <div data-slot="overlay-preview" class="flex flex-wrap items-center gap-2 text-sm">
            <p class="min-w-0 flex-1 text-muted-foreground">
              {{ $t("dashboard.overlays.detail.preview.unavailable") }}
            </p>
            <Badge v-if="size !== null" variant="outline" class="tabular-nums">{{ size }}</Badge>
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
              :detail="plainEvent(key) === key ? undefined : key"
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

      <div class="grid gap-4">
        <OverlaySettings :widget="widget" />
        <PanelCard tier="quiet" pad :title="$t('dashboard.overlays.detail.module.title')">
          <KeyValueList :items="module" />
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
</template>
