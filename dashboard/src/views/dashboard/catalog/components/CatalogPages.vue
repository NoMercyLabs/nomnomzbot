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
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import PageBody from '@/components/layout/PageBody.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import AdminCollectionPage from '@/views/dashboard/admin/components/AdminCollectionPage.vue';
import SettingsPageFrame from '@/views/dashboard/settings/components/SettingsPageFrame.vue';
import PublicTaskShell from '@/views/site/components/PublicTaskShell.vue';
import TaskActions from '@/views/site/components/TaskActions.vue';
import WizardActions from '@/views/site/components/WizardActions.vue';

import CatalogWidthFrame from './CatalogWidthFrame.vue';

const { t } = useI18n();

const page = ref(1);
const search = ref('');
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
</template>
