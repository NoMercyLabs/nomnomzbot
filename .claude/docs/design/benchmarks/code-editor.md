# Benchmark: widget code editor

Method: product docs fetched 2026-10-07 (one source URL per row). Ours = server/src/NomNomzBot.Api/Assets/editor/ (editor.js, preview.js, preview-sdk.js, index.html), read this run. Rows marked "Monaco default" rely on Monaco's built-in behaviour and were not exercised live (not checked live). Firebot docs pages did not load; its rows rest on the one search-listed GitHub issue and the docs URL below.

## Products

- VS Code https://code.visualstudio.com/docs/editor/codebasics
- VS Code REST Client (.http files) https://marketplace.visualstudio.com/items?itemName=humao.rest-client
- CodePen https://blog.codepen.io/documentation/
- StackBlitz https://developer.stackblitz.com/guides/user-guide/what-is-stackblitz
- CodeSandbox https://codesandbox.io/docs/learn/vm-sandboxes/preview
- StreamElements custom widget editor https://docs.streamelements.com/overlays/custom-widget
- Streamer.bot https://docs.streamer.bot/guide/actions
- Firebot https://docs.firebot.app/v5/core/events

## Features

| Feature | Seen in | Source | Ours | Status | Slice |
| --- | --- | --- | --- | --- | --- |
| Multi-cursor editing | VS Code | https://code.visualstudio.com/docs/editor/codebasics | editor.js:635 (multiCursorModifier ctrlCmd) | have | |
| Find and replace in file (regex, case) | VS Code | https://code.visualstudio.com/docs/editor/codebasics | Monaco default find widget, editor.js:610 | have | |
| Code folding | VS Code | https://code.visualstudio.com/docs/editor/codebasics | editor.js:627 | have | |
| Minimap toggle | VS Code | https://code.visualstudio.com/docs/editor/codebasics | editor.js:1164 toggleMinimap | have | |
| Split editors side by side | VS Code | https://code.visualstudio.com/docs/editor/codebasics | one editor, one active tab (index.html:170-171) | missing | S-EDITOR-SPLIT-VIEW |
| Completions, parameter info, hover docs | VS Code | https://code.visualstudio.com/docs/editor/intellisense | editor.js:630-633, SDK types editor.js:603 | have | |
| Snippets (user and built-in) | VS Code | https://code.visualstudio.com/docs/editor/userdefinedsnippets | no snippet provider in editor.js | missing | S-EDITOR-SNIPPETS |
| Emmet abbreviation expand in HTML/CSS | VS Code | https://code.visualstudio.com/docs/editor/emmet | no emmet in editor files | missing | S-EDITOR-SNIPPETS |
| JSON schema completion and validation for JSON input | VS Code | https://code.visualstudio.com/docs/languages/json | event JSON is a plain textarea, index.html:106; Monaco tab in progress | partial | S-EDITOR-JSON-SCHEMA |
| Rename symbol across files | VS Code | https://code.visualstudio.com/docs/editor/refactoring | Monaco TS service, editor.js:601; .vue models via vue-script-model.js (not checked live) | partial | S-EDITOR-CODE-NAV |
| Quick fix lightbulb and code actions | VS Code | https://code.visualstudio.com/docs/editor/refactoring | Monaco default for TS, editor.js:601 (not checked live) | partial | S-EDITOR-CODE-NAV |
| Command palette | VS Code | https://code.visualstudio.com/docs/editor/codebasics | editor.js:1046 commands, 1665 F1 and Ctrl+Shift+P | have | |
| Quick open file by name | VS Code | https://code.visualstudio.com/docs/editor/codebasics | editor.js:1668 Ctrl+P opens files palette | have | |
| Search across files | VS Code | https://code.visualstudio.com/docs/editor/codebasics | editor.js:808 runSearch, case-insensitive substring, 200 hits max | partial | S-EDITOR-SEARCH-REPLACE |
| Replace across files | VS Code | https://code.visualstudio.com/docs/editor/codebasics | runSearch only lists hits, editor.js:808-847 | missing | S-EDITOR-SEARCH-REPLACE |
| Go to definition, peek, find references | VS Code | https://code.visualstudio.com/docs/editor/editingevolved | Monaco TS service, editor.js:601 (cross-file not checked live) | partial | S-EDITOR-CODE-NAV |
| Outline / go to symbol in file | VS Code | https://code.visualstudio.com/docs/editor/editingevolved | Monaco default Ctrl+Shift+O (not checked live); no outline view in index.html | partial | S-EDITOR-SEARCH-REPLACE |
| Breadcrumbs | VS Code | https://code.visualstudio.com/docs/editor/editingevolved | none in index.html:169-173 | missing | S-EDITOR-CODE-NAV |
| Problems panel with click-to-reveal | VS Code | https://code.visualstudio.com/docs/editor/editingevolved | editor.js:657 renderProblems, 765 sidebar, 799 revealMarker | have | |
| Problem count in status bar | VS Code | https://code.visualstudio.com/docs/editor/editingevolved | index.html:202 problemCount | have | |
| Build errors shown as editor markers | CodePen | https://blog.codepen.io/documentation/console/ | editor.js:1188 showBuildErrors, 1214 setModelMarkers | have | |
| File tree with new, rename, delete | StackBlitz | https://developer.stackblitz.com/guides/user-guide/what-is-stackblitz | editor.js:386, 395, 408, tree 429-523 | have | |
| Asset upload (images, sounds, fonts) | CodePen | https://blog.codepen.io/documentation/adding-external-resources/ | no upload or drop handler in editor.js, preview.js | missing | S-EDITOR-ASSETS |
| External library by URL or search | CodePen | https://blog.codepen.io/documentation/adding-external-resources/ | fixed map only: react, vue (preview.js:28-33) | partial | S-EDITOR-EXTERNAL-LIBS |
| npm dependencies panel | CodeSandbox | https://codesandbox.io/docs/learn/vm-sandboxes/preview | fixed esm.sh map, preview.js:28-33 | partial | S-EDITOR-EXTERNAL-LIBS |
| Live preview updates as you type | CodePen | https://blog.codepen.io/documentation/ | editor.js:370 schedule() on content change | have | |
| Preview in sandboxed iframe | StackBlitz | https://developer.stackblitz.com/guides/user-guide/what-is-stackblitz | index.html:184 sandbox allow-scripts | have | |
| Preview error overlay with stack | CodeSandbox | https://codesandbox.io/docs/learn/vm-sandboxes/preview | index.html:186-193 previewError | have | |
| Preview size presets (phone, 1080p, custom) | StackBlitz | https://developer.stackblitz.com/guides/user-guide/what-is-stackblitz | none; preview pane only resizes by splitter (editor.js:729) | missing | S-EDITOR-PREVIEW-SIZES |
| Preview background (transparent, stream frame, chroma) | StreamElements | https://docs.streamelements.com/overlays/custom-widget | none in index.html:178-196 | missing | S-EDITOR-PREVIEW-SIZES |
| Hide or show preview, resizable panels | CodePen | https://blog.codepen.io/documentation/full-screen-editing/ | editor.js:720 setPreviewCollapsed, splitters 729, 1690 | have | |
| Fire an event sample at the widget | StreamElements | https://docs.streamelements.com/overlays/custom-widget-events | fire bar, preview.js:291-406, index.html:93-110 | have | |
| Edit event data before firing | Streamer.bot | https://docs.streamer.bot/guide/actions | plain textarea, index.html:106; Monaco tab in progress | partial | S-EDITOR-EVENT-TAB |
| Saved named test cases per widget | VS Code | https://code.visualstudio.com/docs/debugtest/debugging-configuration | none; sample edited in memory only, preview.js:360 | missing | S-EDITOR-TEST-CASES |
| Run a command/trigger with chosen user role and args | Streamer.bot | https://docs.streamer.bot/guide/actions | index.html:126-138, editor.js:1348-1440 | have | |
| Request file with Send Request CodeLens | REST Client | https://marketplace.visualstudio.com/items?itemName=humao.rest-client | none; no CodeLens provider in editor.js | missing | S-EDITOR-RUN-CODELENS |
| Environments and variables for test data | REST Client | https://marketplace.visualstudio.com/items?itemName=humao.rest-client | WIDGET_SETTINGS only, preview.js:103 | partial | S-EDITOR-TEST-CASES |
| Request history, re-run an earlier send | REST Client | https://marketplace.visualstudio.com/items?itemName=humao.rest-client | none; fire log keeps current run only, preview.js:436 | missing | S-EDITOR-RUN-TIMELINE |
| Inspect variables of a run | Streamer.bot | https://docs.streamer.bot/guide/actions | test-run result panel, editor.js:1427-1440 | partial | S-EDITOR-RUN-TIMELINE |
| Test effect list with the event's own metadata | Firebot | https://github.com/crowbartools/Firebot/issues/2786 | sample per event type, preview.js:302 | partial | S-EDITOR-REAL-SAMPLES |
| Record what the widget would send to the bot | Streamer.bot | https://docs.streamer.bot/guide/actions | preview log kinds fired/action/claim, preview.js:420-434; actions recorded only | partial | S-EDITOR-REAL-ACTIONS |
| Action history with status and duration | Streamer.bot | https://docs.streamer.bot/guide/actions | log has no duration or status, preview.js:436 | missing | S-EDITOR-RUN-TIMELINE |
| Console capture with levels and time | CodePen | https://blog.codepen.io/documentation/console/ | preview.js:453-499, index.html:114-120 | have | |
| Console clear button | CodePen | https://blog.codepen.io/documentation/console/ | preview.js:493-499 | have | |
| Type code into the console (REPL) | CodePen | https://blog.codepen.io/documentation/console/ | read-only list, index.html:120 | missing | S-EDITOR-CONSOLE-REPL |
| Console filter by level or text | CodeSandbox | https://codesandbox.io/docs/learn/vm-sandboxes/preview | none in index.html:114-120 | missing | S-EDITOR-CONSOLE-REPL |
| Logpoints (log without editing code) | VS Code | https://code.visualstudio.com/docs/debugtest/debugging | none | missing | S-EDITOR-CONSOLE-REPL |
| Breakpoints, call stack, watch | VS Code | https://code.visualstudio.com/docs/debugtest/debugging | none | not-doing | Browser devtools already debug the sandboxed iframe; a debugger is a large build (proposed, Stoney to confirm) |
| Test explorer with run icons in gutter | VS Code | https://code.visualstudio.com/docs/debugtest/testing | none | missing | S-EDITOR-RUN-CODELENS |
| Widget settings form from fields definition | StreamElements | https://docs.streamelements.com/overlays/custom-widget | settings reach preview (preview-sdk.js:38, preview.js:103); no form or Fields tab | partial | S-EDITOR-FIELDS-TAB |
| Field types (text, number, boolean, color) | StreamElements | https://docs.streamelements.com/overlays/custom-widget | none in editor files | missing | S-EDITOR-FIELDS-TAB |
| Editor theme choice, remembered | VS Code | https://code.visualstudio.com/docs/editor/profiles | editor.js:856-863, 1019 localStorage | have | |
| Word wrap toggle | VS Code | https://code.visualstudio.com/docs/editor/codebasics | editor.js:1157 toggleWrap | have | |
| Font size and tab size settings | CodePen | https://blog.codepen.io/documentation/pen-editor/ | fixed fontSize 13, tabSize constant, editor.js:616, 636 | missing | S-EDITOR-EDITOR-SETTINGS |
| Custom keyboard shortcuts | VS Code | https://code.visualstudio.com/docs/editor/settings-sync | fixed shortcuts, editor.js:1665-1686 | missing | S-EDITOR-EDITOR-SETTINGS |
| Settings sync across devices | VS Code | https://code.visualstudio.com/docs/editor/settings-sync | theme in localStorage only, editor.js:1019 | not-doing | Browser-local choices are enough for a streamer editor; account sync is out of scope for now |
| Format document | VS Code | https://code.visualstudio.com/docs/languages/json | editor.js:1615, 1050 | have | |
| Save with Ctrl+S, compile result shown | CodePen | https://blog.codepen.io/documentation/autosave/ | editor.js:1557, 1174, 1218 | have | |
| Autosave of drafts | CodePen | https://blog.codepen.io/documentation/autosave/ | save only on Ctrl+S or button, editor.js:1557 | missing | S-EDITOR-DIRTY-AUTOSAVE |
| Warning when leaving with unsaved changes | CodePen | https://blog.codepen.io/documentation/autosave/ | editor.js:1230-1262 | have | |
| Version history with rollback | VS Code | https://code.visualstudio.com/docs/editor/codebasics | editor.js:1264-1340, rollback 1275 | have | |
| Diff view between versions or saved copy | VS Code | https://code.visualstudio.com/docs/editor/codebasics | no createDiffEditor in editor files | missing | S-EDITOR-VERSION-DIFF |
| Share link to a widget | StackBlitz | https://developer.stackblitz.com/guides/user-guide/what-is-stackblitz | none; bundle metadata only, editor.js:1585 | missing | S-EDITOR-SHARE-FORK |
| Fork or duplicate a widget | StackBlitz | https://developer.stackblitz.com/guides/user-guide/what-is-stackblitz | none in index.html | missing | S-EDITOR-SHARE-FORK |
| Import and export a widget | Streamer.bot | https://docs.streamer.bot/guide/actions | bundle view only, index.html:160-161 | partial | S-EDITOR-SHARE-FORK |
| In-editor docs for the widget SDK and events | StreamElements | https://docs.streamelements.com/overlays/custom-widget-events | SDK types only, editor.js:603; no docs pane | partial | S-EDITOR-EVENT-CATALOG-VIEW |
| Shortcut list for newcomers | VS Code | https://code.visualstudio.com/docs/editor/codebasics | shortcuts only in tooltips, editor.js:167-175 | partial | S-EDITOR-EDITOR-SETTINGS |
| Workspace-level config file (per-project settings) | VS Code | https://code.visualstudio.com/docs/editor/workspaces | none | not-doing | A widget is one bundle; per-project config files add nothing the Fields tab will not cover |

## Ranking

1. S-EDITOR-EVENT-TAB - firing real event JSON is the core test loop; a Monaco tab with schema, completion and validation replaces the textarea (index.html:106).
2. S-EDITOR-TEST-CASES - saved named test cases make a repeat check one click; today every edit is lost on reload.
3. S-EDITOR-JSON-SCHEMA - a schema from the event types stops bad samples before they fire.
4. S-EDITOR-REAL-ACTIONS - actions are only recorded, so a streamer cannot see TTS or chat output actually happen.
5. S-EDITOR-FIELDS-TAB - widgets are configured by streamers; they must test settings in the preview without code.
6. S-EDITOR-PREVIEW-SIZES - widgets run in a stream scene at fixed sizes; the preview must show them.
7. S-EDITOR-REAL-SAMPLES - samples that carry the exact fields Twitch sends catch missing-field bugs.
8. S-EDITOR-CONSOLE-REPL - typing a call into the live widget context finds bugs without edit and reload.
9. S-EDITOR-RUN-TIMELINE - status and duration per fire show which step failed and how slow it was.
10. S-EDITOR-ASSETS - widgets need images and sounds; no upload means no real test of a styled alert.
