# Arian Revit MCP Plugin — tool reference

333 tools. All lengths are millimetres, angles degrees, areas m², volumes m³.

| Category | Tools |
|---|---|
| Session | 16 |
| Query | 19 |
| Architecture | 54 |
| Structure | 28 |
| MEP | 34 |
| Family | 45 |
| Material | 17 |
| View | 67 |
| Modify | 20 |
| Manage | 25 |
| Advanced | 8 |

## Session

### `activate_document`
Brings an already-open document to the front (makes it the active UI document).

### `close_document` _(destructive)_
Closes a document. The active document cannot be closed through the API if it is the only open one with views; in that case the call reports it.

Arguments: save

### `create_project`
Creates a new Revit project from a template, saves it, and opens it as the active document. Returns the document title. If a project with the same file is already open it is closed (without saving) and replaced.

Arguments: template, save_path, name

### `get_guidance` _(read-only)_
Returns expert BIM guidance and tool recipes (markdown). Topics: index, workflow, architecture, structure, mep, family, materials, views, lod, scripting, warnings, troubleshooting. Use topic='index' to list all topics.

Arguments: topic

### `get_project_info` _(read-only)_
Project information (name, number, client, address, status, issue date, author, building name, organization) plus site location, true-north and units.

### `get_server_log` _(read-only)_
Last lines of the plugin log (useful to debug failed tool calls).

Arguments: lines

### `get_server_status` _(read-only)_
Plugin/server health: uptime, request count, current busy tool, queue depth, settings.

### `get_session_info` _(read-only)_
Information about the running Revit session: version, user, open documents, the active document/view, project units and plugin state. Call this first.

### `list_documents` _(read-only)_
Lists all open project and family documents.

### `list_templates` _(read-only)_
Lists available project templates (.rte): Autodesk metric templates plus any .rte in extra_dirs. Use the returned 'path' or a short alias (architectural, multi, structural, mechanical, electrical, plumbing, systems, construction) with create_project.

Arguments: extra_dirs

### `open_document`
Opens a Revit project (.rvt) or family (.rfa) file and activates it.

Arguments: **path**, activate, detach, audit

### `reload_tools`
Developer/maintenance: reloads the tool implementations (Arian.RevitMcp.Tools.dll) from disk WITHOUT restarting Revit and re-registers all tools. Use after a new build of the tools assembly.

Arguments: path

### `save_document`
Saves the document in place. For never-saved documents use save_document_as.

### `save_document_as`
Saves the document to a new path (.rvt for projects, .rfa for families).

Arguments: **path**, overwrite

### `set_project_info`
Sets Project Information fields (e.g. {"Project Name":"Villa A","Project Number":"2026-01","Client Name":"...","Project Address":"...","Project Status":"Schematic Design","Author":"..."}).

Arguments: **values**

### `sync_with_central`
Synchronise a workshared document with central.

Arguments: comment, relinquish

## Query

### `analyze_envelope` _(read-only)_
Building-envelope summary for energy/daylight discussions: gross exterior wall area by orientation (N/E/S/W relative to project north), window area and window-to-wall ratio per orientation, roof area, heated floor area (rooms), volume.

### `estimate_cost` _(read-only)_
5D quick cost estimate from the model quantities: walls, floors, roofs, ceilings, doors, windows, curtain walls, stairs, railings, columns, beams, footings. Prices come from @price_list (match = text found in the family/type name, unit = m2 | m3 | m | count, rate per unit) or built-in sample rates (USD-like, indicative only).

Arguments: price_list, categories, currency, csv_path

### `find_clashes` _(read-only)_
Clash detection: finds pairs of elements from set A that geometrically intersect elements from set B (e.g. ducts vs structural framing). Sets are defined by category, or explicit ids.

Arguments: category_a, category_b, ids_a, limit

### `find_elements_near` _(read-only)_
Finds elements within a radius of a point (uses bounding-box proximity).

Arguments: **point**, radius_mm, categories, limit

### `get_element` _(read-only)_
Full detail of one element: category/family/type/level, location, bounding box, host, phase, and ALL parameters (values in mm / m2 / m3 / deg).

Arguments: **id**, describe_parameters, include_type

### `get_element_geometry` _(read-only)_
Geometry summary of an element: bounding box, solid count, total volume (m3), surface area (m2), face/edge counts, planar face normals, plus the location curve/point. Use to verify what was built.

Arguments: **id**, detail_level

### `get_elements` _(read-only)_
Brief or detailed info for several element ids at once.

Arguments: **ids**, detail

### `get_model_overview` _(read-only)_
Bird's-eye summary of the active project: element counts per category, levels, grids, views, sheets, rooms, links, phases, warnings count. Call after opening a project to orient yourself.

### `get_parameters` _(read-only)_
Reads parameters of an element (instance + falls back to type). Pass names to read only those; otherwise all non-empty parameters are returned.

Arguments: **id**, names

### `get_quantities` _(read-only)_
Quantity takeoff computed directly (no schedule needed): counts and summed length/area/volume of a category grouped by type, level and/or material. Works for any discipline (walls, floors, columns, ducts, pipes, doors...).

Arguments: **category**, group_by, level

### `get_room_boundary` _(read-only)_
Returns a room's boundary segments (mm) — to place furniture, floors, ceilings or walls that follow the room.

Arguments: **room_id**

### `get_selection` _(read-only)_
Elements currently selected in the Revit UI.

### `list_categories` _(read-only)_
Lists Revit categories with their BuiltInCategory names. Use the names with query_elements / other tools.

Arguments: kind, with_counts, name_contains

### `list_element_types` _(read-only)_
Lists element TYPES of a category (wall types, floor types, door types, beam types, duct types...). For loadable families the type is a FamilySymbol ('Family: Type'). Includes useful type parameters (width, thickness, function...) when available.

Arguments: **category**, name_contains, parameters, limit

### `list_grids` _(read-only)_
All grids with their geometry (mm).

### `list_levels` _(read-only)_
All levels sorted by elevation (mm).

### `list_rooms` _(read-only)_
Lists rooms (or MEP spaces / areas) with number, name, level, area, volume and department.

Arguments: kind, level

### `query_elements` _(read-only)_
The main search tool. Finds elements by category / class / level / type / name / parameter values / location, with paging. Returns brief info (id, category, family, type, level) plus any parameters you request.

Arguments: category, class_name, level, type_name, name_contains, view, ids, filters, bbox_min, bbox_max, types, parameters, detail, limit, offset

### `select_elements` _(read-only)_
Selects elements in the Revit UI (and optionally zooms to them).

Arguments: **ids**, zoom

## Architecture

### `apply_room_finishes`
Writes finish information on rooms (Floor/Wall/Ceiling/Base Finish parameters) and optionally creates the finish floor and ceiling elements. Use for finish schedules.

Arguments: room_ids, level, floor_finish, wall_finish, ceiling_finish, base_finish, by_name

### `attach_wall`
Attaches the top (or base) of walls to a roof, floor, ceiling or level-based element so they follow its shape (e.g. walls up to a sloped roof).

Arguments: **wall_ids**, **target_id**, location

### `convert_cad_double_lines_to_walls`
Converts a DWG plan whose walls are drawn as DOUBLE lines (two parallel lines per wall — the usual case) into real walls: parallel line pairs become one centerline wall whose thickness equals the gap

Arguments: **cad_id**, **layers**, **wall_type**, **base_level**, top_level, height_mm, min_thickness_mm, max_thickness_mm, min_length_mm, max_walls, dry_run

### `convert_cad_layer_to_walls`
Traces the lines/arcs of a CAD layer (e.g. 'A-WALL') into real Revit walls — turns a DWG plan into a model. Duplicate/overlapping segments are skipped.

Arguments: **cad_id**, **layers**, **wall_type**, **base_level**, top_level, height_mm, min_length_mm, max_walls

### `create_areas_from_rooms`
Creates Area-plan areas (gross/net area scheme) from the rooms of a level: an area plan view, boundary lines following each room's finish boundary, and an Area with the room's name and number.

Arguments: **level**, scheme

### `create_building_levels`
Generates a stack of storey levels: ground + N floors with constant floor-to-floor height (optionally a different ground-floor height) and plan views.

Arguments: **floors**, floor_height_mm, first_floor_height_mm, base_elevation_mm, name_pattern, include_roof

### `create_building_pad`
Creates a building pad (flat graded area cut into topography) from a polygon at a level.

Arguments: **boundary**, **level**, pad_type

### `create_building_shell`
Creates a complete rectangular building shell in ONE call: levels, perimeter walls on every storey, floors, roof. Great starting point for Phase 1 massing/schematic models; refine afterwards.

Arguments: **width_mm**, **depth_mm**, **floors**, **wall_type**, **floor_type**, **roof_type**, floor_height_mm, roof_kind, roof_slope_deg, overhang_mm, origin, ground_floor_slab, create_levels, level_names, roof_level_name

### `create_ceiling`
Creates a ceiling from a boundary polygon.

Arguments: **boundary**, **ceiling_type**, **level**, height_offset_mm

### `create_ceilings_from_rooms`
Creates a ceiling in every room (finish boundary) of a level. @height_offset_mm Height above the level (default 2700).

Arguments: **level**, **ceiling_type**, room_ids, height_offset_mm

### `create_curtain_wall`
Creates a curtain wall (glazed facade) with a regular grid and optional mullions, along a line.

Arguments: **start**, **end**, **base_level**, top_level, height_mm, curtain_wall_type, vertical_spacing_mm, horizontal_spacing_mm, mullion_type, panel_type

### `create_curved_wall`
Creates an arc wall.

Arguments: **center**, **radius_mm**, **start_angle_deg**, **end_angle_deg**, **wall_type**, **base_level**, top_level, height_mm, base_offset_mm, top_offset_mm, location_line, structural

### `create_fascia`
Adds a fascia along the lowest (eave) edges of a roof or the edge of a floor. @roof_id host element. @fascia_type Type name (list_element_types category='OST_Fascia').

Arguments: **roof_id**, **fascia_type**

### `create_floor`
Creates a floor slab from a boundary polygon (flat, or sloped with slope_percent). Pass openings for holes (e.g. stair/shaft openings).

Arguments: **boundary**, **floor_type**, **level**, offset_mm, structural, openings, slope_percent, slope_direction

### `create_floor_opening`
Cuts an opening in an existing floor/roof/ceiling using a polygon (vertical cut).

Arguments: **host_id**, **boundary**, perpendicular_to_face

### `create_floors_from_rooms`
Creates a floor slab inside every room (finish boundary) of a level — the fast way to get finished floors/slabs that follow room shapes.

Arguments: **level**, **floor_type**, room_ids, offset_mm

### `create_grid`
Creates a straight grid line.

Arguments: **start**, **end**, name

### `create_grid_arc`
Creates a curved (arc) grid.

Arguments: **center**, **radius_mm**, **start_angle_deg**, **end_angle_deg**, name

### `create_grid_system`
Creates a complete orthogonal structural/architectural grid in one call.

Arguments: **x_spacings_mm**, **y_spacings_mm**, origin, x_labels, y_labels, extension_mm

### `create_gutter`
Adds a gutter along the lowest (eave) edges of a roof. @gutter_type Type name (list_element_types category='OST_Gutter').

Arguments: **roof_id**, **gutter_type**

### `create_l_stairs`
Creates an L-shaped stair (two runs + landing) with risers computed from the floor height. The first run starts at 'start' heading direction_deg; after the landing the stair turns 90 degrees to the turn side.

Arguments: **start**, **direction_deg**, **base_level**, **top_level**, turn, width_mm, riser_mm, tread_mm

### `create_level`
Creates a level at an elevation. Optionally creates the matching floor plan and ceiling plan views.

Arguments: **name**, **elevation_mm**, create_plan_views, create_ceiling_plan

### `create_levels`
Creates several levels at once (creates floor plan views for each).

Arguments: **levels**, create_ceiling_plans

### `create_mass_extrusion`
Creates a solid extruded from a polygon as a DirectShape — ideal for concept massing, site volumes, or any geometry without a family. Appears in the chosen category (Generic Models by default, or Mass).

Arguments: **boundary**, **height_mm**, base_z_mm, category, name, material

### `create_mesh_shape`
Creates arbitrary 3D geometry from a triangle/quad mesh as a DirectShape (imported sculpted forms, roofs, terrain chunks...).

Arguments: **vertices**, **faces**, category, name, material

### `create_plan_from_rooms`
Turns a room PROGRAM expressed as rectangles into a modelled plan: walls around/between all rectangles (shared edges become ONE wall, overlaps are merged), then a named Room in each rectangle. Ideal for schematic layouts: describe each room as origin + width x depth.

Arguments: **level**, **rooms**, **exterior_wall_type**, interior_wall_type, top_level, height_mm, create_floor, floor_type

### `create_railing`
Creates a railing/guardrail/balustrade along a path (balconies, edges, stair landings), or on a host stair/ramp.

Arguments: **railing_type**, level, path, host_id, elevation_mm, closed

### `create_ramp`
Creates a ramp as a SLOPED floor slab (the Revit API cannot create native Ramp elements) between two points, with optional side railings.

Arguments: **start**, **end**, **width_mm**, **base_level**, **floor_type**, rise_mm, top_level, railing_type, railings

### `create_roof`
Creates a footprint roof. All edges get the slope (hip roof) unless sloped_edges lists which edge indices slope (gable roofs: slope only the long edges, e.g. [0,2]). Edge i runs from point i to point i+1.

Arguments: **boundary**, **roof_type**, **level**, offset_mm, slope_degrees, sloped_edges, flat

### `create_roof_extrusion`
Creates an extruded roof from an OPEN profile drawn in a vertical plane (e.g. a barrel vault or gable ridge profile) swept along the plane normal.

Arguments: **plane_origin**, **plane_direction**, **profile**, **extrude_from_mm**, **extrude_to_mm**, **roof_type**, **level**

### `create_room`
Creates a room at a point inside an enclosed area (walls/separators must enclose it).

Arguments: **point**, **level**, name, number, department

### `create_room_separation`
Draws room separation lines (an invisible boundary) along a polyline on a level, to subdivide open plans into rooms.

Arguments: **level**, **points**, closed

### `create_rooms`
Creates several named rooms at once: [{point:[x,y], name, number, department}] on one level.

Arguments: **level**, **rooms**

### `create_rooms_automatically`
Places a room in EVERY enclosed area of a level that has no room yet.

Arguments: **level**

### `create_shaft_opening`
Creates a vertical shaft opening cutting through floors/roofs/ceilings between two levels (stairs, lifts, MEP shafts).

Arguments: **boundary**, **base_level**, **top_level**, top_offset_mm

### `create_site_subregion`
Creates a sub-region (paving, lawn, road area) on an existing toposurface from a polygon, optionally with a material.

Arguments: **boundary**, topo_id, material

### `create_slab_edge`
Adds a slab edge (fascia/thickened edge profile) along the perimeter of a floor.

Arguments: **floor_id**, **edge_type**, position

### `create_stairs`
Creates a component stair between two levels made of straight runs connected by automatic landings. Provide 1 run for a straight stair, 2 for L/U shaped, etc. Each run is a baseline from start to end (mm); the stair climbs from base_level to top_level.

Arguments: **runs**, **base_level**, **top_level**, stairs_type, width_mm, max_riser_mm, tread_depth_mm

### `create_straight_stairs`
Creates a straight stair and computes everything for you: riser count from the floor height, run length from the tread depth. The stair starts at 'start' (bottom riser) and climbs in direction_deg.

Arguments: **start**, **direction_deg**, **base_level**, **top_level**, width_mm, riser_mm, tread_mm, stairs_type

### `create_toposurface`
Creates a topography surface from 3D points (site terrain).

Arguments: **points**

### `create_u_stairs`
Creates a U-shaped (switchback) stair: two parallel runs joined by a landing (typical stair core). Run 1 starts at 'start' heading direction_deg; run 2 returns alongside on the turn side.

Arguments: **start**, **direction_deg**, **base_level**, **top_level**, turn, width_mm, gap_mm, riser_mm, tread_mm

### `create_wall`
Creates ONE straight wall. For many connected walls use create_walls.

Arguments: **start**, **end**, **wall_type**, **base_level**, top_level, height_mm, base_offset_mm, top_offset_mm, location_line, structural, flip

### `create_wall_by_profile`
Creates a wall from a vertical profile (gable ends, trapezoids, stepped walls). Profile points are given in the wall plane as [u, z]: u = distance along the wall direction from start (mm), z = absolute elevation (mm). The profile must be a closed polygon.

Arguments: **start**, **end**, **profile**, **wall_type**, **level**, structural

### `create_wall_opening`
Cuts a rectangular opening in a wall (without a door/window family).

Arguments: **wall_id**, **corner1**, **corner2**

### `create_wall_sweep`
Adds a horizontal (or vertical) sweep (cornice, skirting, plinth, band) or reveal (groove) to walls.

Arguments: **wall_ids**, **profile_type**, kind, distance_mm, side, from_top, vertical

### `create_walls`
Creates a chain of connected walls through the given points (polyline). Set closed=true to close the loop (e.g. a room outline or building perimeter). Corners are auto-joined by Revit.

Arguments: **points**, **wall_type**, **base_level**, top_level, height_mm, closed, base_offset_mm, top_offset_mm, location_line, structural

### `generate_program_from_brief` _(read-only)_
Turns a short residential brief into a physical PROGRAM: list of rooms with target areas (m²), level, zone (public/private/service) and adjacency hints — ready to feed create_plan_from_rooms or a manual design.

Arguments: bedrooms, total_area_m2, floors, master_suite, extras, style

### `join_walls`
Controls how a wall end joins its neighbour at a corner.

Arguments: **wall_id**, end, join_type, allow

### `modify_level`
Renames a level and/or changes its elevation.

Arguments: **level**, new_name, elevation_mm

### `place_family_instance`
Places ONE family instance of any kind (door, window, furniture, light, equipment, column, beam, generic model...). Handles level-based, wall-hosted, face-based, line-based and two-level families automatically from the family's placement type.

Arguments: **spec**

### `place_family_instances`
Places MANY family instances in one call (each item as in place_family_instance). Failures are reported per item and do not stop the rest.

Arguments: **items**

### `set_room_properties`
Sets name/number/department/occupancy/finish parameters on rooms.

Arguments: **ids**, name, number, department, parameters

### `set_wall_constraints`
Edits wall height/constraints/location line of existing walls.

Arguments: **ids**, base_level, top_level, height_mm, base_offset_mm, top_offset_mm, location_line, structural, room_bounding

### `tag_rooms`
Places room tags on all placed rooms of a level in its floor plan.

Arguments: **level**, view

## Structure

### `check_beam_clear_height` _(read-only)_
Reports beams whose soffit is lower than a minimum clear height above their level (default 2400 mm) — headroom check. @min_mm Minimum clear height.

Arguments: min_mm

### `check_column_alignment` _(read-only)_
Finds columns that are not stacked on a column below (within a tolerance) — possible load-path breaks / transfer situations. @tolerance_mm XY tolerance (default 100).

Arguments: tolerance_mm

### `create_area_load`
Applies a uniform area load over a polygon on an analytical surface (floor/slab). pressure_kn_per_m2 [fx,fy,fz] (gravity = negative z). The polygon is projected onto the analytical panel's plane. load_case: optional load case name.

Arguments: **host_id**, **boundary**, **pressure_kn_per_m2**, load_case

### `create_beam`
Places ONE structural framing member (beam/girder/joist/brace) between two points.

Arguments: **start**, **end**, **beam_type**, **level**, z_offset_mm, structural_type, parameters

### `create_beam_reinforcement`
Reinforces a straight RECTANGULAR concrete beam: bottom and top longitudinal bars plus stirrups with denser spacing near both supports (confinement zones).

Arguments: **beam_id**, **long_bar_type**, **stirrup_type**, cover_mm, bottom_bars, top_bars, stirrup_spacing_mm, dense_spacing_mm, dense_length_mm

### `create_beams`
Places many beams at once: [{start:[x,y], end:[x,y], type, z_offset_mm}] on a level.

Arguments: **beams**, **level**, default_type

### `create_beams_along_grids`
Generates a beam framing layout: one beam between every pair of consecutive grid intersections along each grid line (a classic column-line frame).

Arguments: **beam_type**, **level**, z_offset_mm, grid_names, direction

### `create_column_reinforcement`
Adds a standard reinforcement cage to a RECTANGULAR, vertical, axis-aligned concrete column: longitudinal bars (4 corners + extra along faces) and stirrups at spacing.

Arguments: **column_id**, **long_bar_type**, **stirrup_type**, cover_mm, bars_per_face, stirrup_spacing_mm

### `create_columns_at_grid_intersections`
Places structural columns at grid intersections (all grids, or only those named).

Arguments: **column_type**, **base_level**, **top_level**, grid_names, base_offset_mm, top_offset_mm, rotation_deg, skip_points

### `create_footings_under_columns`
Places an isolated footing under every structural column (or the given ones).

Arguments: **footing_type**, level, column_ids, elevation_offset_mm

### `create_foundation_slab`
Creates a foundation slab / raft / structural floor from a polygon. Use a floor type from category 'Structural Foundations' for a foundation slab (list_element_types category='Structural Foundations' / 'Floors').

Arguments: **boundary**, **floor_type**, **level**, offset_mm

### `create_isolated_footing`
Places an isolated (pad) footing at a point under a column.

Arguments: **point**, **footing_type**, **level**, elevation_offset_mm

### `create_line_load`
Applies a line (distributed) load along a line on an analytical member. force_kn_per_m [fx,fy,fz] in kN/m (gravity = negative z). load_case: optional load case name (list_load_cases).

Arguments: **host_id**, **start**, **end**, **force_kn_per_m**, load_case

### `create_load_combination`
Creates a load combination from load cases, e.g. 1.2 D + 1.6 L.

Arguments: **name**, **components**, state

### `create_point_load`
Applies a point load on an analytical structural member (column/beam/wall...) at a point. The analytical member is created and associated automatically if the physical element has none.

Arguments: **host_id**, **point**, **force_kn**, moment_knm, load_case

### `create_rebar`
Creates a rebar bar (or a set) from a polyline shape inside a concrete host (column, beam, wall, floor, footing).

Arguments: **host_id**, **bar_type**, **points**, **normal**, style, set_count, set_spacing_mm

### `create_slab_reinforcement`
Reinforces a rectangular (by bounding box) concrete floor, foundation slab or footing with an orthogonal bar mesh: X and Y bars in the bottom and/or top layer.

Arguments: **host_id**, **bar_type**, spacing_mm, cover_mm, layers, top_bar_type

### `create_strip_footing`
Creates a continuous wall footing under walls. @footing_type: continuous footing type name; @wall_ids: structural walls.

Arguments: **footing_type**, **wall_ids**

### `create_structural_column`
Places ONE structural column between two levels.

Arguments: **point**, **column_type**, **base_level**, **top_level**, base_offset_mm, top_offset_mm, rotation_deg, parameters

### `create_truss`
Creates a truss along a line using a truss type. @truss_type: truss type name or id (OST_Truss, e.g. M_Howe Flat Truss: Standard; list with list_element_types category OST_Truss); @level for the work plane.

Arguments: **start**, **end**, **truss_type**, **level**

### `create_wall_reinforcement`
Reinforces a straight concrete wall (shear wall): horizontal and vertical bars in both faces (two curtains).

Arguments: **wall_id**, **bar_type**, vertical_spacing_mm, horizontal_spacing_mm, cover_mm, both_faces

### `get_grid_intersections` _(read-only)_
Returns all intersection points between the straight grid lines (mm) with their grid names — to place columns/footings precisely.

Arguments: grid_names

### `get_structural_summary` _(read-only)_
Structural takeoff: for columns, framing, foundations, structural floors and walls — counts, total volume (m3) and length grouped by type and material. Use it to review the structural system.

### `list_load_cases` _(read-only)_
Lists structural load cases and natures.

### `list_rebar_types` _(read-only)_
Lists rebar bar types (diameters), shapes and cover types available.

### `predesign_sections` _(read-only)_
Rough pre-design (preliminary sizing, NOT a structural design) of concrete members from span and load, using code minimum-depth rules (ACI 318 / Iranian Mabhas 9 style): returns suggested beam, slab and column sizes in mm.

Arguments: **span_m**, load_kn_m2, floors, trib_area_m2, continuity, fc_mpa

### `set_rebar_cover`
Sets the rebar clear cover of concrete hosts (creates a cover type 'Cover NN mm' if needed). @host_ids Beams/columns/floors/walls/footings. @cover_mm Cover distance. Existing rebar keeps its geometry until re-created.

Arguments: **host_ids**, **cover_mm**

### `set_structural_usage`
Sets structural properties on framing/columns/walls/floors: structural material, usage (girder/horizontal bracing/joist/purlin/kicker bracing/vertical bracing...), analytical model on (creates + associates an analytical member/panel if missing) / off (deletes it), enable rebar cover.

Arguments: **ids**, material, usage, enable_analytical

## MEP

### `auto_size_ducts`
Resizes ducts to a design velocity from their calculated flow (Revit computes the flow once the system has terminals with airflow). Round ducts get a new diameter, rectangular ducts a new width (height kept).

Arguments: duct_ids, max_velocity_mps, flow_lps

### `calc_duct_size` _(read-only)_
Sizes an air duct from airflow: round diameter, or rectangular width for a given height, with velocity and friction loss (Darcy-Weisbach, galvanised steel).

Arguments: **airflow_lps**, max_velocity_mps, height_mm

### `calc_pipe_size` _(read-only)_
Sizes a water pipe from flow (or fixture units): nominal diameter, velocity and pressure drop per metre (Darcy-Weisbach).

Arguments: flow_lps, fixture_units, max_velocity_mps

### `calc_space_loads`
Estimates cooling/heating load and supply airflow of Spaces (rules of thumb W/m² by occupancy + people), writes them to the Space parameters when available and returns the diffuser count.

Arguments: space_ids, profile, delta_t_kelvin, max_lps_per_diffuser

### `check_mep_clashes` _(read-only)_
Finds geometric clashes between two categories (e.g. Ducts vs Structural Framing, Pipes vs Ducts). Uses Revit's solid intersection filter.

Arguments: **category_a**, **category_b**, limit

### `check_mep_systems` _(read-only)_
QA report of the MEP model: for ducts/pipes/conduits/trays the number of elements with open ends and elements not assigned to any system; per system the element count and open connectors.

### `connect_connectors`
Connects two elements. If connector indices are omitted the closest unconnected pair is used. Between straight ducts/pipes it adds the right fitting (elbow, transition or union) automatically; with equipment it connects directly.

Arguments: **element_a**, **element_b**, connector_a, connector_b, fitting

### `create_cable_tray_run`
Creates a cable tray run through points with elbows. @tray_type from list_mep_types cable_tray_type; width/height in mm.

Arguments: **points**, **tray_type**, **level**, width_mm, height_mm, elevation_mm, fittings

### `create_conduit_run`
Creates a conduit run through points with elbows. @conduit_type from list_mep_types conduit_type; @diameter_mm trade size.

Arguments: **points**, **conduit_type**, **level**, diameter_mm, elevation_mm, fittings

### `create_duct_run`
Creates a duct run through a polyline of points with elbow fittings at the bends. Points are [x,y,z] mm (z absolute) or [x,y] with elevation_mm above the level.

Arguments: **points**, **duct_type**, **system_type**, **level**, width_mm, height_mm, diameter_mm, elevation_mm, fittings

### `create_electrical_circuit`
Creates an electrical circuit from devices (receptacles, lighting fixtures, equipment) and optionally assigns it to a panel.

Arguments: **element_ids**, system_type, panel_id, load_name

### `create_flex_duct`
Creates a flexible duct through points (min 2) — typically the last connection from a duct to a diffuser. Tangents at both ends follow the first/last segment so the ends can be connected (connect_connectors, fitting 'none') or tapped into a main (tap_into).

Arguments: **points**, **flex_duct_type**, **system_type**, **level**, diameter_mm

### `create_flex_pipe`
Creates a flexible pipe through points. @flex_pipe_type @system_type @level @diameter_mm.

Arguments: **points**, **flex_pipe_type**, **system_type**, **level**, diameter_mm

### `create_mep_system`
Creates a named mechanical (duct) or piping system of a given system type and adds the open matching connectors of the given equipment/terminals to it (2027 API).

Arguments: **domain**, **system_type**, **name**, **element_ids**

### `create_panel_schedule`
Creates the panel schedule view of an electrical panel (circuit table) so it can be placed on a sheet. @panel_id Panel equipment id.

Arguments: **panel_id**

### `create_pipe_run`
Creates a pipe run through a polyline of points with elbows. Points [x,y,z] mm (z absolute) or [x,y] + elevation_mm.

Arguments: **points**, **pipe_type**, **system_type**, **level**, diameter_mm, elevation_mm, fittings

### `create_sloped_pipe`
Creates a pipe that falls at a given slope — drainage/sanitary lines. The end elevation is computed from the horizontal length.

Arguments: **start**, **end**, **pipe_type**, **system_type**, **level**, slope_percent, diameter_mm

### `create_space`
Creates one MEP Space at a point. Pass 'parameters' for design values (e.g. {'Design Supply Airflow': 100}).

Arguments: **point**, **level**, name, number, parameters

### `create_spaces_automatically`
Places MEP Spaces in every enclosed room-like area of a level (for HVAC / lighting analysis).

Arguments: **level**

### `create_tee_fitting`
Creates a tee between three connectors: ids of three elements (main end A, main end B, branch) near the same point. Prefer tap_into when branching off a continuous run.

Arguments: **main_a**, **main_b**, **branch**

### `find_open_connectors` _(read-only)_
QA: lists duct/pipe/conduit/tray elements that have unconnected ends (dangling runs) — typical modelling errors to fix before coordination. @category e.g. Ducts, Pipes, Conduits, Cable Trays, Duct Fittings.

Arguments: category, limit

### `get_connectors` _(read-only)_
Lists the MEP connectors of an element (duct/pipe/conduit/equipment/fitting/terminal): index, position, direction, shape, size, system type, flow and what each is connected to. Use the index with connect_connectors / route_from_connector.

Arguments: **id**

### `get_mep_system_info` _(read-only)_
Detailed info of a duct/pipe/electrical system: type, equipment, element count, flow and key parameters.

Arguments: **system_id**

### `get_panel_loads` _(read-only)_
Sums the connected load of the circuits fed by an electrical panel (apparent load, VA/kVA, circuit list) for a quick capacity check. @panel_id Panel equipment id.

Arguments: **panel_id**

### `list_electrical_panels` _(read-only)_
Lists electrical panelboards/switchboards with their circuits.

### `list_mep_systems` _(read-only)_
Lists MEP systems (mechanical, piping, electrical circuits) with type, base equipment and element count.

Arguments: domain

### `list_mep_types` _(read-only)_
Lists MEP types: duct_type, pipe_type, conduit_type, cable_tray_type, flex_duct_type, flex_pipe_type, mechanical_system_type, piping_system_type, wire_type, pipe_segment, duct_insulation, pipe_insulation...

Arguments: **kind**

### `place_fixtures_in_room`
Lays out a regular grid of fixtures (light fittings, air terminals, sprinklers, smoke detectors, speakers...) inside a room/space — the quick way to populate ceilings.

Arguments: **family_type**, **room_id**, spacing_x_mm, spacing_y_mm, margin_mm, elevation_mm, host_id

### `place_lights_by_lux`
Places ceiling lights in a room to reach a target illuminance (lumen method): N = E·A / (lumens · utilisation · maintenance), laid out on a regular grid.

Arguments: **room_id**, **family_type**, target_lux, lumens_per_fixture, utilization, maintenance, elevation_mm, host_id

### `place_sockets_along_walls`
Places wall sockets / switches / data outlets along the walls bounding a room at a regular spacing (hosted on the walls).

Arguments: **room_id**, **family_type**, spacing_mm, elevation_mm, end_offset_mm, max_count

### `place_sprinklers`
Places sprinkler heads in a room on a square grid sized from the coverage area per head (light hazard ≈ 20 m², ordinary ≈ 12 m²) and a maximum spacing.

Arguments: **room_id**, **family_type**, coverage_m2, max_spacing_mm, elevation_mm, host_id

### `route_from_connector`
Starts a duct/pipe directly from an equipment/terminal/fitting connector to an end point; size and system are inherited from the connector. The easiest way to connect an air terminal, fan coil, AHU, sink, etc.

Arguments: **element_id**, **connector_index**, **end_point**, kind, type

### `route_mep_auto`
Routes a duct / pipe / conduit / cable tray between two points avoiding structure and existing MEP: tries orthogonal routes at several elevations (fewest bends first) and creates the first collision-free one.

Arguments: **kind**, **start**, **end**, **type**, **system_type**, **level**, width_mm, height_mm, diameter_mm, clearance_mm, elevations_mm, avoid_walls, dry_run

### `tap_into`
Connects a branch duct/pipe INTO the side of a main duct/pipe with a tap/tee (takeoff) fitting. @branch_id: the branch whose open end is near the main; @main_id: the main run.

Arguments: **branch_id**, **main_id**

## Family

### `create_standard_family`
Builds a professional parametric family from the built-in library (see list_standard_families) and loads it into the project: named reference planes, labelled dimensions, formulas, material parameters, several types, connectors for MEP kinds, flex-tested.

Arguments: **kind**, name, load, extra_types

### `delete_family` _(destructive)_
Deletes a loaded family (and all its instances!).

Arguments: **family**

### `edit_family`
Opens a LOADED family for editing (like 'Edit Family' in Revit). Returns the family document title; pass it as 'document' to the family_* tools, then call family_load_into_project to push changes back.

Arguments: **family**

### `export_family`
Saves a loaded family out to an .rfa file.

Arguments: **family**, **path**

### `family_add_blend`
Adds a blend (loft between a bottom and a top profile, e.g. tapered legs, funnels, transitions). Both profiles are polygons [[x,y],...] (mm) with the same number of points, placed at z = base_z_mm and z = base_z_mm + height_mm.

Arguments: **bottom_profile**, **top_profile**, **height_mm**, base_z_mm, solid, name, material, material_parameter, subcategory

### `family_add_box`
Adds a rectangular block (solid or void) — convenience over family_add_extrusion. Box is centred at center [x,y] mm, size width (X) x depth (Y), from base_z_mm up to base_z_mm+height_mm. For parametric blocks use family_setup_dimensions first and pass lock_to_planes + end_plane.

Arguments: **width_mm**, **depth_mm**, **height_mm**, center, base_z_mm, solid, name, material, material_parameter, subcategory, lock_to_planes, end_plane, end_parameter, detail_levels

### `family_add_connector`
Adds an MEP connector on the face of a form, so the family connects to ducts/pipes/conduit/cable trays/circuits. The face is the planar face of form_id nearest to point [x,y,z] mm.

Arguments: **kind**, **form_id**, **point**, system_type, shape, diameter_mm, width_mm, height_mm, flow_direction

### `family_add_cylinder`
Adds a vertical cylinder (solid or void) centred at [x,y] mm with radius and height, from base_z_mm. For round holes in a block use solid=false.

Arguments: **radius_mm**, **height_mm**, center, base_z_mm, solid, name, material, material_parameter, subcategory, end_parameter, detail_levels

### `family_add_dimension`
Creates a dimension between two reference planes and (optionally) labels it with a family parameter — the label makes the planes move when the parameter changes. Use equal=true with a third plane to create an EQ-constrained symmetric dimension (e.g. Left–Center–Right).

Arguments: **plane_a**, **plane_b**, label, plane_c, equal, offset_mm

### `family_add_extrusion`
Adds an extrusion (solid or void) from a 2D profile drawn on an axis-aligned plane. THE basic modelling tool for families: boxes, panels, legs, frames, profiles...

Arguments: plane, offset_mm, profile, circle_center, circle_radius_mm, start_mm, end_mm, solid, name, material, material_parameter, subcategory, start_parameter, end_parameter, lock_to_planes, start_plane, end_plane, detail_levels

### `family_add_material`
Creates a material inside the family document (so geometry can use it) or reuses it if present. Prefer a 'material' family parameter so users can change it in the project.

Arguments: **name**, color, transparency

### `family_add_nested`
Loads another family (.rfa) into the open family document and places an instance of it (a nested family) — e.g. a handle inside a door or a leg inside a table.

Arguments: **family_path**, type_name, point, shared

### `family_add_parameter`
Adds a family parameter. Dimension parameters (data_type=length) are THE way to make a family parametric: drive geometry with them via family_add_dimension / family_add_extrusion(end_parameter=...).

Arguments: **name**, data_type, instance, group, value, formula

### `family_add_reference_plane`
Adds a named reference plane — the skeleton that makes a family parametric. Axes: 'x' = plane at constant X (a vertical plane seen as a vertical line in plan: Left/Right), 'y' = constant Y (Front/Back), 'z' = constant Z (horizontal plane: Top/Bottom, seen in elevations).

Arguments: **name**, **axis**, **offset_mm**, extent_mm, reference

### `family_add_revolve`
Adds a revolved form (vases, round columns with profile, domes, pipe fittings). The profile is a closed polygon [[r,z],...] in the XZ plane (r = distance from the vertical axis through [axis_x,axis_y], z = height), revolved around the vertical axis by angle_deg (default 360).

Arguments: **profile_rz**, angle_deg, axis_xy, solid, name, material, material_parameter, subcategory

### `family_add_sweep`
Adds a sweep: a closed 2D profile swept along a 3D polyline path (rails, frames, pipes, mouldings, handrails). The path is a polyline of 3D points [[x,y,z],...] in mm (all points must lie in ONE plane); the profile polygon [[u,v],...] mm is placed at the start of the path perpendicular to the first segment: v = the path-plane normal (UP for a path in a horizontal plane; for a straight vertical path it is +X), u = to the right when looking along the first segment with v up (u = t x v).

Arguments: **path**, **profile**, solid, name, material, material_parameter, subcategory

### `family_add_symbolic_lines`
Adds 2D symbolic lines (visible in the plan/elevation view only, not in 3D) — typical for plan symbols, door swings, equipment outlines. Points [[x,y],...] mm in the given view.

Arguments: **points**, closed, view

### `family_add_type`
Creates a family type (e.g. '900 x 2100') and optionally sets parameter values on it in one go (lengths mm).

Arguments: **name**, values, make_current

### `family_associate_parameter`
Binds an element's parameter to a family parameter (e.g. an extrusion's 'Extrusion End' to 'Height', a form's 'Material' to a material parameter, 'Visible' to a yes/no parameter). Element parameter: name or BuiltInParameter.

Arguments: **element_id**, **element_parameter**, **family_parameter**

### `family_build_from_spec`
Builds a COMPLETE parametric family in one call from a declarative spec: parameters (with formulas), types, and shapes (boxes, cylinders, prisms) whose positions are EXPRESSIONS of the parameters (e.g. "-Width/2 + 50"). The tool creates the reference planes, labelled dimensions, formulas, locked extrusions/voids, types, connectors, runs a flex test, saves, and loads the family into the project.

Arguments: **spec**

### `family_close`
Closes a family document.

Arguments: save

### `family_create`
Creates a NEW family document from a template and saves it. Returns the 'document' title to pass to every other family_* tool. Start every from-scratch family here.

Arguments: **name**, template, folder, category

### `family_delete_element` _(destructive)_
Deletes an element (form, reference plane, dimension, connector...) from the family by id.

Arguments: **ids**

### `family_delete_parameter` _(destructive)_
Removes a family parameter.

Arguments: **parameter**

### `family_delete_type` _(destructive)_
Deletes a family type.

Arguments: **name**

### `family_export_type_catalog`
Writes a Revit type catalog (.txt next to the .rfa, same base name) for the open family so users can load only the types they need. @path Output path (default: next to the family file). Length/angle/number/integer/text type parameters are exported.

Arguments: path

### `family_flex_test`
Quality check: cycles every family type and optionally scales length parameters (min/max factor) to verify the geometry regenerates without errors — the 'flex' test every good family must pass. Returns pass/fail per case.

Arguments: factors, parameters

### `family_get_geometry_summary` _(read-only)_
Volume/bounding box of all solid forms and void forms of a family — to sanity-check dimensions after building.

### `family_get_info` _(read-only)_
Full state of a family document: category, parameters (with formulas), types with their values, reference planes, forms (extrusions, sweeps...), connectors.

### `family_import_types_csv`
Creates family types from a CSV file (Family Types from Excel): the first row holds parameter names (first column = type name), following rows the values (lengths in mm). Existing types are updated. @path CSV path (UTF-8, comma or semicolon separated).

Arguments: **path**

### `family_load_into_project`
Loads the family document into a project so it can be placed. Saves first if it has a path.

Arguments: project, overwrite, close

### `family_open`
Opens an existing .rfa file for editing (without loading it into a project). Returns the document title.

Arguments: **path**

### `family_qa_report`
Quality report of the open family document: category, parameters/types, unnamed reference planes, geometry without material binding, missing subcategories, import instances (should be avoided), nested families, connectors, file size, and a flex test. Returns issues + a 0-100 score.

### `family_save`
Saves the family document (optionally to a new path).

Arguments: path

### `family_set_form_properties`
Edits an existing form: visibility per detail level/view, solid<->void, subcategory, material, name. Visibility flags apply to model elements: coarse/medium/fine detail levels and the view types (plan_rcp, front_back, left_right).

Arguments: **id**, coarse, medium, fine, plan_rcp, front_back, left_right, solid, subcategory, material, material_parameter, name, visible_parameter

### `family_set_formula`
Sets (or clears with empty string) the formula of a family parameter, e.g. 'Width / 2 - Leg_Size'.

Arguments: **parameter**, **formula**

### `family_set_parameter_value`
Sets a family parameter value for a type (or the current type). Lengths in mm.

Arguments: **parameter**, **value**, type_name

### `family_set_properties`
Family-level settings. @category change the family category. @always_vertical, @work_plane_based, @shared, @cut_with_voids (door/window opening cut), @room_calculation_point. @part_type e.g. for MEP fittings.

Arguments: category, always_vertical, work_plane_based, shared, room_calculation_point, part_type

### `family_setup_dimensions`
One-call parametric skeleton for a box-like family: creates type parameters Width/Depth/Height (names configurable), reference planes Left/Right/Front/Back/Top at the default sizes, symmetric EQ dimensions about the centre planes, and labelled overall dimensions. Afterwards build geometry with family_add_extrusion(lock_to_planes=true, end_plane='Top') and it will follow the parameters.

Arguments: width_mm, depth_mm, height_mm, width_param, depth_param, height_param

### `get_family_info` _(read-only)_
Describes a loaded family: category, placement type, types, and the parameters available on its types and instances (read from the first type / an instance).

Arguments: **family**

### `list_families` _(read-only)_
Lists loaded families with their types. Use to find what can be placed with place_family_instance.

Arguments: category, name_contains, in_place, include_types, limit

### `list_family_templates` _(read-only)_
Lists family templates (.rft): aliases usable with family_create plus all template files.

### `list_standard_families` _(read-only)_
Lists the ready-made parametric families that create_standard_family can build (furniture, casework, plumbing, lighting, HVAC equipment, electrical, columns, doors, windows).

### `load_family`
Loads a family (.rfa) into the project. Returns the family and its types, ready for place_family_instance.

Arguments: **path**, overwrite

### `search_family_library` _(read-only)_
Searches .rfa family files on disk (Autodesk content library, plus extra folders) by words in the file path.

Arguments: **query**, extra_dirs, limit

## Material

### `create_compound_type`
Creates (or redefines) a layered wall/floor/roof/ceiling type with named layers and materials — e.g. a 300 mm cavity wall or a 150 mm slab with screed + finish. Duplicates based_on, then sets the layers from OUTSIDE to INSIDE (for walls: exterior first; for floors/roofs: top first).

Arguments: **kind**, **name**, **layers**, based_on

### `create_fill_pattern`
Creates a custom hatch fill pattern (drafting or model) from one or more line families (e.g. a brick bond = lines at 0° and 90°; crosshatch = 45° and 135°).

Arguments: **name**, **lines**, target

### `create_line_style`
Creates a new line style (subcategory of Lines) with colour, weight and optional pattern, usable for detail lines.

Arguments: **name**, color, weight, pattern

### `create_material`
Creates a material (or updates it if the name exists), with colour, transparency, graphics patterns and a rendering appearance asset that uses the colour.

Arguments: **name**, color, transparency, shininess, smoothness, material_class, surface_pattern, surface_pattern_color, cut_pattern, cut_pattern_color, copy_from

### `create_material_library`
Creates a curated material library in the project: Persian/Iranian materials (reinforced concrete, clay brick, facing brick, travertine, granite, plaster, ceramic, paint, ST37 steel, rock wool, ...) and common global ones — each with colour, class, cut pattern and, where relevant, structural/thermal physical properties.

Arguments: only, physical

### `create_simple_type`
Creates a new simple (non-layered) type for categories like stairs, railings, door/window types of a family: duplicates an existing type and sets parameters in one go.

Arguments: **based_on**, **name**, parameters

### `delete_material` _(destructive)_
Deletes a material.

Arguments: **material**

### `duplicate_material`
Duplicates a material under a new name.

Arguments: **material**, **new_name**

### `get_material` _(read-only)_
Full details of a material (colour, transparency, shininess, assets, patterns, all parameters).

Arguments: **material**

### `get_type_layers` _(read-only)_
Returns the layer stack (material, thickness, function) of a wall/floor/roof/ceiling type.

Arguments: **type**

### `list_fill_patterns` _(read-only)_
Lists fill patterns (drafting and model).

Arguments: target

### `list_materials` _(read-only)_
Lists project materials.

Arguments: name_contains, material_class, limit

### `material_pass`
Automatic 'material pass': assigns materials to the layers of wall / floor / roof / ceiling types from rules, by type-name substring and (optionally) layer function.

Arguments: **rules**, kinds, dry_run

### `set_element_material`
Paints/assigns a material on elements: sets the 'Material' parameter of families/types (instance first, then type), or paints the faces of DirectShapes/any solid via the Paint tool.

Arguments: **ids**, **material**, paint_faces

### `set_material_appearance`
Edits the RENDER appearance of a material (own copy of the appearance asset is made if it is shared): colour, glossiness, reflectivity, diffuse texture image, bump image, texture scale.

Arguments: **material**, color, glossiness, reflectivity, diffuse_texture, bump_texture, bump_amount, texture_scale_mm

### `set_material_physical`
Assigns Structural (physical) and Thermal property sets to a material, from a preset or custom values — used by analysis, energy and quantity workflows.

Arguments: **material**, preset, density_kg_m3, youngs_modulus_mpa, poisson, compressive_mpa, yield_mpa, tensile_mpa, conductivity_w_mk, specific_heat_j_kg_k

### `set_type_layers`
Replaces the layer stack of an existing wall/floor/roof/ceiling type (outside -> inside).

Arguments: **type**, **layers**

## View

### `add_revision`
Adds a revision (e.g. 'Issued for construction') and optionally assigns it to sheets so it appears in their title-block revision schedules.

Arguments: **description**, date, issued_by, issued_to, sheets, issued

### `apply_view_filter`
Adds a view filter to a view or view template with graphic overrides.

Arguments: **view**, **filter**, visible, color, line_weight, transparency, halftone, solid_fill

### `apply_view_template`
Applies a view template to views. @views View names/ids. @template Template name. Use template='none' to detach.

Arguments: **views**, **template**

### `arrange_viewports`
Re-lays out the viewports already on a sheet in a grid inside the title-block area. @sheet Sheet number/name/id. @columns Number of columns (default: square-ish). @reserve_right_mm @margin_mm as in create_sheet_set.

Arguments: **sheet**, columns, reserve_right_mm, margin_mm

### `check_tag_overlaps`
Finds tags (and text notes) overlapping each other in a view, and optionally fixes them by nudging the later one upward until it is free. @view View. @fix true to move overlapping tags. @gap_mm Gap kept between tags when fixing (default 5 paper mm × scale).

Arguments: **view**, fix, gap_mm

### `create_3d_view`
Creates a 3D view: isometric (default) or perspective. For perspective give eye and target points.

Arguments: name, perspective, eye, target

### `create_callout`
Creates a callout (detail window) from a parent plan/section view over a rectangle.

Arguments: **parent_view**, **min**, **max**, name, kind

### `create_detail_lines`
Draws 2D detail lines (or a closed polygon) in a view.

Arguments: **view**, **points**, closed, line_style

### `create_dimension`
Creates a linear dimension string in a view between elements: grids, walls (centreline or faces), columns/families (centre planes) or levels (in elevation/section views).

Arguments: **view**, **ids**, direction, offset_mm, faces

### `create_drafting_view`
Creates a drafting view (2D detailing only).

Arguments: **name**, scale

### `create_elevation`
Creates an exterior/interior elevation view looking in a compass direction from a point (places an elevation marker in the given plan view).

Arguments: **point**, direction, plan_view, scale, name

### `create_filled_region`
Creates a filled region (hatch/solid fill) from a polygon in a view (e.g. insulation hatch, colour blocks). @region_type: filled region type name (list_annotation_types kind=filled_region).

Arguments: **view**, **boundary**, region_type

### `create_floor_plan`
Creates a floor plan (or ceiling/structural/area plan) view for a level.

Arguments: **level**, kind, name, view_template, scale, detail_level

### `create_revision_cloud`
Creates a revision cloud around a polygon in a view (creates a revision if none exists).

Arguments: **view**, **boundary**, description

### `create_schedule`
Creates a schedule (door schedule, room finish schedule, wall type takeoff, equipment list, quantity takeoff...).

Arguments: **category**, **name**, **fields**, filters, sort, itemize, totals_fields, grand_total, sheet

### `create_section`
Creates a section view cut along a line in plan. The section looks to the LEFT of the line direction start->end (flip=true reverses).

Arguments: **start**, **end**, **bottom_mm**, **top_mm**, depth_mm, name, flip

### `create_sheet`
Creates a sheet.

Arguments: **number**, **name**, titleblock

### `create_sheet_index`
Creates a Sheet List schedule (Sheet Number, Sheet Name, Current Revision...) — the drawing index for the title/cover sheet. @name Schedule name (default 'Sheet Index'). @extra_fields More fields e.g. ['Drawn By','Sheet Issue Date'].

Arguments: name, extra_fields

### `create_sheet_set`
Creates sheets for a list of views and lays the viewports out automatically inside the title-block area (grid, centred, no overlap checks beyond size warnings).

Arguments: **views**, titleblock, number_prefix, start_number, per_sheet, sheet_name, reserve_right_mm, margin_mm

### `create_sheets`
Creates several sheets at once with their views placed. Views are placed in a simple row layout (left to right) at the sheet centre line; adjust later with place_view_on_sheet if needed.

Arguments: **sheets**, titleblock

### `create_text_note`
Places a text note in a view.

Arguments: **view**, **point**, **text**, text_type, width_mm

### `create_text_note_fa`
Places a Persian (RTL) text note: right-aligned, RTL-marked, Persian digits optional, using a dedicated text type 'Arian Persian' (font Tahoma by default — it shapes Persian correctly in Revit).

Arguments: **view**, **point**, **text**, digits, font, size_mm, width_mm

### `create_view_filter`
Creates a view filter (rule-based) for graphic overrides: elements of the given categories whose parameter matches a condition.

Arguments: **name**, **categories**, **parameter**, **value**, operator, is_length

### `create_view_template`
Creates a view template from an existing view's settings (graphics, visibility, filters, detail level), optionally changing scale / detail level / visual style.

Arguments: **name**, **from_view**, scale, detail_level, visual_style

### `delete_schedule` _(destructive)_
Deletes a schedule view.

Arguments: **schedule**

### `delete_view` _(destructive)_
Deletes a view.

Arguments: **view**

### `dimension_grids`
Dimensions the grids of a plan view automatically: a chain along each grid direction plus an overall dimension. @view Plan view. @offset_mm Distance of the first dimension line from the grid ends (default 1500).

Arguments: **view**, offset_mm

### `dimension_openings`
Dimensions the doors/windows of straight walls in a plan view: a chain from wall end → opening → opening → wall end along each wall that hosts openings.

Arguments: **view**, offset_mm, side, exterior_only

### `duplicate_view`
Duplicates a view.

Arguments: **view**, mode, new_name

### `export_dwg` _(read-only)_
Exports views or sheets (names/ids/sheet numbers) to DWG. Returns the folder and the files written.

Arguments: **folder**, **views**, file_name

### `export_dwg_standard` _(read-only)_
Exports views/sheets to DWG with a standard layer mapping and clean settings.

Arguments: **folder**, **views**, layer_standard, merged_views, file_name

### `export_fbx`
Exports a 3D view to FBX (Twinmotion, Lumion, Unreal, 3ds Max, Blender workflows) or other 3D formats. @view 3D view name/id (default active 3D view). @format fbx | obj | stl | step (default fbx). @path Output path (default Documents/ArianRenders).

Arguments: view, format, path

### `export_ifc` _(read-only)_
Exports the model to IFC (IFC2x3 or IFC4).

Arguments: **folder**, file_name, version

### `export_pdf` _(read-only)_
Exports views/sheets to PDF.

Arguments: **folder**, file_name, views, combine

### `export_schedule` _(read-only)_
Exports a schedule to CSV, XLSX (real Excel file with live formulas) or JSON. Optionally adds calculated columns (Excel formulas over other columns) and total rows — the way to do formulas/calculated values that Revit schedules cannot.

Arguments: **schedule**, **path**, format, calculated, totals

### `export_sheets_pdf` _(read-only)_
Exports sheets to PDF, selecting them by number prefix. @folder @number_prefix e.g. 'A-' (default all sheets) @combine One combined PDF (default false = one PDF per sheet) @file_name Name of the combined file.

Arguments: **folder**, number_prefix, combine, file_name

### `get_schedule_data` _(read-only)_
Reads a schedule's table as rows of text (exactly as displayed in Revit) — to verify counts, check data or feed other analysis.

Arguments: **schedule**, max_rows

### `get_view_image` _(read-only)_
Renders a view to a PNG and returns it as an image you can LOOK at. Use after building to visually verify the model (plans, sections, elevations, 3D). Defaults to the active view.

Arguments: view, width_px, fit_to_page, crop_to_content, shaded

### `hide_elements`
Hides (or unhides) specific elements in a view.

Arguments: **view**, **ids**, hide

### `isolate_elements`
Temporarily isolates elements in a view (hides everything else). Use reset=true to restore.

Arguments: **view**, ids, reset

### `jalali_date` _(read-only)_
Converts a Gregorian date to the Persian (Jalali/Shamsi) calendar. @date ISO date 'yyyy-MM-dd' (default today). @persian_digits Use ۱۲۳ digits. @style numeric (1405/07/24) | long (۲۴ مهر ۱۴۰۵).

Arguments: date, persian_digits, style

### `list_annotation_types` _(read-only)_
Lists text note types, dimension types, tag types of a category, filled region types, line styles.

Arguments: kind, category

### `list_render_plugins` _(read-only)_
Detects installed Revit rendering/visualisation add-ins (Enscape, Twinmotion, Lumion, V-Ray, Corona, D5, Cloud rendering...) from their .addin manifests and tells how to hand a model over to them.

### `list_schedulable_fields` _(read-only)_
Lists the field names available for a schedule of a category (to use with create_schedule).

Arguments: **category**

### `list_schedules` _(read-only)_
Lists schedule views.

### `list_sheets` _(read-only)_
Lists sheets with their placed views.

### `list_titleblocks` _(read-only)_
Lists title block types available for sheets.

### `list_view_templates` _(read-only)_
Lists view templates (name, view type, scale, discipline) and how many views use each.

### `list_views` _(read-only)_
Lists views.

Arguments: view_type, name_contains, include_templates

### `open_view` _(read-only)_
Makes a view the active view in the Revit window.

Arguments: **view**

### `override_graphics`
Overrides how elements or a whole category are drawn in a view (colour, line weight, transparency, halftone, surface/cut fill colour & pattern). Great for colour-coding (rooms by department, fire ratings, phases).

Arguments: **view**, element_ids, category, line_color, fill_color, line_weight, transparency, halftone, solid_fill, clear

### `place_image_on_sheet`
Places a raster image (e.g. a render from render_view_image) on a sheet or drafting view at a given size — for presentation boards and cover sheets.

Arguments: **sheet**, **path**, **center**, width_mm

### `place_view_on_sheet`
Places a view (or schedule) on a sheet.

Arguments: **sheet**, **view**, center

### `render_view_image`
Exports a view as a high-resolution PNG/JPG file using Revit's own renderer for the view's visual style (use set_view_visual_style 'realistic' first for materials/textures). @view View name/id (default active). @width_px Pixel size of the longest side (default 3000). @format png | jpg. @path Output file (default: Documents/ArianRenders).

Arguments: view, width_px, format, path

### `set_category_visibility`
Shows or hides whole categories in a view (e.g. hide 'Furniture' in a structural plan).

Arguments: **view**, **categories**, **visible**

### `set_section_box`
Sets the 3D section box of a 3D view to the given model-space box and enables it.

Arguments: **view**, **min**, **max**, enabled

### `set_sheet_dates_jalali`
Writes the Jalali (Persian) date into sheets' issue-date field. @sheets Sheet numbers (or ['all']). @date ISO Gregorian date (default today). @persian_digits Use Persian digits (default true). @parameter Sheet parameter to fill (default 'Sheet Issue Date').

Arguments: **sheets**, date, persian_digits, parameter

### `set_view_crop`
Sets the crop box of a view to a rectangle in model coordinates (plans/elevations) and turns cropping on.

Arguments: **view**, **min**, **max**

### `set_view_environment`
Sets the look of a 3D view's environment: sun shadow intensity, sky/background and sun position — what turns a flat model picture into an architectural render.

Arguments: **view**, shadows, background, sky_color, horizon_color, ground_color, color, date, hour

### `set_view_properties`
Changes view settings.

Arguments: **view**, name, scale, detail_level, display_style, view_template, discipline, crop_active, crop_visible, phase, phase_filter, underlay_level

### `set_view_range`
Sets the view range of a plan view (all mm, measured from the view's associated level unless noted).

Arguments: **view**, top_offset_mm, cut_plane_offset_mm, bottom_offset_mm, view_depth_offset_mm

### `set_view_visual_style`
Sets a view's visual style: wireframe | hidden_line | shaded | consistent_colors | realistic. 'realistic' shows material appearance (textures) in the 3D view and in exported images. @view View name/id (default active).

Arguments: **style**, view

### `snapshot_3d`
One-call 3D picture of the whole model (or of selected elements) from a chosen direction — the quickest way to LOOK at what you built. Creates/reuses a 3D view named 'Arian Snapshot', frames the model, renders shaded and returns the PNG.

Arguments: direction, element_ids, width_px, hide_categories

### `sun_study`
Renders a sun/shadow study of a 3D view: one image per time of day on a given date, with shadows on (project location is used for the sun position).

Arguments: view, date, hours, folder, width_px

### `tag_elements`
Tags elements in a view with their category tag (door numbers, wall types, room tags are separate: tag_rooms).

Arguments: **view**, ids, category, tag_type, add_leader, offset_mm

### `to_persian_digits` _(read-only)_
Converts Latin digits in a text to Persian digits (0123 → ۰۱۲۳). @text Text.

Arguments: **text**

### `zoom_to_elements` _(read-only)_
Zooms the active view to the given elements.

Arguments: **ids**

## Modify

### `change_element_type`
Changes the type of elements (e.g. swap a wall type, a door type, a beam size).

Arguments: **ids**, **type_name**

### `copy_elements`
Copies elements, optionally several times along a vector (linear array: copy i is offset by i*vector).

Arguments: **ids**, **vector**, count

### `copy_to_levels`
Copies elements (e.g. a typical floor: walls, doors, floors, columns...) to other levels, offsetting vertically by the level elevation difference — the quick way to build multi-storey buildings.

Arguments: **ids**, **source_level**, **target_levels**

### `cut_geometry`
Cuts element A with element B (void cut), e.g. a void family cutting a wall. Set uncut=true to remove the cut.

Arguments: **cutting_id**, **cut_id**, uncut

### `delete_elements` _(destructive)_
Deletes elements (and anything that depends on them, e.g. hosted doors with a wall). Returns the ids that were actually deleted. Undoable in Revit.

Arguments: **ids**

### `duplicate_type`
Duplicates an element type (wall type, floor type, family type, ...) under a new name. Then edit it with set_parameters / set_type_layers.

Arguments: **type_name**, **new_name**, category

### `group_elements`
Creates a model group from elements.

Arguments: **ids**, name

### `join_geometry`
Joins the geometry of two elements (e.g. wall to floor, beam to column). Set unjoin=true to unjoin.

Arguments: **id_a**, **id_b**, unjoin

### `mirror_elements`
Mirrors elements about a vertical plane through a point.

Arguments: **ids**, **plane_point**, **plane_normal**, copy

### `move_elements`
Moves elements by a vector.

Arguments: **ids**, **vector**

### `number_elements`
Assigns sequential numbers to a text parameter (default 'Mark') of all elements of a category — e.g. door marks D01, D02..., window marks, column marks. Ordered by position (left-to-right then bottom-to-top) or by type.

Arguments: **category**, prefix, start, digits, parameter, order, level

### `pin_elements`
Pins or unpins elements.

Arguments: **ids**, pinned

### `rename_element`
Renames an element/type/level/view/grid/room name etc.

Arguments: **id**, **name**

### `rotate_array`
Radial array: creates copies of elements rotated step by step around an axis (e.g. columns around a circle).

Arguments: **ids**, **center_point**, **step_deg**, **count**

### `rotate_elements`
Rotates elements about a vertical (or custom) axis.

Arguments: **ids**, **center_point**, **angle_deg**, axis_direction

### `set_element_workset`
Moves elements to a workset (worksharing).

Arguments: **ids**, **workset**

### `set_parameter_on_elements`
Sets ONE parameter to the same value on many elements.

Arguments: **ids**, **name**, **value**

### `set_parameters`
Writes parameters on one element. Numbers for dimensional parameters are mm / deg / m2 / m3; strings are parsed by Revit using project units (e.g. "1500 mm"); booleans for Yes/No; ElementId parameters accept an id or a name (materials, levels).

Arguments: **id**, **values**

### `set_parameters_bulk`
Writes parameters on many elements: [{id, values:{name:value}}...]. Use for numbering, marks, comments, type changes at scale.

Arguments: **updates**

### `ungroup`
Ungroups a group instance.

Arguments: **group_id**

## Manage

### `audit_model_health` _(read-only)_
BIM health check: warnings, in-place families, imported CAD, unused types, views not on sheets, groups, links, design options, file size — with a short list of recommended fixes.

### `check_building_rules` _(read-only)_
Checks the model against configurable building-code rules and returns violations with element ids — a first-pass compliance check (default limits follow typical Iranian residential practice: Mabhas 4 general requirements, stairs, daylight; verify against the current edition and project use).

Arguments: profile, limits

### `create_project_parameter`
Creates a project parameter (via a shared parameter definition) bound to categories — for BIM data such as 'Fire Rating', 'Cost Code', 'Zone', 'Assembly Code'.

Arguments: **name**, **categories**, data_type, instance, group

### `create_workset`
Creates a user workset (project must be workshared).

Arguments: **name**

### `dismiss_dialogs`
Clicks a button on any Revit dialog that is currently open, via UI Automation (works even when Revit is blocked by a modal dialog). Safe default button order: OK, Close, Continue, Ignore, Yes, Cancel, No.

Arguments: preferred_buttons, list_only

### `enable_worksharing`
Enables worksharing on the project (creates default worksets).

### `export_bcf_clashes` _(read-only)_
Detects clashes between two categories and writes them as a BCF 2.1 file (issues for Navisworks, Solibri, BIMcollab, Revizto...). Each clash becomes a topic with both elements' ids/names.

Arguments: **category_a**, **category_b**, **path**, limit

### `generate_model_report`
Writes a self-contained HTML report of the model: project info, levels, room/area table, quantities by category, building-rule check, warnings, envelope summary, estimated cost and images of chosen views embedded. Opens anywhere (no internet needed).

Arguments: **path**, title, views, include_cost, rules_profile

### `get_dialog_log` _(read-only)_
Recent Revit dialogs / message boxes that appeared (and how they were answered). Use when a tool call times out.

### `get_warnings` _(read-only)_
Lists the model's warnings grouped by description with counts and (optionally) the element ids involved. Use before issuing a model / to find modelling problems (overlapping walls, duplicate instances, room not enclosed, ...).

Arguments: details, limit

### `link_cad`
Links a DWG/DXF file into a view (typically a plan) at the origin.

Arguments: **path**, view, units, current_view_only

### `link_revit_model`
Links another Revit model into the project (origin-to-origin by default).

Arguments: **path**

### `list_cad_layers` _(read-only)_
Lists the layers of an imported/linked CAD file with the number of curves on each — use before convert_cad_layer_to_walls. @cad_id: id from list_links.

Arguments: **cad_id**

### `list_design_options` _(read-only)_
Lists design option sets and options.

### `list_links` _(read-only)_
Lists linked Revit models and CAD links.

### `list_phases` _(read-only)_
Lists project phases and phase filters.

### `list_project_parameters` _(read-only)_
Lists project/shared parameters with their bound categories and instance/type nature.

### `list_worksets` _(read-only)_
Lists user worksets (workshared projects).

### `purge_unused` _(destructive)_
Removes unused families, types, materials, line patterns, view filters etc. (Revit's Purge Unused). Runs several passes since purging reveals more unused items.

Arguments: dry_run, exclude

### `resolve_warnings`
Automatically cleans up safe classes of warnings.

Arguments: strategy, description_contains, dry_run

### `set_element_phase`
Sets the phase an element was created in and/or demolished in (existing/new/demolished workflow for renovations).

Arguments: **ids**, created, demolished

### `set_project_location`
Sets the project's geographic location (latitude/longitude in degrees) and place name.

Arguments: **latitude**, **longitude**, place_name

### `set_project_units`
Sets the project display units. @length mm | cm | m | ft | in | ft_in. @area m2 | ft2. @volume m3 | ft3. @angle deg. Decimal places via @length_decimals.

Arguments: length, area, volume, length_decimals

### `set_warning_policy`
Configure how Revit warnings and errors are handled automatically (applies to the agent's tools AND to the user's own work).

Arguments: **policy**, auto_dismiss_dialogs

### `undo` _(read-only)_
Undo the last Revit operation(s) via the UI command (posts the Undo command; takes effect right after this call returns).

Arguments: steps

## Advanced

### `define_custom_tool`
Teach the plugin a NEW reusable tool: you supply a name, description, JSON-schema for its arguments and C# code. The tool is compiled, registered immediately (it appears in tools/list as '[Custom] name'), persisted on disk and available in every future session. Use it for repeated workflows (e.g. 'create_apartment_unit', 'place_fire_extinguishers', 'number_rooms_by_level').

Arguments: **name**, **description**, **code**, input_schema, members, read_only

### `delete_custom_tool` _(destructive)_
Deletes an agent-defined custom tool.

Arguments: **name**

### `execute_csharp` _(destructive)_
Compile and run C# code INSIDE Revit with full Revit API access — the escape hatch for anything the dedicated tools don't cover (custom geometry, bulk edits, Dynamo-like parametric generation, API features).

Arguments: **code**, members, args, transaction

### `get_dynamo_status` _(read-only)_
State of the last run_dynamo_graph / run_dynamo_python call: queued | running | started (run triggered) | finished (evaluation done; node_issues lists nodes in warning/error state) | error, with Dynamo's message and (for run_dynamo_python) the script result.

### `list_custom_tools` _(read-only)_
Lists agent-defined custom tools with their code.

### `list_dynamo_graphs` _(read-only)_
Lists .dyn graph files in a folder (default: the Dynamo samples shipped with Revit and the user's Documents/Dynamo).

Arguments: folder

### `run_dynamo_graph`
Runs a Dynamo for Revit graph (.dyn) on the active document — for parametric/generative modelling you or the user already have as Dynamo graphs. Runs asynchronously right after this call returns (Dynamo needs a genuine Revit command context); poll with get_dynamo_status.

Arguments: **path**, show_ui

### `run_dynamo_python`
Runs Python (CPython 3 / PythonNet3, Dynamo's engine) inside a generated Dynamo graph with full Revit API access — the 'Dynamo way' to script Revit. Executes asynchronously after this call returns; poll get_dynamo_status (it returns the script_result).

Arguments: **code**, show_ui

