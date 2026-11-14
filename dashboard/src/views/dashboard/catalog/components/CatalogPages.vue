<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The page recipes: PageBody, SettingsPageFrame, AdminCollectionPage, PublicTaskShell with TaskActions and
// WizardActions. Each sits in a frame whose width the reader picks.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import type { BoardPanel } from '@/components/layout/board';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PanelBeside from '@/components/layout/PanelBeside.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import AdminCollectionPage from '@/views/dashboard/admin/components/AdminCollectionPage.vue';
import SettingsPageFrame from '@/views/dashboard/settings/components/SettingsPageFrame.vue';
import PublicTaskShell from '@/views/site/components/PublicTaskShell.vue';
import StepsFrame from '@/views/site/setup/components/StepsFrame.vue';
import TaskActions from '@/views/site/components/TaskActions.vue';
import ViewerPage from '@/views/viewer/components/ViewerPage.vue';
import WizardActions from '@/views/site/components/WizardActions.vue';

import CatalogWidthFrame from './CatalogWidthFrame.vue';

const { t } = useI18n();

const BODIES = ['standard', 'dense', 'listDetail', 'board', 'workspace', 'beside'] as const;

const boardPanels = computed(() => [
  { id: 'one', label: t('dashboard.catalog.pages.bodies.board.one') },
  { id: 'two', label: t('dashboard.catalog.pages.bodies.board.two') },
] as const satisfies readonly BoardPanel[]);

// The first step is done and the second is open, as in the real wizard between two steps.
const setupSteps = computed(() => [
  {
    key: 'catalog_one',
    title: t('dashboard.catalog.pages.setupStepOne'),
    complete: true,
  },
  {
    key: 'catalog_two',
    title: t('dashboard.catalog.pages.setupStepTwo'),
    complete: false,
  },
]);

const page = ref(1);
const search = ref('');
const sideOpen = ref(true);
</script>

<template>
  <section data-catalog="page-body" class="grid gap-4">
    <CatalogWidthFrame :title="t('dashboard.catalog.pages.body')">
      <div class="grid gap-3">
        <PageBody dense class="rounded-[8px] bg-muted/40">
          <PanelCard :title="t('dashboard.catalog.pages.dense')" pad>{{ t('dashboard.catalog.pages.denseText') }}</PanelCard>
        </PageBody>
        <PageBody class="max-h-40 rounded-[8px] bg-muted/40">
          <PanelCard :title="t('dashboard.catalog.pages.scroll')" pad>{{ t('dashboard.catalog.pages.scrollText') }}</PanelCard>
          <PanelCard :title="t('dashboard.catalog.pages.scroll')" pad>{{ t('dashboard.catalog.pages.scrollText') }}</PanelCard>
        </PageBody>
      </div>
    </CatalogWidthFrame>
  </section>

  <section data-catalog="page-body-set" class="grid gap-4">
    <h2 class="text-base font-semibold">{{ t('dashboard.catalog.pages.bodySet') }}</h2>
    <ul class="grid grid-cols-2 gap-4 max-md:grid-cols-1">
      <li v-for="body in BODIES" :key="body" :data-body="body" class="grid min-w-0 content-start gap-2">
        <h3 class="text-sm font-medium">{{ t(`dashboard.catalog.pages.bodies.${body}.name`) }}</h3>
        <p class="text-sm text-muted-foreground">{{ t(`dashboard.catalog.pages.bodies.${body}.rule`) }}</p>
        <!-- A 12px frame around a 4px gap keeps each example's own 8px corners concentric. -->
        <div data-body-example class="@container flex h-56 min-w-0 flex-col overflow-hidden rounded-[12px] border border-dashed border-border p-1">
          <PageBody v-if="body === 'standard'" class="rounded-[8px] bg-muted/40">
            <PanelCard :title="t('dashboard.catalog.pages.bodies.standard.panel')" pad />
            <PanelCard :title="t('dashboard.catalog.pages.bodies.standard.panel')" pad />
          </PageBody>
          <PageBody v-else-if="body === 'dense'" dense class="rounded-[8px] bg-muted/40">
            <PanelCard :title="t('dashboard.catalog.pages.bodies.dense.panel')" tier="lead" class="min-h-0 flex-1" pad />
          </PageBody>
          <ListDetail v-else-if="body === 'listDetail'" class="rounded-[8px] bg-muted/40">
            <template #list>
              <PanelCard :title="t('dashboard.catalog.pages.bodies.listDetail.list')" pad />
            </template>
            <template #detail>
              <PanelCard :title="t('dashboard.catalog.pages.bodies.listDetail.detail')" tier="lead" pad />
            </template>
          </ListDetail>
          <BoardGrid v-else-if="body === 'board'" :panels="boardPanels" class="rounded-[8px] bg-muted/40">
            <BoardCell panel="one" :span="6">
              <PanelCard :title="t('dashboard.catalog.pages.bodies.board.one')" tier="lead" />
            </BoardCell>
            <BoardCell panel="two" :span="6">
              <PanelCard :title="t('dashboard.catalog.pages.bodies.board.two')" />
            </BoardCell>
          </BoardGrid>
          <div
            v-else-if="body === 'workspace'"
            data-slot="workspace"
            class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-[8px] bg-muted/40"
          >
            <div class="border-b border-border px-3 py-2 text-sm font-medium">
              {{ t('dashboard.catalog.pages.bodies.workspace.toolbar') }}
            </div>
            <p class="flex-1 p-3 text-sm text-muted-foreground">{{ t('dashboard.catalog.pages.bodies.workspace.stage') }}</p>
          </div>
          <PanelBeside v-else class="rounded-[8px] bg-muted/40">
            <PageBody dense>
              <PanelCard :title="t('dashboard.catalog.pages.bodies.beside.panel')" tier="lead" class="min-h-0 flex-1" pad />
            </PageBody>
            <template #panel>
              <SidePanel v-model:open="sideOpen" pinned :label="t('dashboard.catalog.pages.bodies.beside.side')">
                <PanelCard :title="t('dashboard.catalog.pages.bodies.beside.side')" tier="quiet" pad />
              </SidePanel>
            </template>
          </PanelBeside>
        </div>
      </li>
    </ul>
  </section>

  <section data-catalog="settings-frame" class="grid gap-4">
    <CatalogWidthFrame :title="t('dashboard.catalog.pages.settings')">
      <SettingsPageFrame :title="t('dashboard.catalog.pages.settingsTitle')" :crumb="t('dashboard.catalog.crumb')" dense>
        <template #actions>
          <Button variant="outline">{{ t('dashboard.catalog.outline') }}</Button>
          <Button>{{ t('dashboard.catalog.primary') }}</Button>
        </template>
        <PanelCard :title="t('dashboard.catalog.pages.settingsPanel')" pad>
          {{ t('dashboard.catalog.pages.settingsText') }}
        </PanelCard>
      </SettingsPageFrame>
    </CatalogWidthFrame>
  </section>

  <section data-catalog="admin-collection" class="grid gap-4">
    <CatalogWidthFrame :title="t('dashboard.catalog.pages.admin')">
      <AdminCollectionPage
        v-model:search="search"
        :title="t('dashboard.catalog.pages.adminTitle')"
        :count="t('dashboard.catalog.pages.adminCount')"
        :page="page"
        :pages="3"
        state="ready"
        :list-title="t('dashboard.catalog.pages.adminList')"
        :search-placeholder="t('dashboard.catalog.search')"
        @previous="page = Math.max(1, page - 1)"
        @next="page = Math.min(3, page + 1)"
      >
        <RowGroup>
          <SummaryRow :title="t('dashboard.catalog.panel.readyRow')" :detail="t('dashboard.catalog.panel.readyDetail')" />
        </RowGroup>
      </AdminCollectionPage>
    </CatalogWidthFrame>
  </section>

  <section data-catalog="task-shell" class="grid gap-4">
    <CatalogWidthFrame :title="t('dashboard.catalog.pages.task')">
      <PublicTaskShell class="min-h-0!" :brand="t('dashboard.catalog.pages.brand')">
        <PanelCard :title="t('dashboard.catalog.pages.taskTitle')" heading="h2" pad class="w-full">
          <TaskActions>
            <Button>{{ t('dashboard.catalog.pages.taskPrimary') }}</Button>
            <Button variant="outline">{{ t('dashboard.catalog.pages.taskSecondary') }}</Button>
          </TaskActions>
        </PanelCard>
        <PanelCard :title="t('dashboard.catalog.pages.wizardTitle')" class="w-full" pad>
          <WizardActions>
            <template #back><Button variant="outline">{{ t('dashboard.catalog.pages.back') }}</Button></template>
            <template #next><Button>{{ t('dashboard.catalog.pages.next') }}</Button></template>
          </WizardActions>
        </PanelCard>
        <template #below>{{ t('dashboard.catalog.pages.below') }}</template>
      </PublicTaskShell>
    </CatalogWidthFrame>
  </section>

  <section data-catalog="setup-shell" class="grid gap-4">
    <CatalogWidthFrame :title="t('dashboard.catalog.pages.setup')">
      <PublicTaskShell class="min-h-0!" width="wizard" :brand="t('dashboard.catalog.pages.brand')">
        <StepsFrame :steps="setupSteps" :finish-title="t('dashboard.catalog.pages.setupFinish')" :current="1" />
        <PanelCard :title="t('dashboard.catalog.pages.setupCard')"
                   tier="lead"
                   heading="h2"
                   pad
                   class="w-full"
        >
          <WizardActions>
            <template #back><Button variant="outline">{{ t('dashboard.catalog.pages.back') }}</Button></template>
            <template #next><Button>{{ t('dashboard.catalog.pages.next') }}</Button></template>
          </WizardActions>
        </PanelCard>
      </PublicTaskShell>
    </CatalogWidthFrame>
  </section>

  <section data-catalog="viewer-page" class="grid gap-4">
    <CatalogWidthFrame :title="t('dashboard.catalog.pages.viewer')">
      <ViewerPage :title="t('dashboard.catalog.pages.viewerTitle')" class="py-4">
        <template #status><StatusBadge tone="ok" :label="t('dashboard.catalog.pages.viewerStatus')" /></template>
        <template #actions><Button variant="outline">{{ t('dashboard.catalog.pages.viewerAction') }}</Button></template>
        <PanelCard :title="t('dashboard.catalog.pages.viewerTitle')" pad>{{ t('dashboard.catalog.pages.viewerBody') }}</PanelCard>
      </ViewerPage>
    </CatalogWidthFrame>
  </section>
</template>
