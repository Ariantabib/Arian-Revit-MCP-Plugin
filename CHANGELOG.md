# Changelog

## 1.5.0 (phases 5, 6, 7, 10)
- Families: `list_standard_families`/`create_standard_family` (19 ready parametric families incl. MEP connectors), `family_qa_report`, `family_export_type_catalog`, `family_import_types_csv`, `family_add_nested`. Flex test no longer scales helper (Ref_*) parameters; library shapes are flex-safe.
- Materials: `create_material_library` (Persian + global), `set_material_appearance` (colour/gloss/reflectivity/diffuse+bump textures), `set_material_physical`, `material_pass`, `set_view_visual_style`, `render_view_image`, `export_fbx` (fbx/obj/stl/step), `list_render_plugins`. Fixed: appearance-asset edits silently failed since 1.0 (needs an outer transaction).
- Documentation: view templates & filters, `create_sheet_set`/`arrange_viewports`/`create_sheet_index`/`add_revision`, `dimension_grids`, `dimension_openings`, `check_tag_overlaps`, `export_schedule` (xlsx formulas), Persian/Jalali tools, `check_building_rules`, `export_dwg_standard`, `export_sheets_pdf`.
- UI: dockable Arian Agent panel (live calls, image history, log, undo, cancel), Monitor ribbon panel, vector icons, Persian UI option, tool profiles, per-category visibility and read-only switches, cooperative Cancel.
- Unignorable Revit errors no longer leave a dialog blocking the agent (rolled back + logged when Auto Dialogs is on); every failure message is logged.

## 1.4.0 (phase 4: MEP depth)
- New: `calc_duct_size`, `calc_pipe_size`, `calc_space_loads`, `auto_size_ducts`, `create_sloped_pipe`, `route_mep_auto`, `place_lights_by_lux`, `place_sprinklers`, `place_sockets_along_walls`, `get_panel_loads`, `create_panel_schedule`, `check_mep_systems`, `check_mep_clashes`.
- Fix: the live-test scenarios no longer leave the warning policy at Off (that caused Revit warning dialogs to stay open); AutoDismissDialogs now defaults to on.

## 1.3.0 (phase 3: structure depth)
- New: `create_beam_reinforcement`, `create_slab_reinforcement`, `create_wall_reinforcement`, `set_rebar_cover`, `create_load_combination`, `predesign_sections`, `check_beam_clear_height`, `check_column_alignment`. Rebar tools validate the host and explain how to make it a valid (structural + concrete) host.

## 1.2.0 (phase 2: architecture depth)
- New: `create_slab_edge`, `create_fascia`, `create_gutter`, `create_wall_sweep` (cornice/skirting/reveal), `create_ramp`, `create_l_stairs`, `create_u_stairs` (multi-run stairs with automatic landings), `create_areas_from_rooms`, `apply_room_finishes`, `create_site_subregion`, `list_cad_layers`, `convert_cad_layer_to_walls`.
- New: `convert_cad_double_lines_to_walls` (DWG with double-line walls -> centerline walls with matching thickness types; tested on a real apartment DWG).
- Fix: multi-run stair landings (run elevations chained). Type categories: Slab Edges (OST_EdgeSlab), Fascias, Gutters, Wall Sweeps.

## 1.1.0
- Live-tested rebar, structural loads, trusses, electrical circuits, spaces, flex duct/pipe, MEP systems, curtain walls, dimensions, tags, family sweeps/revolves/blends/connectors, Dynamo (see docs/LIVE-TEST-REPORT.md); ~17 bugs fixed.
- New: `snapshot_3d`, `create_plan_from_rooms`, `create_building_shell`, `create_curtain_wall`, `create_floors_from_rooms`, `create_ceilings_from_rooms`, `copy_to_levels`, `number_elements`, `create_sheets`, `run_dynamo_python`, `define_custom_tool`, template lookup by name (`template='arian'`).
- Dialog dismisser handles Revit's embedded dialogs (window messages, never the physical mouse).
- Packaging: `scripts/package.ps1` (release zip with Install/Uninstall), `scripts/sign.ps1`, central version in `Directory.Build.props`.
- `scripts/run_all.mjs` runs every live scenario.

## 1.0.0
- First release: MCP server inside Revit 2027, Arian ribbon tab, ~258 tools, knowledge base, hot-reloadable tools assembly.
