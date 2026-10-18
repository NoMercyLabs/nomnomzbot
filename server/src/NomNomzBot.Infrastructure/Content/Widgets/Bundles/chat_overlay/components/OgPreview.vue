<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
defineProps<{
  title: string
  description?: string
  host?: string
  imageUrl?: string
}>()
</script>

<template>
  <div class="og-preview">
    <img v-if="imageUrl" class="og-image" :src="imageUrl" :alt="title || 'Link preview'">
    <div class="og-text">
      <h3>{{ title || 'No title' }}</h3>
      <p v-if="description">{{ description }}</p>
      <small v-if="host">{{ host }}</small>
    </div>
  </div>
</template>

<style scoped>
.og-preview {
  display: flex;
  flex-direction: row;
  gap: 8px;
  width: 100%;
  margin-top: 4px;
  padding: 0 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 4px;
  overflow: hidden;
  background: #374151;
}
.og-image {
  width: 25%;
  height: 150px;
  object-fit: contain;
  border-radius: 2px;
  align-self: center;
}
.og-text {
  padding: 8px;
  min-width: 0;
}
.og-text h3 {
  margin: 0;
  font-size: 1em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.og-text p {
  margin: 4px 0;
  font-size: 0.85em;
  line-height: 1.4;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
}
.og-text small {
  color: rgba(255, 255, 255, 0.6);
}
</style>
