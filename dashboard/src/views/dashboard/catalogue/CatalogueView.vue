<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Development only: every shared building block in each of its states (spec section 5, step 2).
import { useLocalStorage, type RemovableRef } from '@vueuse/core'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useCommunityFirstPage, type CommunityRow } from '@/api/community'
import UserName from '@/components/layout/UserName.vue'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

import BoardCell from '@/components/layout/BoardCell.vue'
import BoardGrid from '@/components/layout/BoardGrid.vue'
import ChartPanel, { type ChartRow, type ChartSeries } from '@/components/layout/ChartPanel.vue'
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue'
import FilterBar, { type FilterOption } from '@/components/layout/FilterBar.vue'
import FormDialog from '@/components/layout/FormDialog.vue'
import InlineFlow from '@/components/layout/InlineFlow.vue'
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue'
import ListDetail from '@/components/layout/ListDetail.vue'
import HelperPanel from '@/components/layout/HelperPanel.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SearchField from '@/components/layout/SearchField.vue'
import SettingRow from '@/components/layout/SettingRow.vue'
import StatStrip from '@/components/layout/StatStrip.vue'
import StatTile from '@/components/layout/StatTile.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import TemplateField from '@/components/layout/TemplateField.vue'
import Countdown from '@/components/layout/Countdown.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import VirtualList from '@/components/layout/VirtualList.vue'
import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { usePageView } from '@/composables/usePageView'
import CodeEditor from '@/components/layout/CodeEditor.vue'
import {
  fetchScriptProject,
  fetchScriptSdkTypes,
  saveScriptProject,
  useCodeScripts,
  type ScriptProject,
} from '@/api/codeScripts'
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge'

const { t } = useI18n()

const tabs: PageTab[] = [
  { value: 'blocks', label: t('dashboard.catalogue.tabs.blocks') },
  { value: 'states', label: t('dashboard.catalogue.tabs.states') },
  { value: 'layouts', label: t('dashboard.catalogue.tabs.layouts') },
]
const view = usePageView(tabs.map((tab: PageTab) => tab.value))

const kvItems: KeyValueItem[] = [
  { label: t('dashboard.catalogue.kv.region'), value: t('dashboard.catalogue.kv.regionValue') },
  {
    label: t('dashboard.catalogue.kv.uptime'),
    value: null,
    reason: t('dashboard.catalogue.kv.uptimeReason'),
  },
]

const stateOptions: FilterOption[] = ['all', 'on', 'off'].map((key: string) => ({
  value: key,
  label: t(`dashboard.catalogue.filter.${key}`),
}))
const sinceOptions: FilterOption[] = ['day', 'week', 'month', 'quarter', 'year'].map(
  (key: string) => ({
    value: key,
    label: t(`dashboard.catalogue.filter.${key}`),
  }),
)

// Demo data only: these numbers are invented for the catalogue and never shown anywhere else.
const lineDemo: ChartRow[] = [1200, 1500, 1450, 2100, 2600, 2500, 3100, 3400].map(
  (lines: number, day: number) => ({
    day: new Date(2026, 0, day + 1),
    lines,
  }),
)
const monthDemo: ChartRow[] = [
  { month: 'Sep', returning: 36, fresh: 50 },
  { month: 'Oct', returning: 43, fresh: 27 },
  { month: 'Nov', returning: 41, fresh: 41 },
  { month: 'Dec', returning: 49, fresh: 45 },
  { month: 'Jan', returning: 35, fresh: 12 },
]
const lineSeries: ChartSeries[] = [
  { key: 'lines', labelKey: 'dashboard.catalogue.chart.line.title', color: 'chart-1' },
]
const monthSeries: ChartSeries[] = [
  { key: 'returning', labelKey: 'dashboard.catalogue.chart.series.returning', color: 'chart-1' },
  { key: 'fresh', labelKey: 'dashboard.catalogue.chart.series.fresh', color: 'chart-3' },
]

const ttsOn = ref(true)
const modsOn = ref(false)
const stateFilter: RemovableRef<string> = useLocalStorage('nnz.catalogue.state', 'all')
const sinceFilter: RemovableRef<string> = useLocalStorage('nnz.catalogue.since', 'week')
const query = ref('')
const retries = ref(0)
const detailOpen = ref(false)
const confirmOpen = ref(false)
const confirmPending = ref(false)
const confirmed = ref(0)
const consequences = ['chat', 'pipeline', 'history'].map((key: string) =>
  t(`dashboard.catalogue.confirm.${key}`),
)

// Real members of the open channel (first page of the community list), so the profile dialog opens on real data.
const { channelId } = useCurrentChannel()
const community = useCommunityFirstPage(channelId)
const people: ComputedRef<CommunityRow[]> = computed(() => community.data.value ?? [])

const formOpen = ref(false)

// The code editor opens on the channel's first real script. Reading only: nothing saves until someone presses Save in it.
type EditorSession = { id: string; name: string; project: ScriptProject; sdkTypes: string | null }
const scripts = useCodeScripts()
const editorSession: Ref<EditorSession | null> = ref(null)
const editorMissing = ref(false)

async function openEditor(): Promise<void> {
  const first = scripts.data.value?.[0]

  editorMissing.value = first === undefined

  if (!first) return

  const project = await fetchScriptProject(first.id)

  if (!project) return

  editorSession.value = {
    id: first.id,
    name: first.name,
    project,
    sdkTypes: await fetchScriptSdkTypes(first.id),
  }
}

async function saveEditor(files: EditorFiles): Promise<CompileFeedback> {
  const session = editorSession.value

  if (!session)
    return { ok: false, message: '' }

  return saveScriptProject(session.id, { ...session.project, files })
}

const formPending = ref(false)
const formName = ref('')
const formNameError: Ref<string | undefined> = ref(undefined)
const formServerError: Ref<string | undefined> = ref(undefined)
const formCreated: Ref<string[]> = ref([])

function submitForm(): void {
  formServerError.value = undefined
  formNameError.value =
    formName.value.trim() === ''
      ? t('dashboard.catalogue.form.nameMissing')
      : undefined

  if (formNameError.value) return

  formPending.value = true
  setTimeout(() => {
    formPending.value = false

    if (formName.value.trim() === 'so') {
      formServerError.value = t('dashboard.catalogue.form.nameTaken')

      return
    }

    formCreated.value.push(formName.value.trim())
    formName.value = ''
    formOpen.value = false
  }, 800)
}

const templateText = ref('')
const templateField: Ref<InstanceType<typeof TemplateField> | null> = ref(null)

const countdownTarget = ref(Date.now() + 15_000)
const countdownZeros = ref(0)

function restartCountdown(): void {
  countdownTarget.value = Date.now() + 15_000
}

// Demo data for the VirtualList example only: 5,000 generated rows, some long enough to wrap.
interface DemoRow {
  id: number
  text: string
}
const demoRows: Ref<DemoRow[]> = ref(
  Array.from({ length: 5000 }, (_: unknown, id: number) => ({
    id,
    text:
      t('dashboard.catalogue.virtual.row', { number: id + 1 }) +
      (id % 7 === 0
        ? ' ' + t('dashboard.catalogue.virtual.long')
        : ''),
  })),
)

function addDemoRow(): void {
  const id = demoRows.value.length

  demoRows.value = [
    ...demoRows.value,
    { id, text: t('dashboard.catalogue.virtual.row', { number: id + 1 }) },
  ]
}

function pause(): Promise<void> {
  return new Promise((resolve: () => void) => setTimeout(resolve, 900))
}

function confirmDelete(): void {
  confirmPending.value = true
  setTimeout(() => {
    confirmPending.value = false
    confirmOpen.value = false
    confirmed.value += 1
  }, 800)
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <PageHeader :title="t('dashboard.catalogue.title')" :crumb="t('dashboard.catalogue.crumb')">
      <template #status>
        <StatusBadge tone="live" :label="t('dashboard.catalogue.status.live')" />
      </template>
      <template #meta>{{ t('dashboard.catalogue.meta') }}</template>
      <template #actions>
        <Button variant="outline">{{ t('dashboard.catalogue.outline') }}</Button>
        <Button>{{ t('dashboard.catalogue.primary') }}</Button>
      </template>
      <template #tabs><PageTabs :tabs="tabs" /></template>
    </PageHeader>

    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="grid gap-4 px-6 pt-4 pb-6">
        <p class="text-sm text-muted-foreground">{{ t('dashboard.catalogue.view', { view }) }}</p>

        <template v-if="view === 'blocks'">
          <StatStrip>
            <StatTile
              :label="t('dashboard.catalogue.stats.messages')"
              :value="54431"
              :context="t('dashboard.catalogue.stats.messagesContext')"
            />
            <StatTile
              :label="t('dashboard.catalogue.stats.channels')"
              :value="26"
              :context="t('dashboard.catalogue.stats.channelsContext')"
            />
            <StatTile
              :label="t('dashboard.catalogue.stats.hidden')"
              :value="8019"
              :context="t('dashboard.catalogue.stats.hiddenContext')"
              warn
            />
          </StatStrip>

          <div class="flex flex-wrap gap-4">
            <StatusBadge tone="ok" :label="t('dashboard.catalogue.status.ok')" />
            <StatusBadge tone="bad" :label="t('dashboard.catalogue.status.bad')" />
            <StatusBadge tone="live" :label="t('dashboard.catalogue.status.live')" />
            <StatusBadge tone="off" :label="t('dashboard.catalogue.status.off')" />
          </div>

          <FilterBar
            v-model="stateFilter"
            :label="t('dashboard.catalogue.filter.state')"
            :options="stateOptions"
          >
            <SearchField v-model="query" :placeholder="t('dashboard.catalogue.search')" />
          </FilterBar>
          <FilterBar
            v-model="sinceFilter"
            :label="t('dashboard.catalogue.filter.since')"
            :options="sinceOptions"
          />

          <PanelCard :title="t('dashboard.catalogue.panel.ready')" pad>
            <KeyValueList :items="kvItems" />
          </PanelCard>

          <div class="flex items-center gap-4">
            <Button variant="outline" @click="confirmOpen = true">{{
              t('dashboard.catalogue.confirm.open')
            }}</Button>
            <span class="text-sm text-muted-foreground">
              {{ t('dashboard.catalogue.confirm.count', { count: confirmed }) }}
            </span>
          </div>
          <ConfirmDialog
            v-model:open="confirmOpen"
            :title="t('dashboard.catalogue.confirm.title')"
            :description="t('dashboard.catalogue.confirm.description')"
            :consequences="consequences"
            :action="t('dashboard.catalogue.confirm.action')"
            :pending="confirmPending"
            @confirm="confirmDelete"
          />

          <div class="flex items-center gap-4">
            <Button variant="outline" @click="formOpen = true">{{
              t('dashboard.catalogue.form.open')
            }}</Button>
            <span class="text-sm text-muted-foreground">
              {{ t('dashboard.catalogue.form.count', { count: formCreated.length }) }}
            </span>
          </div>
          <FormDialog
            v-model:open="formOpen"
            :title="t('dashboard.catalogue.form.title')"
            :description="t('dashboard.catalogue.form.description')"
            :action="t('dashboard.catalogue.form.action')"
            :pending="formPending"
            :error="formServerError"
            @submit="submitForm"
          >
            <Field :data-invalid="formNameError ? true : undefined">
              <FieldLabel for="catalogue-command-name">{{
                t('dashboard.catalogue.form.name')
              }}</FieldLabel>
              <Input
                id="catalogue-command-name"
                v-model="formName"
                :aria-invalid="formNameError ? true : undefined"
                :placeholder="t('dashboard.catalogue.form.namePlaceholder')"
              />
              <FieldError v-if="formNameError">{{ formNameError }}</FieldError>
            </Field>
          </FormDialog>

          <div class="flex items-center gap-4">
            <Button variant="outline" @click="openEditor">{{
              t('dashboard.catalogue.codeEditor.open')
            }}</Button>
            <span v-if="scripts.error.value" role="alert" class="text-sm text-destructive">
              {{ scripts.error.value.message }}
            </span>
            <span v-else-if="editorMissing" class="text-sm text-muted-foreground">
              {{ t('dashboard.catalogue.codeEditor.none') }}
            </span>
          </div>
          <CodeEditor
            v-if="editorSession"
            :title="editorSession.name"
            :files="editorSession.project.files"
            :entry="editorSession.project.manifest.entry"
            :sdk-types="editorSession.sdkTypes ?? ''"
            :sdk-types-unavailable="editorSession.sdkTypes === null"
            :save="saveEditor"
            @close="editorSession = null"
          />

          <PanelCard :title="t('dashboard.catalogue.flow.title')" pad>
            <div class="flex flex-col items-start gap-3">
              <InlineFlow
                kind="confirm"
                harm
                :label="t('dashboard.catalogue.flow.confirm.label')"
                :ask="t('dashboard.catalogue.flow.confirm.ask')"
                :yes="t('dashboard.catalogue.flow.confirm.yes')"
                :result="t('dashboard.catalogue.flow.confirm.result')"
                :action="pause"
                :revert="pause"
              >
                <Button variant="outline">{{ t('dashboard.catalogue.flow.confirm.open') }}</Button>
              </InlineFlow>
              <InlineFlow
                kind="apply"
                :label="t('dashboard.catalogue.flow.apply.label')"
                :result="t('dashboard.catalogue.flow.apply.result')"
                :action="pause"
                :revert="pause"
              >
                <Button variant="outline">{{ t('dashboard.catalogue.flow.apply.open') }}</Button>
              </InlineFlow>
              <InlineFlow
                kind="run"
                :label="t('dashboard.catalogue.flow.run.label')"
                :text="t('dashboard.catalogue.flow.run.text')"
                :result="t('dashboard.catalogue.flow.run.result')"
                :action="pause"
              >
                <Button variant="outline">{{ t('dashboard.catalogue.flow.run.open') }}</Button>
              </InlineFlow>
              <InlineFlow
                kind="note"
                :label="t('dashboard.catalogue.flow.note.label')"
                :text="t('dashboard.catalogue.flow.note.text')"
              >
                <Button variant="outline">{{ t('dashboard.catalogue.flow.note.open') }}</Button>
              </InlineFlow>
            </div>
          </PanelCard>

          <PanelCard :title="t('dashboard.catalogue.countdown.title')" pad>
            <div class="flex items-center gap-4">
              <b class="text-2xl font-semibold">
                <Countdown
                  :target="countdownTarget"
                  :label="t('dashboard.catalogue.countdown.label')"
                  @zero="countdownZeros += 1"
                />
              </b>
              <Button variant="outline" @click="restartCountdown">
                {{ t('dashboard.catalogue.countdown.restart') }}
              </Button>
              <span class="text-sm text-muted-foreground">
                {{ t('dashboard.catalogue.countdown.count', { count: countdownZeros }) }}
              </span>
            </div>
          </PanelCard>

          <ChartPanel
            kind="line"
            :title="t('dashboard.catalogue.chart.line.title')"
            :takeaway="t('dashboard.catalogue.chart.line.takeaway')"
            :data="lineDemo"
            x="day"
            :series="lineSeries"
          />
          <ChartPanel
            kind="area"
            :title="t('dashboard.catalogue.chart.area.title')"
            :takeaway="t('dashboard.catalogue.chart.area.takeaway')"
            :data="lineDemo"
            x="day"
            :series="lineSeries"
          />
          <ChartPanel
            kind="bar"
            :title="t('dashboard.catalogue.chart.bar.title')"
            :takeaway="t('dashboard.catalogue.chart.bar.takeaway')"
            :data="monthDemo"
            x="month"
            :series="monthSeries"
          />
          <ChartPanel
            kind="groupedBar"
            :title="t('dashboard.catalogue.chart.groupedBar.title')"
            :takeaway="t('dashboard.catalogue.chart.groupedBar.takeaway')"
            :data="monthDemo"
            x="month"
            :series="monthSeries"
          />

          <PanelCard :title="t('dashboard.catalogue.template.title')" pad>
            <div class="grid gap-4 md:grid-cols-[1fr_20rem]">
              <TemplateField
                ref="templateField"
                v-model="templateText"
                context="command"
                :label="t('dashboard.catalogue.template.label')"
                :placeholder="t('dashboard.catalogue.template.placeholder')"
              />
              <div class="flex h-72 flex-col">
                <HelperPanel :target="templateField" context="command" />
              </div>
            </div>
          </PanelCard>

          <PanelCard
            :title="t('dashboard.catalogue.profile.title')"
            :state="
              community.isError.value
                ? 'error'
                : community.isPending.value
                  ? 'loading'
                  : people.length > 0
                    ? 'ready'
                    : 'empty'
            "
            :empty-text="t('dashboard.catalogue.profile.empty')"
            pad
            @retry="community.refetch()"
          >
            <p class="mb-3 text-sm text-muted-foreground">
              {{ t('dashboard.catalogue.profile.hint') }}
            </p>
            <ul class="flex flex-wrap gap-x-4 gap-y-2 text-sm">
              <li v-for="person in people" :key="person.id">
                <UserName :user-id="person.id" :name="person.displayName" />
              </li>
            </ul>
          </PanelCard>

          <PanelCard :title="t('dashboard.catalogue.virtual.title')" pad>
            <div class="flex flex-col gap-3">
              <div class="flex items-center gap-4">
                <Button variant="outline" @click="addDemoRow">{{
                  t('dashboard.catalogue.virtual.add')
                }}</Button>
                <span class="text-sm text-muted-foreground">
                  {{ t('dashboard.catalogue.virtual.count', { count: demoRows.length }) }}
                </span>
              </div>
              <div class="flex h-72 flex-col">
                <VirtualList
                  stick
                  :items="demoRows"
                  :item-key="(row: DemoRow) => row.id"
                  :estimate-size="36"
                  :label="t('dashboard.catalogue.virtual.title')"
                >
                  <template #default="{ item }">
                    <p class="px-3 py-2 text-sm">{{ item.text }}</p>
                  </template>
                </VirtualList>
              </div>
            </div>
          </PanelCard>

          <PanelCard :title="t('dashboard.catalogue.panel.ready')">
            <RowGroup>
              <SettingRow
                v-model="ttsOn"
                :title="t('dashboard.catalogue.settings.tts')"
                :description="t('dashboard.catalogue.settings.ttsHelp')"
              />
              <SettingRow
                v-model="modsOn"
                :title="t('dashboard.catalogue.settings.mods')"
                :description="t('dashboard.catalogue.settings.modsHelp')"
              />
            </RowGroup>
          </PanelCard>
        </template>

        <div v-else-if="view === 'states'" class="grid h-72 grid-cols-2 gap-4 xl:grid-cols-3">
          <PanelCard :title="t('dashboard.catalogue.panel.ready')" tier="lead">
            <RowGroup>
              <SummaryRow
                :title="t('dashboard.catalogue.panel.readyRow')"
                :detail="t('dashboard.catalogue.panel.readyDetail')"
              />
            </RowGroup>
          </PanelCard>
          <PanelCard :title="t('dashboard.catalogue.panel.loading')" state="loading" />
          <PanelCard
            :title="t('dashboard.catalogue.panel.empty')"
            state="empty"
            :empty-text="t('dashboard.catalogue.panel.emptyText')"
          />
          <PanelCard
            :title="t('dashboard.catalogue.panel.error')"
            state="error"
            :description="t('dashboard.catalogue.panel.retried', { count: retries })"
            @retry="retries += 1"
          />
          <PanelCard :title="t('dashboard.catalogue.panel.quiet')" tier="quiet" />
        </div>

        <template v-else>
          <div class="flex h-80 flex-col">
            <BoardGrid>
              <BoardCell :span="7" :rows="2">
                <PanelCard :title="t('dashboard.catalogue.layout.board')" tier="lead" />
              </BoardCell>
              <BoardCell :span="5">
                <PanelCard :title="t('dashboard.catalogue.layout.cell')" />
              </BoardCell>
              <BoardCell :span="5">
                <PanelCard :title="t('dashboard.catalogue.layout.cell')" tier="quiet" />
              </BoardCell>
            </BoardGrid>
          </div>
          <div class="flex h-80 flex-col">
            <ListDetail :show-detail="detailOpen">
              <template #list>
                <PanelCard :title="t('dashboard.catalogue.layout.list')">
                  <RowGroup>
                    <SummaryRow
                      pick
                      :title="t('dashboard.catalogue.panel.readyRow')"
                      @click="detailOpen = !detailOpen"
                    />
                  </RowGroup>
                </PanelCard>
              </template>
              <template #detail>
                <PanelCard :title="t('dashboard.catalogue.layout.detail')" tier="lead" />
              </template>
              <template #aside>
                <PanelCard :title="t('dashboard.catalogue.layout.aside')" tier="quiet" />
              </template>
            </ListDetail>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>
