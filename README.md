# Arian Revit MCP Plugin

**A professional Model Context Protocol (MCP) server that runs *inside* Autodesk Revit 2027** and lets any AI agent
(Claude, GPT, Gemini, Cursor, VS Code agents …) model, edit, document and audit a Revit project — architecture,
structure and MEP — create Revit **families from scratch**, manage materials, silence warnings/dialogs, run
Dynamo graphs and execute arbitrary Revit API code.

* **258 tools** across Architecture · Structure · MEP · Families · Materials · Views/Sheets/Schedules · Model management · Scripting
* A new **Arian** ribbon tab with a **Start/Stop Server** button, connection helper, settings and automation toggles
* Built-in **BIM knowledge base** (workflows, LOD 100–500, recipes per discipline, family authoring) exposed as MCP *resources*, *prompts* and the `get_guidance` tool
* The agent can **see** the model: `get_view_image` returns a real PNG of any view
* Units are always **millimetres / degrees / m² / m³** — no feet-vs-mm mistakes
* Hot-reloadable tool assembly (no Revit restart while developing) and agent-defined **custom tools**

> Quality of the result depends on the model you connect (Claude Opus/Sonnet, GPT, …). The plugin gives it
> everything a senior BIM modeller needs: precise tools, validation feedback, visual verification and expert guidance.

---

## 1. Install

Requirements: Windows, **Autodesk Revit 2027**, [.NET 10 SDK](https://dotnet.microsoft.com/download) (only to build), Node.js 18+ (only for the stdio bridge used by Claude Desktop).

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1      # builds + installs for Revit 2027 (per user, no admin)
```

The script copies the plugin to `%APPDATA%\Autodesk\Revit\Addins\2027\ArianRevitMcp\` and writes `ArianRevitMcp.addin`.
On the first start Revit asks about an *unsigned add-in*: choose **Always Load**.

**Without the SDK (release zip):** `powershell -File scripts\package.ps1` builds `dist\ArianRevitMcp-<version>.zip`. On any machine unzip it and run `Install.cmd` (per-user, no admin; `Uninstall.cmd` removes it). Signing: `scripts\sign.ps1 -Pfx cert.pfx -Password …` (a CA-trusted code-signing certificate is what removes the *Always Load* prompt).

## 2. Start the server

Open Revit → **Arian** tab:

| Button | What it does |
|---|---|
| **Start / Stop Server** | Starts the local MCP server (`http://127.0.0.1:8765/mcp`). The button turns red while running. |
| **Connect Agent** | Shows ready-to-copy commands for Claude Code / Claude Desktop / Cursor and can add the server to the Claude Desktop config for you. |
| **Settings** | Port, auto-start with Revit, access token, code execution on/off, warning policy, dialog automation, exposed tool categories. |
| **Warnings: …** | Cycles *Default → Ignore warnings → Ignore + auto-resolve errors*. |
| **Auto Dialogs** | Auto-answers Revit task dialogs with the least destructive choice. |
| **Dismiss Dialogs** | Clicks open dialogs now (UI Automation, works while Revit is blocked). |
| **Run Dynamo Graph** | Runs a `.dyn` file in automation mode. |
| **Model Warnings / Open Logs / About** | Utilities. |

## 3. Connect an agent

**Claude Code** (native HTTP):
```bash
claude mcp add --transport http arian-revit http://127.0.0.1:8765/mcp
```

**Claude Desktop / any stdio-only client** — uses the bundled zero-dependency bridge:
```json
{ "mcpServers": { "arian-revit": {
    "command": "node",
    "args": ["C:\\Users\\<you>\\AppData\\Roaming\\Autodesk\\Revit\\Addins\\2027\\ArianRevitMcp\\bridge\\arian-revit-mcp-bridge.mjs"],
    "env": { "ARIAN_REVIT_MCP_URL": "http://127.0.0.1:8765/mcp" } } } }
```
(or press **Connect Agent → Add to Claude Desktop config**).

**Cursor / VS Code / other HTTP-capable clients**: `{"mcpServers":{"arian-revit":{"type":"http","url":"http://127.0.0.1:8765/mcp"}}}`

Optional: restrict the tool list for small-context models with `?categories=Query,Architecture,Structure` on the URL
(or `ARIAN_REVIT_MCP_CATEGORIES` for the bridge). If you set an access token in *Settings*, pass `Authorization: Bearer <token>`.

Then just talk to the agent:
> “Create a new project, a 3-storey office 24×16 m with a 6 m structural grid, concrete frame, glazed façade on the south side, core with stairs, and give me an area schedule.”

The server sends the agent its operating manual (`instructions`), 14 guide resources (`revit://guide/…`) and ready-made prompts
(`start_architectural_project`, `phase2_design_development`, `model_structure`, `mep_design`, `create_family`, `produce_drawing_set`, `model_audit`).

## 4. What the agent can do

Full reference: [docs/TOOLS.md](docs/TOOLS.md). Highlights:

* **Architecture** – levels, grids, straight/curved/profile walls, curtain walls, floors, ceilings, roofs (footprint/extrusion), openings, doors & windows (auto-hosting), rooms, **room program → walls + rooms in one call**, building shell generator, stairs (auto-computed risers), railings, DirectShape masses & meshes, topography.
* **Structure** – grids, columns at grid intersections, beams along grids, braces, isolated/strip footings, foundation slabs, trusses, rebar & column cages, point/line/area loads, structural takeoff.
* **MEP** – duct/pipe/conduit/cable-tray runs with fittings, flex ducts, connectors API, connect/tap, MEP systems, electrical circuits, spaces, fixture layouts in rooms, open-connector QA, clash detection.
* **Families** – edit loaded families, create new ones from any template, parameters, formulas, reference planes, dimensions, extrusions/blends/revolves/sweeps/voids, connectors, flex test — or build a **complete parametric family from a JSON spec in one call** (`family_build_from_spec`).
* **Materials** – materials with appearance assets and patterns, layered wall/floor/roof/ceiling types, fill patterns, line styles.
* **Documentation** – views, sections, elevations, callouts, templates, graphics overrides, sheets, schedules, tags, dimensions, text, PDF/DWG/IFC export.
* **Management** – warnings (group/resolve), purge, health audit, links, worksets, phases, project parameters, units, quantities, undo.
* **Advanced** – `execute_csharp` (full Revit API in-process), `define_custom_tool` (teach the plugin new tools), `run_dynamo_graph`, `run_dynamo_python`.

## 5. Warnings, errors and dialogs

* `set_warning_policy` / ribbon toggle: **ignore warnings**, or **ignore + auto-resolve errors** (applies Revit's default resolution), for the agent's own transactions *and* for your manual work.
* `auto_dismiss_dialogs`: task dialogs are answered automatically (OK → Close → Cancel/No; "save changes?" → No).
* `get_dialog_log`, `dismiss_dialogs`: inspect and click dialogs even when Revit is blocked (UI Automation + window messages — the physical mouse is never touched).
* Last resort for dialogs that are not accessible: let a desktop-control agent click it.

## 6. Security

* Binds to **127.0.0.1 only**; rejects non-loopback `Host`/`Origin` (blocks browser/DNS-rebinding attacks); optional bearer token.
* `execute_csharp`/Dynamo run arbitrary code *as you*: only connect agents you trust. Disable code execution in **Settings** if needed.
* All edits are normal Revit transactions → **Ctrl+Z** works. Destructive tools default to dry-run or are annotated `destructiveHint`.

## 7. Development

```
Arian Revit MCP Plugin/
  src/Arian.RevitMcp/         main add-in: ribbon, HTTP/MCP server, dispatcher, helpers (Core/), knowledge base
  src/Arian.RevitMcp.Tools/   all tools (hot-reloadable assembly)
  bridge/                     stdio ↔ HTTP bridge (Node, no dependencies)
  scripts/                    install, redeploy (restart Revit), hot (reload tools), call/smoke test runners, scenarios
```

* A tool is a static method with `[Tool("name", """description…""")]`; the JSON schema is generated from the method signature (`@arg text` lines document arguments).
* `scripts\redeploy.ps1` – rebuild + restart Revit + auto-click the *Always Load* prompt. `scripts\hot.ps1` – rebuild tools only and hot-swap them into the running Revit (`reload_tools`).
* `node scripts/call.mjs <tool> '<json>'`, `node scripts/smoke.mjs scripts/scenarios/arch3.json`, `node scripts/gen_tools_doc.mjs`.

## 8. Known limits (Revit API)

Phases cannot be created through the API (use *Manage → Phasing*); Autodesk content libraries are not installed by default (create content with the family tools); MEP routing needs a template with routing preferences (use the `multi`/`mechanical`/`systems` templates); the API cannot create some UI-only objects (e.g. view templates from scratch, tags families).

See also: [docs/GUIDE-FA.md](docs/GUIDE-FA.md) (راهنمای فارسی).
