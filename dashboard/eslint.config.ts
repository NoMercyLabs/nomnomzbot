// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { globalIgnores } from 'eslint/config';
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript';
import pluginVue from 'eslint-plugin-vue';
import pluginPlaywright from 'eslint-plugin-playwright';
import pluginVitest from '@vitest/eslint-plugin';
import pluginOxlint from 'eslint-plugin-oxlint';
import pluginVueI18n from '@intlify/eslint-plugin-vue-i18n';
import pluginUnusedImports from 'eslint-plugin-unused-imports';
import pluginStylistic from '@stylistic/eslint-plugin';
import * as jsoncParser from 'jsonc-eslint-parser';

import { arrowTernaryBody } from './eslint-rules/arrow-ternary-body';
import { bodyShape } from './eslint-rules/body-shape';
import { maxCallNesting } from './eslint-rules/max-call-nesting';
import { noRedundantTypeAnnotation } from './eslint-rules/no-redundant-type-annotation';
import { trailingArrowArgument } from './eslint-rules/trailing-arrow-argument';
import { membersPerLine } from './eslint-rules/members-per-line';
import { genericOverAnnotation } from './eslint-rules/generic-over-annotation';

const pluginApp = {
  rules: {
    'arrow-ternary-body': arrowTernaryBody,
    'body-shape': bodyShape,
    'max-call-nesting': maxCallNesting,
    'no-redundant-type-annotation': noRedundantTypeAnnotation,
    'trailing-arrow-argument': trailingArrowArgument,
    'members-per-line': membersPerLine,
    'generic-over-annotation': genericOverAnnotation,
  },
};

// To allow more languages other than `ts` in `.vue` files, uncomment the following lines:
// import { configureVueProject } from '@vue/eslint-config-typescript'
// configureVueProject({ scriptLangs: ['ts', 'tsx'] })
// More info at https://github.com/vuejs/eslint-config-typescript/#advanced-setup

export default defineConfigWithVueTs(
  {
    name: 'app/files-to-lint',
    files: ['**/*.{vue,ts,mts,tsx}'],
  },

  globalIgnores(['**/dist/**', '**/dist-ssr/**', '**/coverage/**']),

  ...pluginVue.configs['flat/essential'],
  vueTsConfigs.recommended,

  {
    ...pluginPlaywright.configs['flat/recommended'],
    files: ['e2e/**/*.{test,spec}.{js,ts,jsx,tsx}'],
  },

  {
    ...pluginVitest.configs.recommended,
    files: ['src/**/__tests__/*'],
  },

  // V17: every key must exist in the locale files, and no text is written in a template.
  {
    name: 'app/vue-i18n',
    files: ['src/**/*.{vue,ts}'],
    plugins: { '@intlify/vue-i18n': pluginVueI18n },
    settings: {
      'vue-i18n': {
        localeDir: [
          { pattern: './src/i18n/locales/*.json', localeKey: 'file' },
          { pattern: './src/views/*/*/i18n/*.json', localeKey: 'file' },
        ],
        messageSyntaxVersion: '^11.0.0',
      },
    },
    rules: {
      '@intlify/vue-i18n/no-missing-keys': 'error',
      '@intlify/vue-i18n/no-raw-text': 'error',
      '@intlify/vue-i18n/no-v-html': 'error',
    },
  },

  {
    // A key written twice in a locale file silently keeps the last one (2026-10-13: clearFocus
    // landed twice in the chat en.json and nl.json).
    name: 'app/vue-i18n-locales',
    files: ['src/i18n/locales/*.json', 'src/views/*/*/i18n/*.json'],
    languageOptions: { parser: jsoncParser },
    plugins: { '@intlify/vue-i18n': pluginVueI18n },
    settings: {
      'vue-i18n': {
        localeDir: [
          { pattern: './src/i18n/locales/*.json', localeKey: 'file' },
          { pattern: './src/views/*/*/i18n/*.json', localeKey: 'file' },
        ],
        messageSyntaxVersion: '^11.0.0',
      },
    },
    rules: {
      '@intlify/vue-i18n/no-duplicate-keys-in-locale': 'error',
    },
  },

  {
    // shadcn-vue output is changed only through its CLI, so its own style is not linted.
    // Its English screen-reader labels never reach a page: structure.spec.ts fails a caller
    // that shows them.
    name: 'app/shadcn-generated',
    files: ['src/components/ui/**/*.{vue,ts}'],
    rules: {
      'vue/multi-word-component-names': 'off',
      '@intlify/vue-i18n/no-raw-text': 'off',
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-unused-vars': 'off',
    },
  },

  {
    name: 'app/api-client-only-in-api',
    files: ['src/**/*.{vue,ts,tsx}'],
    ignores: ['src/api/**'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              regex: '(^|/)api/client(\\.ts)?$',
              message:
                'Only files in src/api/ may import the API client. Add a query function in src/api/<area>.ts instead.',
            },
          ],
        },
      ],
    },
  },

  {
    // An unused import is deleted by `eslint --fix`, never by hand (owner 2026-10-13).
    name: 'app/unused-imports',
    files: ['src/**/*.{vue,ts}', 'e2e/**/*.ts', 'eslint-rules/**/*.ts'],
    ignores: ['src/components/ui/**'],
    plugins: { 'unused-imports': pluginUnusedImports },
    rules: {
      '@typescript-eslint/no-unused-vars': 'off',
      'unused-imports/no-unused-imports': 'error',
      'unused-imports/no-unused-vars': [
        'error',
        { vars: 'all', args: 'after-used', argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],
    },
  },

  {
    // A local whose annotation repeats its value's type drops it, and a generic call takes the
    // type as a generic instead of an annotation (owner 2026-10-13: "use generics wherever
    // supported").
    name: 'app/no-redundant-type-annotation',
    files: ['src/**/*.{vue,ts}'],
    ignores: ['src/components/ui/**', 'src/api/schema.d.ts'],
    languageOptions: {
      parserOptions: { projectService: true, extraFileExtensions: ['.vue'] },
    },
    plugins: { app: pluginApp },
    rules: {
      'app/no-redundant-type-annotation': 'error',
      'app/generic-over-annotation': 'error',
    },
  },

  ...pluginOxlint.buildFromOxlintConfigFile('.oxlintrc.json'),

  {
    // ESLint is the one formatter (owner 2026-10-13: "can we not move the settings from prettier
    // into eslint so we don't have 2 tools fighting eachother"). The stylistic preset carries the
    // old Prettier settings; the house rules below refine it.
    name: 'app/format',
    files: ['src/**/*.{vue,ts}', 'e2e/**/*.ts', 'eslint-rules/**/*.ts', '*.ts'],
    ignores: ['src/components/ui/**', 'src/api/schema.d.ts'],
    plugins: { '@stylistic': pluginStylistic },
    rules: {
      ...pluginStylistic.configs.customize({
        indent: 2,
        quotes: 'single',
        semi: true,
        commaDangle: 'always-multiline',
        braceStyle: '1tbs',
        arrowParens: true,
        quoteProps: 'as-needed',
        jsx: false,
      }).rules,
      '@stylistic/max-len': [
        'error',
        {
          code: 100,
          ignoreUrls: true,
          ignoreStrings: true,
          ignoreTemplateLiterals: true,
          ignoreRegExpLiterals: true,
          ignoreComments: true,
        },
      ],
    },
  },

  {
    // Vue templates keep their house shape: 2-space indent, double-quoted
    // attributes, one attribute per line once an element spans lines.
    name: 'app/format-vue-template',
    files: ['src/**/*.vue'],
    ignores: ['src/components/ui/**'],
    rules: {
      // vue/max-len below measures .vue files; the stylistic one would count class lists too.
      '@stylistic/max-len': 'off',
      'vue/html-indent': ['error', 2],
      'vue/html-quotes': ['error', 'double'],
      'vue/html-closing-bracket-newline': 'error',
      'vue/html-closing-bracket-spacing': 'error',
      'vue/mustache-interpolation-spacing': 'error',
      'vue/no-multi-spaces': 'error',
      'vue/max-attributes-per-line': ['error', { singleline: { max: 4 }, multiline: { max: 1 } }],
      'vue/max-len': [
        'error',
        {
          code: 100,
          template: 100,
          ignoreUrls: true,
          ignoreStrings: true,
          ignoreTemplateLiterals: true,
          ignoreHTMLAttributeValues: true,
          ignoreComments: true,
        },
      ],
    },
  },

  {
    // A blank line sets apart each group of statements (owner 2026-10-13: "add some new lines
    // in between things"). `eslint --fix` adds them.
    name: 'app/blank-lines',
    files: ['src/**/*.{vue,ts}', 'e2e/**/*.ts', 'eslint-rules/**/*.ts'],
    ignores: ['src/components/ui/**', 'src/api/schema.d.ts'],
    plugins: { '@stylistic': pluginStylistic },
    rules: {
      '@stylistic/padding-line-between-statements': [
        'error',
        { blankLine: 'always', prev: '*', next: 'return' },
        { blankLine: 'always', prev: ['const', 'let'], next: '*' },
        { blankLine: 'any', prev: ['const', 'let'], next: ['const', 'let'] },
        { blankLine: 'always', prev: 'multiline-block-like', next: '*' },
        { blankLine: 'always', prev: '*', next: 'multiline-block-like' },
        // A call that spans lines stands apart too (owner 2026-10-13: "terrible for reading").
        { blankLine: 'always', prev: 'multiline-expression', next: '*' },
        { blankLine: 'always', prev: '*', next: 'multiline-expression' },
        { blankLine: 'always', prev: 'import', next: '*' },
        { blankLine: 'any', prev: 'import', next: 'import' },
        { blankLine: 'always', prev: '*', next: ['function', 'class', 'export'] },
        { blankLine: 'any', prev: 'export', next: 'export' },
        { blankLine: 'always', prev: '*', next: ['if', 'for', 'while', 'do', 'switch', 'try'] },
        { blankLine: 'always', prev: ['if', 'for', 'while', 'do', 'switch', 'try'], next: '*' },
      ],
    },
  },

  {
    // Owner 2026-10-13: a ternary puts `? a` and `: b` on their own lines, and an if body is a
    // bare exit on the if line, a returned value on the next line, or a block.
    name: 'app/layout',
    files: ['src/**/*.{vue,ts}', 'e2e/**/*.ts', 'eslint-rules/**/*.ts'],
    ignores: ['src/components/ui/**', 'src/api/schema.d.ts'],
    plugins: {
      '@stylistic': pluginStylistic,
      app: pluginApp,
    },
    rules: {
      '@stylistic/multiline-ternary': ['error', 'always'],
      '@stylistic/operator-linebreak': [
        'error',
        null,
        { overrides: { '?': 'before', ':': 'before' } },
      ],
      '@stylistic/indent': ['error', 2, { offsetTernaryExpressions: true }],
      'app/arrow-ternary-body': 'error',
      'app/body-shape': 'error',
      'app/trailing-arrow-argument': 'error',
      'app/max-call-nesting': 'error',
      'app/members-per-line': 'error',
    },
  },

  {
    // Owner 2026-10-13: "turn [...channelHandlers, ...communityHandlers, ...chatHandlers] into
    // merge(channelHandlers, communityHandlers, chatHandlers)".
    name: 'app/merge-lists',
    files: ['src/**/*.{vue,ts}'],
    ignores: ['src/components/ui/**', 'src/api/schema.d.ts'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector:
            'ArrayExpression[elements.0.type=\'SpreadElement\'][elements.1.type=\'SpreadElement\']',
          message: 'Join lists with merge(a, b) from @/lib/array.',
        },
      ],
    },
  },
);
