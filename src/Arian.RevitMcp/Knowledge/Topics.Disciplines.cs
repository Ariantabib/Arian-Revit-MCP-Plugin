namespace Arian.RevitMcp.Knowledge;

public static partial class Topics
{
    static void RegisterArchitecture()
    {
        Add("architecture", "Architecture recipes (LOD 100–350)", "Complete modelling sequence for buildings: site, levels, walls, openings, floors, roofs, rooms, stairs, finishes.", """
# Architecture — recipe for a complete building

## 0. Brief
Establish: building type, site/plot dimensions, number of storeys, storey height, program (room list with target areas), structural system, climate/style, local code. If unknown, state assumptions.

## 1. Project setup
`create_project(template='architectural' | 'multi')` → `set_project_info` → `set_project_units` → `create_building_levels(floors, floor_height_mm, name_pattern='Level {n}', include_roof=true)` (rename with `modify_level`: `Ground Floor`, `First Floor`, `Roof`). Add `Foundation`/`Basement` levels with `create_level` (negative elevations).

## 2. Grids (structural bays first!)
`create_grid_system(x_spacings_mm=[6000,6000,6000], y_spacings_mm=[5000,5000])`. Residential: 3.0–5.0 m bays; offices 7.2–9.0 m; parking 7.5–8.1 m. Align walls and columns to grids.

## 3. Wall types
`list_element_types(category='Walls')`; make real assemblies with `create_material` + `create_compound_type(kind='wall', ...)`. Suggested: `Arian EXT - Brick Cavity 350`, `Arian INT - Block 150`, `Arian INT - Partition 100`, `Arian CORE - Concrete 250`. Use `Generic - 200mm` only for LOD 100–200.

## 4. Walls
- Perimeter: `create_walls(points=[...], closed=true, wall_type, base_level, top_level, location_line='finish_face_exterior' or 'wall_centerline')`.
- Interior partitions: `create_walls` per run (or `create_wall`). Snap to grid lines and keep 100–300 mm clear to column faces.
- Curved: `create_curved_wall`. Gables/shaped: `create_wall_by_profile`. Walls to roof: `attach_wall`.
- Check: `get_view_image(plan)`; corners clean; `get_warnings`.

## 5. Floors, ceilings, roofs
- `create_floor(boundary, floor_type, level, offset_mm, openings=[stair/shaft polygons])` per level. Slab top at level elevation: use negative `offset_mm` equal to finish thickness when the type is thicker than the finish.
- Ceilings: `create_ceiling(boundary, ceiling_type, level, height_offset_mm=2700)`.
- Roof: `create_roof(boundary incl. overhang, roof_type, level, offset_mm=wall top, slope_degrees, sloped_edges=[...])`. Flat roof: `flat=true` (+ `create_floor` with slope for drainage via `slope_percent`). Barrel/vault: `create_roof_extrusion`.
- Shafts: `create_shaft_opening(boundary, base_level, top_level)`.

## 6. Doors & windows
Always read the real types first (`list_families(category='Doors')`). `place_family_instance({type:'M_Single-Flush: 0915 x 2134mm', point:[x,y], level})` — the nearest wall is auto-detected; or `host_id` + `distance_from_start_mm`. Windows: `elevation_mm`=sill height (900–1000 typ.; 450 for large glazing). Orient with `flip_hand` / `flip_facing`. Bulk: `place_family_instances`.
Rules of thumb: main entry 1000–1200 wide, interior doors 900 (bath 800), corridor ≥ 1200, windows ≥ 10% of floor area (habitable rooms), keep ≥ 150 mm from corners.

## 7. Rooms & areas
After enclosing: `create_rooms_automatically(level)` then name them with `set_room_properties` (or `create_rooms` with points+names+numbers), `tag_rooms`. Check `list_rooms` areas against the program. Open plans: `create_room_separation`.

## 8. Vertical circulation
`create_straight_stairs(start, direction_deg, base_level, top_level, width_mm, riser_mm=170, tread_mm=280)` computes everything; L/U stairs: `create_stairs(runs=[...])` (run length ≈ (risers−1)×tread). Cut the slab: `create_shaft_opening`. Railings: `create_railing(path=..., railing_type)` for balconies/landings (stairs include railings by default). L/U stairs with landings: `create_l_stairs` / `create_u_stairs`; ramps: `create_ramp`; eave details: `create_fascia`, `create_gutter`; `create_slab_edge`; skirting/cornice: `create_wall_sweep` (types: category 'OST_Cornices'); room finishes: `apply_room_finishes`; `create_areas_from_rooms`. DWG plans: `link_cad` (check units!), `list_cad_layers`, `convert_cad_double_lines_to_walls` (double-line walls) or `convert_cad_layer_to_walls` (single lines).

## 9. Fit-out & façade
Furniture/fixtures: `place_family_instance` (or generate with `family_*`). Curtain walls: use a Curtain Wall wall type with `create_wall`. Louvers/fins: `execute_csharp`.

## 10. Documentation
Views (`create_floor_plan`, `create_section`, `create_elevation`, `create_3d_view`), `create_dimension`, `tag_elements`, sheets, schedules (door/window/room finish/area), `get_view_image` for QA, `export_pdf`.

## Phase 1 vs Phase 2 shortcuts
- Phase 1 (schematic): steps 1–2, 4 (generic walls), 5 (generic slabs/roof), 6 (generic doors/windows), 7 (areas), 8 (basic stairs), views + area schedule.
- Phase 2 (design development): add 3 (real assemblies), full 6 with schedules, 8 (railings, openings), ceilings, finishes (room finishes), furniture, sections/elevations with dimensions, sheets, coordination with `structure` and `mep` topics.
""");
    }

    static void RegisterStructure()
    {
        Add("structure", "Structural modelling recipes", "Grids, columns, beams, slabs, foundations, walls, rebar, loads; sizing rules of thumb; family needs.", """
# Structure — recipe

## Before you start
- Template: `create_project(template='structural')` (or `multi`). In the plain Architectural template the only structural families are steel UC/UB — **create concrete families** with `family_build_from_spec` / `family_*` tools (see topic `family`): e.g. `Concrete-Rectangular-Column 400x400`, `Concrete-Rectangular Beam 300x500`, `Concrete-Isolated Footing 1500x1500x500`.
- Materials: `create_material('Concrete - C30', class Concrete)`, `Steel - S275` (class Metal).

## Sequence
1. **Grid & levels** (`create_grid_system`, `create_building_levels`).
2. **Foundation**: `create_isolated_footing` / `create_footings_under_columns(footing_type, elevation_offset_mm=-…)`, strip footings under bearing walls `create_strip_footing`, raft `create_foundation_slab`.
3. **Columns**: `create_columns_at_grid_intersections(column_type, base_level, top_level)` per storey range; remove unwanted with `skip_points`.
4. **Beams**: `create_beams_along_grids(beam_type, level, z_offset_mm)` creates a full frame; extra beams `create_beam` / `create_beams`. Cantilevers/edge beams with `create_beam`.
5. **Slabs**: `create_floor(..., structural=true)` with a concrete floor type from `create_compound_type(kind='floor')`; openings for stairs/shafts.
6. **Structural walls/cores**: `create_walls(..., structural=true)`.
7. **Bracing**: `create_beam(..., structural_type='brace')`.
8. **Reinforcement (LOD 400)**: `create_column_reinforcement(column_id, long_bar_type, stirrup_type, cover_mm, bars_per_face, stirrup_spacing_mm)`; free-form: `create_rebar(host_id, bar_type, points, normal, style, set_count, set_spacing_mm)`. See `list_rebar_types`. Also `create_beam_reinforcement` (bottom/top bars + stirrups with dense end zones), `create_slab_reinforcement` (mesh for floors/foundations/footings, bottom/top/both), `create_wall_reinforcement` (two curtains), `set_rebar_cover`. Rebar hosts must be STRUCTURAL (structural=true) and use a concrete material layer (create_compound_type). Early sizing: `predesign_sections`; load combos: `create_load_combination`; checks: `check_beam_clear_height`, `check_column_alignment`.
9. **Loads/analysis**: `create_point_load`, `create_line_load`, `create_area_load` (kN, gravity = −z; optional `load_case` from `list_load_cases`) on the *physical* element id — the analytical member/panel is created and associated automatically when missing; `set_structural_usage`. Trusses: `create_truss(truss_type='Standard' ...)` (types via `list_element_types('Structural Trusses')`); wall footings: `create_strip_footing`.
10. **QA**: `get_structural_summary`, `find_clashes(category_a='Structural Framing', category_b='Structural Columns')`, `get_warnings`.

## Preliminary sizing (rules of thumb — state them as assumptions)
- RC slab thickness ≈ span/28 (two-way) to span/24 (one-way), 150–250 mm typ.
- RC beam depth ≈ span/12 (width = depth/2 … /3, ≥ 250); column ≈ 300–500 for ≤ 5 storeys at 5–6 m bays (≈ 1% steel).
- Steel beam depth ≈ span/20, column HEB/UC by storey count; typical bay 6–9 m.
- Isolated footing ≈ 1.2–2.5 m square, depth 400–600 (soil dependent); strip footing width 600–1200.
- Cover: 25 mm interior slabs/walls, 40 mm columns/beams, 50–75 mm footings.
- Always coordinate with architecture: columns inside walls, beam depth within ceiling void (storey height − 2700 − slab).

## Naming
Types: `CONC-COL-400x400`, `CONC-BEAM-300x500`, `CONC-FTG-1500x1500x500`, `STL-COL-HEB200`. Level names `Foundation`, `Ground`, `L01`... Marks per element for schedules.
""");
    }

    static void RegisterMep()
    {
        Add("mep", "MEP (mechanical, electrical, plumbing) recipes", "Templates, systems, routing, fittings, equipment, circuits, spaces; troubleshooting fittings.", """
# MEP — recipe

## Setup
- Use `create_project(template='multi' | 'mechanical' | 'electrical' | 'plumbing' | 'systems')`: these templates contain system types, **routing preferences with fittings**, sizes and view templates. The plain Architectural template has *no* fittings → `create_duct_run` reports "failed to insert elbow".
- Link the architecture model: `link_revit_model(path)` (Revit link) so MEP is modelled around real walls, or model in the same file.
- Discover: `list_mep_types(kind='duct_type'|'pipe_type'|'mechanical_system_type'|'piping_system_type'|'conduit_type'|'cable_tray_type'|'wire_type')`, `list_families(category='Mechanical Equipment'|'Air Terminals'|'Lighting Fixtures'|'Electrical Fixtures'|'Plumbing Fixtures'|'Electrical Equipment')`.
- Missing equipment? Build it: `family_create(template='mechanical_equipment', ...)` + `family_add_box` + `family_add_connector(kind='duct'|'pipe'|'electrical', ...)` (see topic `family`).
- Spaces: `create_spaces_automatically(level)` (needs rooms/walls), set design values with `set_parameters` (airflow, loads).

## Mechanical (HVAC)
1. Equipment: `place_family_instance` (AHU, FCU, fans, diffusers). Use `get_connectors(id)` to see connector positions/sizes/system types.
2. Terminals in rooms: `place_fixtures_in_room(family_type, room_id, spacing_x_mm, spacing_y_mm, elevation_mm, host_id=ceiling)`.
3. Main duct: `route_from_connector(element_id, connector_index, end_point, kind='duct')` from the AHU, or `create_duct_run(points, duct_type, system_type, level, width_mm, height_mm, elevation_mm)` (elbows auto).
4. Branches: create branch run then `tap_into(branch_id, main_id)`; reducers: `connect_connectors(a, b, fitting='transition')`.
5. Terminals: `create_flex_duct` (start at the terminal connector, end at the main; then `connect_connectors(terminal, flex, fitting='none')` at the terminal side and `tap_into(flex, main)` at the main) or short ducts + `connect_connectors`.
6. System: `create_mep_system(domain='duct'|'pipe', system_type='Supply Air', name, element_ids=[equipment, terminals...])` — only *free* connectors whose own system classification equals the chosen system type are added (add the supply and return side of one equipment in two calls, e.g. 'Hydronic Supply' then 'Hydronic Return').
7. Size: `set_parameters` (Width/Height/Diameter) — keep velocities ≈ 4–6 m/s main, 2–3 m/s branch; keep clear height; coordinate with beams (`find_clashes`).
Heights: ducts 2400–2800 AFF (above ceiling void), keep ≥ 100 mm below slab/beam soffit.

## Piping / plumbing
`create_pipe_run(points, pipe_type, system_type='Domestic Cold Water'|'Sanitary'|'Hydronic Supply'..., diameter_mm)`; fixtures via families (`list_families(category='Plumbing Fixtures')`); connect with `route_from_connector(kind='pipe')` / `connect_connectors`. Gravity drains slope ≥ 1–2% — set via pipe `Slope` parameter with `set_parameters`. Systems: `create_mep_system(domain='pipe', ...)`.

## Electrical
- Panels/fixtures: place families; lighting grid in rooms: `place_fixtures_in_room` (lux target → spacing ≈ 2.5–3.5 m for offices).
- Circuits: `create_electrical_circuit(element_ids, system_type='power', panel_id)` (a freshly placed panelboard has no Distribution System: the tool picks a matching one and reports `panel_distribution_system_set`); read `list_electrical_panels`, `list_mep_systems(domain='electrical')`.
- Containment: `create_cable_tray_run`, `create_conduit_run`.
- Loads: set `Apparent Load`/`Voltage` parameters on families/circuits via `set_parameters`.

## QA
`find_open_connectors(category='Ducts'|'Pipes'|...)`, `find_clashes(category_a='Ducts', category_b='Structural Framing')`, `get_warnings`, `get_mep_system_info`.

**Phase-4 helpers:** sizing `calc_duct_size`, `calc_pipe_size` (flow or fixture units), `calc_space_loads` (W/m² + diffuser count), `auto_size_ducts`; layouts `place_lights_by_lux`, `place_sprinklers`, `place_sockets_along_walls`; routing `route_mep_auto` (collision-free orthogonal route avoiding structure/MEP; use dry_run first) and `create_sloped_pipe` (drainage fall in %); electrical `get_panel_loads`, `create_panel_schedule`; QA `check_mep_systems`, `check_mep_clashes`. All sizing is preliminary - state the assumptions.

## Naming
Systems `SA-01`, `RA-01`, `EA-01`, `CHWS-01`, `DCW-01`, `SAN-01`; circuits per panel `LP-1/3`.
""");
    }
}
