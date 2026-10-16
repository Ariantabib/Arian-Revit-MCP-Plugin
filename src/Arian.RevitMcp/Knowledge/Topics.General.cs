namespace Arian.RevitMcp.Knowledge;

public static partial class Topics
{
    static partial void RegisterMore()
    {
        RegisterWorkflow();
        RegisterArchitecture();
        RegisterStructure();
        RegisterMep();
        RegisterFamily();
        RegisterMisc();
    }

    static void RegisterWorkflow()
    {
        Add("workflow", "How to work with this Revit server", "START HERE: operating rules, verification loop, units, error recovery, tool selection.", """
# Working with the Arian Revit MCP server

## Golden rules
1. **Units**: every length is **millimetres**, angles **degrees**, areas **m²**, volumes **m³**. Points are `[x,y,z]` mm in project coordinates (internal origin). If z is omitted, level-based tools use the level elevation.
2. **Orient first**: `get_session_info` → `get_model_overview` → `list_levels` / `list_grids` / `list_element_types` / `list_families` / `list_materials`. Never guess type names — read them. Type names look like `Family: Type` (`Basic Wall: Generic - 200mm`); the part after the colon alone also works when unambiguous.
3. **Build in dependency order**: levels → grids → structure → walls → floors/roofs/ceilings → openings (doors/windows) → rooms → stairs/railings → fixtures/furniture/equipment → MEP → annotation → views/sheets/schedules.
4. **Verify after each step**: `get_view_image` (you receive a real PNG — LOOK at it), `query_elements`, `get_element`, `get_warnings`. Fix before continuing. A wrong model built on a wrong model is expensive.
5. **Batch when you can**: `create_walls` (polyline), `create_grid_system`, `create_columns_at_grid_intersections`, `create_beams_along_grids`, `place_family_instances`, `set_parameters_bulk`, `create_rooms`. Avoid hundreds of single calls.
6. **Every write is a separate Revit transaction** (undoable with Ctrl+Z, or `undo`). Warnings are returned as `revit_warnings`; the plugin may auto-ignore them (see `set_warning_policy`).
7. **Never delete or overwrite user content without being asked.** Prefer new types/names (`Arian ...`) over editing shared types. `purge_unused` and `delete_*` are destructive — default to dry-run/ask.

## Quick starts (one call, then refine)
- Program of rooms → plan: `create_plan_from_rooms` (rectangles with names → walls + named rooms). Whole box building: `create_building_shell`. Frame: `create_grid_system` + `create_columns_at_grid_intersections` + `create_beams_along_grids`.
- If the user owns a custom template (`list_templates` shows e.g. "Arian Architectural Template V3.1"), create the project from it (`create_project(template='arian')`) and use ITS wall/floor/door types and levels (`list_levels`, `list_element_types`).

## Tool selection map
| Need | Tool |
|---|---|
| Find things | `query_elements`, `get_element`, `find_elements_near`, `get_room_boundary` |
| Read/write data | `get_parameters`, `set_parameters`, `set_parameters_bulk`, `create_project_parameter` |
| Create architecture | `create_walls`, `create_floor`, `create_roof`, `create_ceiling`, `place_family_instance`, `create_straight_stairs`, `create_railing`, `create_rooms_automatically` |
| Create structure | `create_grid_system`, `create_columns_at_grid_intersections`, `create_beams_along_grids`, `create_isolated_footing`, `create_foundation_slab`, `create_rebar`, `create_point_load` |
| Create MEP | `create_duct_run`, `create_pipe_run`, `create_conduit_run`, `create_cable_tray_run`, `route_from_connector`, `connect_connectors`, `create_mep_system`, `create_electrical_circuit`, `place_fixtures_in_room` |
| Make new content | `family_create` + `family_*` tools, `family_build_from_spec`, `create_compound_type`, `create_material` |
| See | `get_view_image`, `create_3d_view`, `create_section`, `create_floor_plan` |
| Document | `create_sheet`, `place_view_on_sheet`, `create_schedule`, `create_dimension`, `tag_elements`, `export_pdf` |
| Quality | `get_warnings`, `resolve_warnings`, `audit_model_health`, `find_clashes`, `find_open_connectors`, `purge_unused` |
| Anything else | `execute_csharp` (full Revit API) — see topic `scripting` |

## When something fails
- Read the error: the plugin returns the Revit message and a hint. Typical causes: wrong type name, element not found, geometry too short (< 2 mm) or self-intersecting, wrong level, family needs a host.
- `get_server_log` shows details. If a tool call times out, Revit is probably blocked by a dialog: `get_dialog_log`, then `dismiss_dialogs`. Consider `set_warning_policy` with `ignore_and_resolve_errors`.
- If a dialog cannot be dismissed by the API (some Revit-internal windows), tell the user — as a last resort a desktop-automation (computer-use) agent can click it.

## Communicating with the user
Reply in the user's language. State assumptions (storey height, wall type, grid spacing). Show the picture (`get_view_image`) after major steps. Ask when program, area, code or structural system are genuinely unknown.
""");

        Add("units", "Units, coordinates and conventions", "Millimetres, degrees, levels, sign conventions, plan orientation.", """
# Units & conventions
- API units: **mm, degrees, m², m³**. Revit's internal unit is decimal feet; the plugin converts everything. In `execute_csharp` use `U.Mm(x)` (mm→feet) and `U.ToMm(x)`.
- Plan orientation: +X = east (right), +Y = north (up). Angles are counter-clockwise from +X.
- Walls are drawn left→right of their direction; the *exterior* side is on the **left of the drawing direction when walking along the wall** for clockwise-drawn perimeters... To be safe draw building perimeters **clockwise** (as `create_walls` examples: (0,0)→(10000,0)→(10000,8000)→(0,8000)) and check with `get_view_image`; use `flip` / `set_wall_constraints` if the exterior face is wrong.
- Wall `location_line`: `wall_centerline` (default), `finish_face_exterior`, `finish_face_interior`, `core_*`.
- Elevations are absolute from the project base point. Level-relative offsets are passed as `*_offset_mm`.
- Element ids are integers; names are case-insensitive.
- Typical metric values: floor-to-floor 3000–3600 (residential 3000–3200, office 3600–4000), clear ceiling 2700, door 900×2100, window sill 900, stair riser 160–180 / tread 260–300, slab 150–250, exterior wall 250–400, interior partition 100–150.
""");

        Add("lod", "LOD 100–500 and design phases", "What to model at each level of development; mapping to schematic (Phase 1) and design development (Phase 2).", """
# Level of Development (LOD) — what to model

| LOD | Meaning | Architecture | Structure | MEP |
|---|---|---|---|---|
| 100 | Concept | Masses (`create_mass_extrusion`), areas, site | none / grid | none |
| 200 | Approx. geometry (Schematic design) | Walls (generic types), floors, roof, simple doors/windows (generic), rooms with names/areas, stairs, levels, grids | Grid, columns & slabs with approximate sizes, core walls | Systems as zones, main equipment placeholders |
| 300 | Precise geometry (Design development) | Real wall/floor/roof types with layers, real door/window types, stairs/railings, ceilings, finishes, furniture layout, schedules, sections, elevations, sheets | Designed sections for columns/beams/slabs/foundations, structural walls, openings coordinated | Routed ducts/pipes/cable trays sized, equipment, terminals, panels, fixtures, circuits |
| 350 | + interfaces / connections | Details, junction conditions, tagged assemblies | Connections, bracing | Hangers/supports, penetrations |
| 400 | Fabrication/assembly | Shop-level components | Rebar, steel connections, anchor bolts | Fabrication parts, spools |
| 500 | As-built | Verified field conditions | | |

## Typical Iranian architectural project phases (mapping)
- **فاز ۱ (طرح اولیه / schematic design)** ≈ LOD 100–200: site & context, concept masses, program areas (rooms with area), plans of every floor, main elevations, 1–2 sections, 3D views; walls/floors/roofs as simple generic types; doors/windows placed; area schedule. Deliver: plans, elevations, sections, 3D images, room/area schedule.
- **فاز ۲ (طرح تفصیلی / design development)** ≈ LOD 300: coordinated structure (grid, columns, beams, slabs, foundations), wall/floor/roof assemblies with real layers & materials, full door/window schedule, stairs & railings, ceilings, finishes, furniture, MEP main routes & equipment, detailed sections/elevations, dimensions & tags, sheets, schedules (door, window, room finish, quantities).
- Each phase ends with: `get_warnings` cleanup, `audit_model_health`, sheets with views, and schedules.

Use `get_guidance` topics `architecture`, `structure`, `mep` for per-discipline recipes.
""");
    }

    static void RegisterMisc()
    {
        Add("materials", "Materials, assets, layered types", "Create materials, patterns, wall/floor/roof types with layers.", """
# Materials & layered types
- **Phase-6 helpers**: `create_material_library` (Persian + global materials with colours/patterns/physical assets), `set_material_appearance` (colour, glossiness, reflectivity, diffuse + bump texture images, texture scale), `set_material_physical` (concrete/steel/timber/... structural + thermal assets), `material_pass(rules)` assigns materials to type layers by name/function. Rendering: `set_view_visual_style('realistic')`, `render_view_image`, `export_fbx` (fbx/obj/stl/step), `list_render_plugins`.
- `list_materials` / `get_material` to see what exists (a default template has ~100).
- `create_material(name, color, material_class, surface_pattern, cut_pattern, transparency...)` — creates a material with its own rendering appearance asset coloured accordingly. Name professionally: `Concrete - Cast-in-place C30`, `Brick - Facing Red`, `Insulation - XPS`, `Paint - White`, `Glass - Clear`.
- Pattern names: use `list_fill_patterns` (e.g. `Crosshatch`, `Diagonal crosshatch`, `Solid fill`, `Concrete`, `Brick`, `Earth`...).
- Layered types: `create_compound_type(kind=wall|floor|roof|ceiling, name, layers=[outside→inside])`. Each layer `{material, thickness_mm, function}` where function ∈ structure, substrate, insulation, finish1 (exterior finish), finish2 (interior finish), membrane. Always include exactly one `structure` core (e.g. concrete or block).
  - Example exterior wall: finish1 *Brick* 100 → insulation *XPS* 50 → structure *Concrete C30* 200 → finish2 *Plaster* 15  (total 365).
  - Example slab: finish2 *Tile* 20 → substrate *Screed* 50 → structure *Concrete* 200.
- Change an existing type's layers: `set_type_layers`; inspect with `get_type_layers`.
- Paint materials on faces: `set_element_material(paint_faces=true)`. DirectShape/mass: pass `material` to `create_mass_extrusion`.
- Structural/thermal assets are not created by the API here; use `execute_csharp` if physical assets are required.
- Never edit a material that is shared by many types unless asked — duplicate (`duplicate_material`) first.
""");

        Add("views", "Views, sheets, schedules, annotation", "Plans, sections, elevations, 3D, templates, sheets, schedules, tags, dimensions.", """
# Views, sheets, schedules, annotation
- **Phase-7 helpers**: view templates `create_view_template`/`apply_view_template`/`list_view_templates`; filters `create_view_filter` + `apply_view_filter`; sheets `create_sheet_set` (auto layout), `arrange_viewports`, `create_sheet_index`, `add_revision`; dimensions `dimension_grids`, `dimension_openings`; `check_tag_overlaps(fix=true)`; `export_schedule` (csv/xlsx with live formulas, calculated columns, totals) ; Persian: `create_text_note_fa`, `jalali_date`, `to_persian_digits`, `set_sheet_dates_jalali`; code checks `check_building_rules` (Iranian residential/public defaults, overridable); CAD/PDF `export_dwg_standard` (AIA/ISO13567/...), `export_sheets_pdf`.
- **Plans**: `create_floor_plan(level, kind=floor|ceiling|structural|area)`. Set scale/detail with `set_view_properties`. View range via `set_view_range`.
- **Sections/elevations**: `create_section(start, end, bottom_mm, top_mm, depth_mm)` looks to the left of start→end; `create_elevation(point, direction)`. Check the result with `get_view_image` and flip if needed.
- **3D**: `create_3d_view(eye, target, perspective)`; crop with `set_section_box`.
- **Templates**: `list_view_templates`, then `set_view_properties(view, view_template=...)`.
- **Graphics**: `override_graphics` (colour-coding by category/element), `set_category_visibility`, `hide_elements`, `isolate_elements`.
- **Sheets**: `create_sheet(number, name)` → `place_view_on_sheet(sheet, view, center)` (also for schedules). Numbering convention: `A-101` plans, `A-201` elevations, `A-301` sections, `A-501` details, `S-101` structural, `M-101` HVAC, `E-101` electrical, `P-101` plumbing.
- **Schedules**: `list_schedulable_fields(category)` then `create_schedule(category, name, fields, filters, sort, totals_fields)`. Common: door schedule (Mark, Type, Level, Width, Height, Fire Rating), window schedule, room finish schedule (Number, Name, Area, floor/wall/ceiling finish), wall quantity (Type, Length, Area, Volume). Read results with `get_schedule_data`. For quick numbers without a schedule use `get_quantities`.
- **Annotation**: `create_dimension` (grids, walls, columns, levels), `tag_elements` / `tag_rooms`, `create_text_note`, `create_detail_lines`, `create_filled_region`.
- **Export**: `export_pdf`, `export_dwg`, `export_ifc`.
""");

        Add("scripting", "execute_csharp: full Revit API access", "How to use the C# scripting escape hatch: patterns, units, transactions, pitfalls.", """
# execute_csharp
Your code is the *body* of `object Run(Document doc, UIApplication uiapp, UIDocument uidoc, JsonObject args, Action<object> Print)`. Return a value (it is serialised to JSON; `ElementId` → number, `XYZ` → mm array) and use `Print(x)` to log.

**Rules**
- Revit units are *feet*. Use `U.Mm(mm)` / `U.ToMm(ft)`, `U.Deg(deg)`.
- By default the whole script runs in **one transaction** (rolled back on error). Pass `transaction=false` if you open your own `Transaction`/`TransactionGroup`/`StairsEditScope`/`AppearanceAssetEditScope`.
- Use `FilteredElementCollector`, never loops over all elements by id.
- ElementId has `.Value` (long). `new ElementId(long)`.
- `Find.*` helpers are available: `Find.Level(doc,"Level 1")`, `Find.Type<WallType>(doc,"Generic - 200mm")`, `Find.Material(doc,"X")`, `Find.View(doc,"X")`, `Find.Category(doc,"Walls")`.

**Examples**
```csharp
// 1. Count walls by type
var g = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().GroupBy(w => w.WallType.Name);
return g.ToDictionary(x => x.Key, x => x.Count());
```
```csharp
// 2. Parametric facade: vertical fins every 600 mm along a wall (generic model DirectShapes)
var wall = (Wall)doc.GetElement(new ElementId(args["wall"].GetValue<long>()));
var c = ((LocationCurve)wall.Location).Curve;
int n = (int)(U.ToMm(c.Length) / 600);
for (int i = 0; i <= n; i++) {
  var p = c.Evaluate(i * 600 / U.ToMm(c.Length), true);
  var loop = CurveLoop.CreateViaThicken(CurveLoop.Create(new List<Curve>{ Line.CreateBound(p - XYZ.BasisX*U.Mm(25), p + XYZ.BasisX*U.Mm(25)) }), U.Mm(100), XYZ.BasisZ);
  var solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop>{loop}, XYZ.BasisZ, U.Mm(3000));
  var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
  ds.SetShape(new List<GeometryObject>{ solid });
}
return n + 1;
```
**Dynamo-style parametric work** (grids of panels, spirals, lofts, attractor patterns) is best done here: compute points with C# maths, then create geometry with DirectShapes (`create_mesh_shape` for meshes) or place family instances in a loop.

**Pitfalls**: Revit API calls only work inside the script (already on the Revit thread); don't use `Task`/threads; avoid UI (TaskDialog blocks Revit); keep scripts under ~60 s. A running script cannot be cancelled (the Revit API is single-threaded): if the HTTP call times out, Revit may stay busy until the code ends — never write unbounded loops; test on a small element set first.

**Dynamo graphs**: `run_dynamo_graph(path)` / `run_dynamo_python(code)` run asynchronously; poll `get_dynamo_status` until `state` = `finished` (`node_issues` lists nodes in warning/error; the very first run after Revit starts takes ~20 s because Dynamo initialises). Of the shipped samples (`AddInsDynamoForRevitSamplesen-US`) only the *Revit Geometry Creation* graphs (Curves → model lines, Points, Solids/Surfaces → forms) build elements in an empty project; the others (Floors and Framing, Structural Framing, Place Families..., Color, Import Solid, Adaptive Component, GD/*) select existing rooms/floors/families from the sample models, which are not installed, so they evaluate with warnings and create nothing.
""");

        Add("warnings", "Warnings, errors and dialogs", "How the plugin silences/resolves warnings and dialogs; how to clean a model.", """
# Warnings & dialogs
- `set_warning_policy(policy)`: `off` (Revit default) · `ignore_warnings` (warnings deleted as they appear — the equivalent of clicking OK/Ignore) · `ignore_and_resolve_errors` (also applies Revit's default resolution to errors such as "Highlighted walls overlap" → *delete/unjoin*, or removes constraints). The ribbon button **Warnings:** in the *Arian* tab cycles these modes.
- `auto_dismiss_dialogs=true` answers task dialogs/message boxes automatically (OK → Close → Cancel/No; "save changes?" → No).
- `get_dialog_log` shows every dialog that appeared. `dismiss_dialogs` clicks open dialogs through UI Automation even while Revit is blocked.
- Errors are auto-resolved for the agent's own tools too: the result then carries `auto_resolved_errors` (e.g. "Constraints are not satisfied" after moving an element that is dimension-locked to a pinned one). With policy `off`/`ignore_warnings` such an error rolls the operation back ("Revit rolled back the operation: ...").
- Model hygiene: `get_warnings(details=true)`, `resolve_warnings(strategy='delete_duplicates', dry_run=false)` for "Identical instances in the same place" (pass `description_contains='overlap'` to also clean "walls overlap"), `purge_unused(dry_run=false)`, `audit_model_health`.
- Typical warnings and fixes: *Walls overlap* → `join_geometry` or move; *Room not enclosed* → add `create_room_separation` or close the walls; *Duplicate Mark* → `set_parameters_bulk` with unique Marks; *Stair run too short* → `create_straight_stairs`; *Element is slightly off axis* → rotate/re-draw orthogonal.
""");

        Add("troubleshooting", "Troubleshooting", "Common failures and their fixes.", """
# Troubleshooting
| Symptom | Fix |
|---|---|
| Tool times out | A modal dialog is open: `dismiss_dialogs`, `get_dialog_log`. Or Revit is busy regenerating. |
| "No document is open" | `create_project` or `open_document`. Family tools need `document` = the family title. |
| Type not found | `list_element_types(category)` / `list_families`; load families with `load_family`. In a default Architectural template many categories (MEP fittings, structural families) are missing — use a better template (`multi` / `structural` / `mechanical` / `systems`) or create content with `family_*` tools. |
| Door/window placement fails | Pass `host_id` (wall) or place the point ON the wall; check `level`. |
| Floor/roof error | Boundary must be a closed non-self-intersecting polygon with edges > 2 mm. |
| Duct/pipe "failed to insert elbow" | The duct/pipe type has no routing preferences with fittings (common in the Architectural template). Create the project from the `mechanical`/`systems`/`multi` template, or load fittings and set routing preferences via `execute_csharp`. |
| Rooms have zero area | Not enclosed (gaps at wall joins/doors don't matter, but missing walls do) or room-bounding off. Use `create_room_separation`. |
| Image is empty | The view shows nothing (wrong level/phase/crop). Use a 3D view or check `view range`. |
| Family won't flex | Run `family_flex_test`; check reference planes are *strong* and dimensions are labelled; avoid over-constrained dims. |
| Nothing happens in Revit UI | The server is stopped: Arian tab → Start Server. |
""");
    }
}
