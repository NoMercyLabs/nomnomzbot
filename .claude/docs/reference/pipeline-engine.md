# Pipeline Engine

Commands and event responses run a visual pipeline. A pipeline is a **tree** of steps, not a flat list.

## Tree model

Each `PipelineStep` (`NomNomzBot.Domain/Commands/Entities/PipelineStep.cs`) carries:

- `ParentStepId` — the owning block; null for a top-level step.
- `Branch` — the lane inside the parent (for example `then` / `else`).
- `BlockKind` — null for a leaf action step; a value below for a block step.
- `BlockConfigJson` — block-specific settings (condition, switch value, loop mode, case weight).
- `ActionType` — the snake_case action key (leaf steps).
- `ConfigJson` — the action's own settings.

**Control-flow block kinds:**

- `if` (with else-if / else lanes)
- `switch` / `switch_case`
- `loop`
- `random_branch` / `random_case`
- `try`
- `detached_step`

**Control-flow actions:** `break`, `continue`, `return_value`, `run_pipeline`, `wait_for_event`.

## Actions

`ActionType` strings are snake_case (`send_message`, `timeout`, ...). Unknown types are rejected at save time.
About 110 types exist. The families:

- **Chat and messaging:** `send_message`, `send_reply`, `announce`, `shoutout`, `send_webhook`, `send_discord_notification`.
- **Moderation:** `ban`, `timeout`, `delete_message`, `permit`, `unpermit`, `start_raid`.
- **State:** `set_variable`, counters (`set_counter`, `adjust_counter`), viewer data (`set_viewer_data`, ...), `set_pronoun`, `pick_from_list`, `post_quote`.
- **Flow and timing:** `wait`, `wait_for_event`, `wait_until_raid_fires`, `comparison`, `stop`, `run_pipeline`, `schedule_pipeline`.
- **Economy and games:** currency (`grant_currency`, `deduct_currency`, `check_balance`), giveaways, live games, jar, redemptions.
- **Music and song requests:** `music_*` and `song_*` actions.
- **Audio and TTS:** `play_sound`, `stop_sound`, `play_tts`, `tts_synthesize`.
- **Overlays and OBS:** `widget_event`, `obs_*`.
- **VTube Studio:** `vts_*`.
- **Custom code:** `run_code`.

The live catalogue is `GET /api/v1/channels/{channelId}/pipelines/actions` (`PipelinesController`). Use it, not this list, as the source of truth.

Actions implement `ICommandAction` and register by assembly scan. See *Adding a New Pipeline Action* in `common-tasks.md`.

## Conditions

Conditions are blocks (`if`, `switch`) and comparison actions. They test the user's role (broadcaster / mod / sub / vip / everyone), random chance, and variable comparisons.

## Template variables

Templates use single braces: `{user}`, `{channel.title}`, `{args.1}`, and so on. `TemplateResolver` (`NomNomzBot.Infrastructure/Platform/Templating/`) resolves them.
The double-brace form (`{{user.name}}`) is **not** implemented.
The template grammar is an open owner question. Do not spec or build a new form until the owner answers.

## Scripting

`run_code` executes sandboxed user scripts (Jint on self-host) via `IScriptExecutor`. All other actions are compiled C# classes.
