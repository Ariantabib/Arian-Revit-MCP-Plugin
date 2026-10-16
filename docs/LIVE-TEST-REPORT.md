# Live test report — Revit 2027 (2026-10-16)

Groups that were previously only compile-checked were exercised against a running Revit 2027 and fixed where needed.
Every group has a smoke scenario in `scripts/scenarios` (run: `node scripts/smoke.mjs scripts/scenarios/<name>.json`).
The scenarios create their own project (`create_project`, re-runnable), so run them one at a time on a scratch Revit session.

| Scenario | Covers | Result |
|---|---|---|
| `struct3.json` | `create_column_reinforcement`, `create_rebar` (bar set + stirrup set), `list_load_cases`, `create_point_load` / `create_line_load` / `create_area_load`, `create_truss`, `create_strip_footing`, `set_structural_usage` | 19/19 pass |
| `mep4.json` | `create_spaces_automatically`, `create_space`, `place_fixtures_in_room`, `create_electrical_circuit`, `list_electrical_panels`, `create_flex_duct`, `create_flex_pipe`, `connect_connectors`, `tap_into`, `create_mep_system` (duct + pipe), `list_mep_systems`, `find_open_connectors` | 32/32 pass |
| `arch5.json` | `create_roof_extrusion`, `create_curtain_wall` (+`mullion_type`, `panel_type`), `create_wall_opening` (4 walls), `create_dimension` (wall centrelines, wall faces, levels, grids), `tag_elements` (ids / category / leader / wrong tag type / missing args) | 25/25 pass |
| `warnpol1.json` | `set_warning_policy` off / ignore_warnings / ignore_and_resolve_errors with a **real Revit ERROR** ("Constraints are not satisfied": dimension-locked wall pair, one wall pinned) — rolled back with policies 1–2, auto-resolved (`auto_resolved_errors`) with policy 3 | 10/10 pass |
| `fam4.json` | `family_add_blend`, `family_add_revolve`, `family_add_sweep` (incl. void), `family_add_symbolic_lines` (plan + elevation), `family_add_connector` for pipe / electrical / conduit / cable tray / duct, load into a project and read the connectors | 26/26 pass |
| `dyn2.json` | `run_dynamo_python`, `run_dynamo_graph` on the shipped *Revit Geometry Creation* samples, `get_dynamo_status` (now reports `finished` + `node_issues`) | 8/8 pass |

Verified by hand (no scenario, because they write outside the model or change the file):

* `export_pdf` (combined / separate, views and sheets), `export_dwg`, `export_ifc` (IFC4 + IFC2x3) — files written and listed in the result.
* `link_revit_model`, `link_cad`, `list_links`.
* `enable_worksharing`, `create_workset`, `list_worksets`.
* `resolve_warnings dry_run=false` (deleted an overlapping wall via `description_contains='overlap'`), `purge_unused dry_run=false` (~350 unused types/families removed, model intact).

## Bugs found and fixed

| Tool | Problem | Fix |
|---|---|---|
| `create_point_load` / `create_line_load` / `create_area_load` | API-created elements have **no analytical member**, so every call failed ("no analytical member") | Analytical member / panel is created and associated on demand (column, beam, brace, wall, floor). Area-load polygon is projected onto the panel plane (no more "exceeds host boundaries" warning). New optional `load_case`. |
| `set_structural_usage` `enable_analytical` | used a parameter that does not exist in 2027 (silently ignored) | creates / deletes the analytical association |
| `create_truss` | `Find.Type<TrussType>` throws (TrussType is not a native element class) | looks the type up through category `OST_Truss`; `list_element_types('Structural Trusses')` now includes truss types |
| `create_spaces_automatically` | `NewSpaces2` with a null view threw `ArgumentNullException` | uses the level's floor plan; clear error if none |
| `create_space` | area was read before regeneration (0 m²) | regenerates first |
| `create_electrical_circuit` | `SelectPanel` failed ("panel and circuit do not match") for freshly placed panelboards (empty Distribution System); circuit name never set; voltage in internal units | auto-selects a matching distribution system (reported as `panel_distribution_system_set`), sets `LoadName`, returns volts / VA |
| `list_mep_systems`, `list_electrical_panels` | voltage in internal units; `panel_name` null | volts; panel name + circuit numbers |
| `create_flex_duct` / `create_flex_pipe` | end connectors always pointed along +X, so `tap_into` failed | start/end tangents follow the first/last segment |
| `create_mep_system` | adding equipment with both supply and return connectors failed | only connectors whose classification matches the system type are added |
| `export_pdf` / `export_dwg` | sheets could not be addressed by sheet number | sheet number / name / id accepted; result lists the written files |
| `create_curtain_wall` | `mullion_type='Family: Type'` produced an illegal type name (`:`); cached type ignored the base wall type / panel | names sanitised and unique per base type + mullion + panel |
| `create_dimension` | wall centreline dimension always failed (`LocationCurve.Reference` is null) | centreline reference taken from the wall geometry |
| `tag_elements` | wrong `tag_type` left an untyped tag behind and returned a cryptic error | tag removed, clear message |
| `family_add_sweep` | **every** sweep failed for paths not in a vertical plane starting at the origin: Revit wants the profile drawn in the XY plane at the family origin (local coordinates), not at the path start | profile is mapped to Revit's frame (local y = sketch-plane normal, local x = n × t); documented: `u` = right, `v` = up |
| `create_project` | failed when a project with the same file was still open (re-running a scenario) | old document is closed (not saved) and replaced |
| `get_dynamo_status` / `DynamoRunner` | state stayed `started` forever | hooks the workspace `EvaluationCompleted` event → `finished`, node count and the nodes in warning/error |

## Not supported / limits found

* `create_rebar` builds straight-segment bars (no hooks/bends); `create_column_reinforcement` is rectangular, vertical, axis-aligned columns only (by design).
* Truss types: only the types already in the project (`M_Howe Flat Truss: Standard` in the structural template); there is no API to create new truss families here.
* Dynamo samples (`AddIns\DynamoForRevit\Samples\en-US`): only the four *Revit Geometry Creation* graphs build elements in an empty project (model lines, reference points, forms). *Floors and Framing*, *Structural Framing*, *Place Families…*, *Color*, *Import Solid*, *Adaptive Component Placement* and `GD/*` select rooms/floors/families from the sample RVTs, which Autodesk does not install — they evaluate with node warnings and create nothing. `Basics_*` / `Core_*` run clean (no Revit elements).
* The first Dynamo run after Revit starts takes ~20 s (Dynamo initialises, then opens the graph on a second command).
* `AutoStartServer` is **off** by default (`%APPDATA%\Arian\RevitMcp\settings.json`); the Arian tab only exists once a project is open. `scripts/redeploy.ps1` waits for `/health`, so set `"AutoStartServer": true` for the dev loop.
