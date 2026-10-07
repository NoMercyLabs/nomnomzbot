# Benchmark: pipeline editor

Date 2026-10-07. Sources were fetched or searched this run. "Ours" paths are under app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/feature/pipelines/ (short names: Screen = ui/PipelinesScreen.kt, Controller = state/PipelinesController.kt). A "missing" row means a grep of the pipelines folder found nothing (grep for duplicate, undo, import/export, search, pin, copy/paste). Streamer.bot, Firebot, Zapier, n8n, Make and Mix It Up facts come from the pages linked below; no source means the feature is left out.

## Products

- Streamer.bot https://docs.streamer.bot/guide/core/actions
- Firebot https://docs.firebot.app/v5/core/effects
- Mix It Up https://wiki.mixitupapp.com/en/special-identifiers
- Zapier https://help.zapier.com/hc/en-us/articles/18811411817741-Test-Zap-steps
- Make.com https://help.make.com/
- n8n https://docs.n8n.io/data/data-pinning/
- Node-RED https://nodered.org/docs/user-guide/editor/
- Apple Shortcuts https://support.apple.com/guide/shortcuts/welcome/ios
- IFTTT https://help.ifttt.com/hc/en-us/articles/37696590075547-How-do-I-check-my-Applet-activity-and-troubleshoot-issues

## Features

| Feature | Seen in | Source | Ours | Status | Slice |
|---|---|---|---|---|---|
| Building: add a step from a categorized list of actions | Streamer.bot, Firebot | https://docs.streamer.bot/guide/core/actions | Screen:2466 (backend palette grouped by category) | have | - |
| Building: reorder steps with up/down | Streamer.bot | https://docs.streamer.bot/guide/core/actions | Screen:1266, Controller:384 | have | - |
| Building: drag and drop to reorder or move a step into a group | Streamer.bot | https://docs.streamer.bot/guide/core/actions | Screen:1266 (buttons only, no drag) | partial | S-PIPE-UNDO-DUPLICATE |
| Building: if / else conditional blocks | Firebot | https://docs.firebot.app/v5/guides/conditional-effects | Screen:1331 (IfBlockCard) | have | - |
| Building: random branch with weights | Streamer.bot | https://docs.streamer.bot/guide/core/actions | Screen:1861 (RandomBranchBlockCard) | have | - |
| Building: try / error-catch block | Make.com | https://help.make.com/error-handling | Screen:1418 (TryBlockCard) | have | - |
| Building: run another pipeline as a reusable step | Zapier, Node-RED, Firebot | https://help.zapier.com/hc/en-us/articles/8496308527629-Create-reusable-Zap-steps-with-the-Sub-Zap-app | Screen:3026 (RunPipelineArgumentsField); run_pipeline in .claude/docs/reference/pipeline-engine.md | have | - |
| Building: script step for custom logic | IFTTT, Mix It Up | https://help.ifttt.com/hc/en-us/articles/4406412223771-How-to-add-filter-code | Screen:2929 (CodeScriptStepField) | have | - |
| Building: several triggers on one action, shown in the editor | Streamer.bot | https://docs.streamer.bot/guide/core/triggers | PipelineTestRunDialog.kt:74 (pipelines bind from commands, events, timers; no trigger panel in the editor) | partial | S-PIPE-TRIGGER-PANEL |
| Building: switch a single step off to find a fault | Streamer.bot | https://docs.streamer.bot/guide/core/actions | not found | missing | S-PIPE-STEP-TOGGLE |
| Building: copy and paste steps | Streamer.bot | https://docs.streamer.bot/guide/core/actions | not found | missing | S-PIPE-UNDO-DUPLICATE |
| Building: action queues, pause and resume | Streamer.bot, Firebot | https://docs.streamer.bot/guide/core/actions | not found | missing | S-PIPE-QUEUES |
| Building: search for a step or pipeline | Node-RED | https://nodered.org/docs/user-guide/editor/ | not found (only the history filter) | missing | S-PIPE-FOLDERS |
| Explaining: help panel for each block (what it does) | Node-RED | https://nodered.org/docs/user-guide/editor/ | Screen:3431 (help text per field only) | partial | S-PIPE-BLOCK-HELP |
| Explaining: plain one-line description of a condition | Firebot | https://docs.firebot.app/v5/guides/conditional-effects | ui/ConditionExplainers.kt:38 | partial | S-PIPE-SENTENCE-SUMMARY |
| Explaining: whole pipeline read as one sentence (If this, then that) | IFTTT | https://help.ifttt.com/hc/en-us/articles/4411016949403-Glossary | not found (only per-step ParamSummary, Screen:2379) | missing | S-PIPE-SENTENCE-SUMMARY |
| Explaining: show the order things run in | IFTTT | https://ifttt.com/docs/applets | not found | missing | S-PIPE-SENTENCE-SUMMARY |
| Explaining: ready-made starting examples (gallery or presets) | Apple Shortcuts, Firebot | https://support.apple.com/guide/shortcuts/welcome/ios | ui/PipelineRecipePicker.kt:47 (4 recipes in state/PipelineRecipes.kt) | partial | S-PIPE-RECIPES-MORE |
| Explaining: sample value beside each variable | Zapier | https://help.zapier.com/hc/en-us/articles/8496343026701-Send-data-between-steps-by-mapping-fields | state/PipelineVariableCatalogue.kt:29 (VariableOption.sample) | have | - |
| Explaining: reference list of all variables | Mix It Up | https://wiki.mixitupapp.com/en/special-identifiers | Screen:55 (TemplateHelpersLink) | partial | S-PIPE-BLOCK-HELP |
| Data: pick a variable from earlier steps into a field | Zapier | https://help.zapier.com/hc/en-us/articles/8496343026701-Send-data-between-steps-by-mapping-fields | Screen:2808 (TemplateVariableField), state/PipelineVariableCatalogue.kt:35 | have | - |
| Data: argument stack of all values at a step | Streamer.bot | https://docs.streamer.bot/guide/variables | state/PipelineVariableCatalogue.kt:35 (declaredVariablesBefore) | partial | S-PIPE-STEP-IO |
| Data: built-in variables for the event (user, args) | Mix It Up | https://wiki.mixitupapp.com/en/special-identifiers | Screen:2795 (template helpers) | have | - |
| Data: text functions on a variable (lower, upper, length) | Mix It Up | https://wiki.mixitupapp.com/en/actions/special-identifier-action | Screen:2795 (helpers list; which functions: not checked) | partial | S-PIPE-BLOCK-HELP |
| Data: user-made variables and counters | Streamer.bot, Firebot | https://docs.streamer.bot/guide/variables | .claude/docs/reference/pipeline-engine.md (set_variable, counters) | have | - |
| Data: map fields from the real output of a tested step | Zapier, Make.com | https://help.zapier.com/hc/en-us/articles/8496343026701-Send-data-between-steps-by-mapping-fields | not found (sample is the declared default only) | missing | S-PIPE-STEP-IO |
| Testing: run the whole pipeline with sample values | Streamer.bot | https://docs.streamer.bot/guide/core/triggers | ui/PipelineTestRunDialog.kt:92 | have | - |
| Testing: test one step alone | n8n, Make.com, Zapier, Firebot | https://docs.n8n.io/workflows/executions/manual-partial-and-production-executions/ | not found | missing | S-PIPE-TEST-STEP |
| Testing: pin sample data so later runs reuse it | n8n | https://docs.n8n.io/data/data-pinning/ | not found | missing | S-PIPE-TEST-CASES |
| Testing: edit the sample data and save named test cases | n8n | https://community.n8n.io/t/pin-to-the-node-input-data-with-the-ability-to-manually-edit-the-input-data/128226 | PipelineTestRunDialog.kt:66 (key=value lines, nothing saved) | partial | S-PIPE-TEST-CASES |
| Testing: send a simulated real event | Streamer.bot | https://docs.streamer.bot/guide/core/triggers | not found | missing | S-PIPE-SIMULATE-EVENT |
| Testing: per-step data in and data out | Zapier | https://help.zapier.com/hc/en-us/articles/18811411817741-Test-Zap-steps | ui/PipelineTraceList.kt:44 (trace; values in and out: not checked) | partial | S-PIPE-STEP-IO |
| Testing: run history with a failures filter | IFTTT, Streamer.bot, Make.com | https://help.ifttt.com/hc/en-us/articles/37696590075547-How-do-I-check-my-Applet-activity-and-troubleshoot-issues | ui/PipelineHistoryScreen.kt:154 | have | - |
| Testing: inspect variables of a past run | Streamer.bot | https://docs.streamer.bot/faq/view-variables | ui/PipelineHistoryScreen.kt:77 (step logs; variable values: not checked) | partial | S-PIPE-STEP-IO |
| Testing: re-run a past execution with its data | n8n | https://docs.n8n.io/workflows/executions/debug/ | not found | missing | S-PIPE-RERUN |
| Errors: failing step and its error called out in the log | IFTTT | https://help.ifttt.com/hc/en-us/articles/37696590075547-How-do-I-check-my-Applet-activity-and-troubleshoot-issues | ui/PipelineHistoryScreen.kt:77 | have | - |
| Errors: per-step error route (retry, ignore, fallback) | Make.com | https://help.make.com/error-handling | Screen:1418 (try block only, no per-step route) | partial | S-PIPE-ERROR-ROUTE |
| Errors: clear message while editing that says how to fix it | IFTTT | https://help.ifttt.com/hc/en-us/articles/43254066003099-How-to-troubleshoot-errors-when-editing-an-Applet | Screen:3600 (blockComplete gate; message text: not checked) | partial | S-PIPE-EDIT-ERRORS |
| Organising: turn a whole pipeline on or off | Streamer.bot | https://docs.streamer.bot/guide/core/actions | Controller:211 (togglePipeline), Screen:653 | have | - |
| Organising: duplicate a pipeline | Streamer.bot, Apple Shortcuts | https://support.apple.com/guide/shortcuts/welcome/ios | not found | missing | S-PIPE-UNDO-DUPLICATE |
| Organising: undo and redo | n8n | https://docs.n8n.io/build/keyboard-shortcuts | not found | missing | S-PIPE-UNDO-DUPLICATE |
| Organising: folders or groups | Apple Shortcuts, Streamer.bot, Node-RED | https://support.apple.com/guide/shortcuts/welcome/ios | not found | missing | S-PIPE-FOLDERS |
| Organising: start a new pipeline from a template | Apple Shortcuts | https://support.apple.com/guide/shortcuts/welcome/ios | ui/PipelineRecipePicker.kt:47, Controller:186 | have | - |
| Sharing: export and import a pipeline | Streamer.bot, Node-RED | https://docs.streamer.bot/guide/core/actions | not found | missing | S-PIPE-EXPORT |
| Sharing: community gallery of shared pipelines | Streamer.bot, Apple Shortcuts | https://docs.streamer.bot/guide/core/actions | not found | missing | S-PIPE-EXPORT |
| Sharing: run from voice, widget or watch | Apple Shortcuts | https://support.apple.com/guide/shortcuts/welcome/ios | not applicable | not-doing | The bot runs on the server for chat events, so no device surface applies. |
| Sharing: cloud sync of personal shortcuts across devices | Apple Shortcuts | https://support.apple.com/guide/shortcuts/welcome/ios | not applicable | not-doing | Pipelines already live on the server per channel, so sync has no meaning. |

## Ranking

1. S-PIPE-TEST-STEP: a streamer who cannot code needs to try one step and see it work before trusting the whole chain (n8n, Make, Zapier, Firebot all have it).
2. S-PIPE-SENTENCE-SUMMARY: reading the pipeline as "When X happens, do Y" explains it without reading blocks (IFTTT model).
3. S-PIPE-BLOCK-HELP: a help panel with a plain example per block answers "what does this do" at the moment of doubt (Node-RED).
4. S-PIPE-RECIPES-MORE: starting from a working example beats a blank page; only 4 recipes exist now (Apple gallery, Firebot presets).
5. S-PIPE-TEST-CASES: saved named sample events (in the code editor, not a textarea) make testing repeatable without typing key=value lines (n8n pinned data).
6. S-PIPE-SIMULATE-EVENT: one click that fakes a real follow or raid shows the true result with real fields (Streamer.bot Test Trigger).
7. S-PIPE-STEP-IO: showing data in and data out per step tells the streamer which step went wrong and why (Zapier).
8. S-PIPE-UNDO-DUPLICATE: copying a working pipeline is how non-coders make variants safely (Streamer.bot, Shortcuts).
9. S-PIPE-UNDO-DUPLICATE: a mistake that can be undone removes the fear of trying things (n8n).
10. S-PIPE-STEP-TOGGLE: switching one step off finds a fault without deleting work (Streamer.bot).
