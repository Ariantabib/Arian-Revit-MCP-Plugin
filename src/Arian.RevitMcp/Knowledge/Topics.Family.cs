namespace Arian.RevitMcp.Knowledge;

public static partial class Topics
{
    static void RegisterFamily()
    {
        Add("family", "Creating & editing families (from scratch)", "Step-by-step family authoring: templates, parameters, reference planes, extrusions, voids, types, connectors, testing; plus the one-shot family_build_from_spec.", """
# Families — author anything

## Two ways
**A. One call — `family_build_from_spec`** (fastest, fully parametric). You describe parameters, types and shapes with *expressions of parameters*; the tool creates reference planes, labelled dimensions, formulas, extrusions, types, saves and (optionally) loads into the project. Best for tables, shelves, casework, columns, beams (I/H/rect), footings, simple equipment, fixtures.
**B. Step by step** with `family_*` tools (maximum control):
1. `family_create(name, template, category?)` → returns `document` (title) — pass it as `document` to every call.
2. `family_setup_dimensions(width_mm, depth_mm, height_mm)` creates Width/Depth/Height parameters, planes Left/Right/Front/Back/Top and EQ-locked labelled dimensions.
3. Geometry: `family_add_box` / `family_add_cylinder` / `family_add_extrusion` / `family_add_blend` / `family_add_revolve` / `family_add_sweep` (use `solid=false` for **voids**). With `lock_to_planes=true` + `end_plane='Top'` the form follows the dimensions.
4. More parameters: `family_add_parameter(name, data_type, instance, group, value, formula)` (e.g. `Leg_Size` length, `Material` material, `Visible` yesno). Bind with `material_parameter=` on forms, `end_parameter=` for depths, or `family_associate_parameter`.
5. Extra planes/dimensions: `family_add_reference_plane(name, axis, offset_mm)` + `family_add_dimension(plane_a, plane_b, label=param)`.
6. Types: `family_add_type(name, values={param: value})` for each size.
7. MEP: `family_add_connector(kind, form_id, point, system_type, size)`.
8. 2D: `family_add_symbolic_lines` (plan symbols), `family_set_form_properties` (detail-level visibility, subcategory, yes/no visibility parameter).
9. **Test**: `family_flex_test()` and `family_get_geometry_summary`; fix, then `family_save` and `family_load_into_project(project, overwrite=true, close=true)`.
10. Place with `place_family_instance({type:'FamilyName: TypeName', ...})`.
Existing family: `edit_family(name)` → same `family_*` tools → `family_load_into_project`.

## Templates (aliases for `family_create`)
generic_model, generic_model_wall_based / _face_based / _ceiling_based / _floor_based / _line_based, door, window, furniture, casework, column, structural_column, structural_framing, structural_foundation, structural_truss, lighting_fixture(_ceiling/_wall), electrical_equipment, electrical_fixture, mechanical_equipment(_ceiling/_wall), plumbing_fixture, specialty_equipment, profile, detail_item, planting, entourage, parking, duct_elbow/tee/transition ... (`list_family_templates`). Pick the template whose *host/placement* matches (door & window are wall-hosted and include the wall opening cut; ceiling-based for lights/diffusers).

## Coordinate system inside a family
Origin = centre of the Left/Right and Front/Back planes at the reference level. +X right, +Y back, +Z up. Doors/windows: wall centre on Y=0, exterior toward −Y... check the template. Always draw geometry around the origin symmetric about the centre planes.

## Parametric rules that always work
- Dimensions drive reference planes; geometry is **locked to reference planes** (strong references); never dimension directly to sketch lines.
- Use EQ symmetric dimensions about the centre planes.
- Put numeric limits in formulas: `Leg_Inset = Width / 10`, `Top_Thickness = 40 mm`.
- Type parameters for sizes, instance parameters for things that vary per placement (offset, rotation flags, visibility).
- Don't over-constrain (a plane controlled by two dimensions).
- Provide a Material parameter per distinct part.

## family_build_from_spec — format
```json
{
  "name": "Arian Table", "template": "furniture", "category": "Furniture",
  "parameters": [
    {"name":"Width","type":"length","value":1600},
    {"name":"Depth","type":"length","value":800},
    {"name":"Height","type":"length","value":740},
    {"name":"Top_Thickness","type":"length","value":30},
    {"name":"Leg_Size","type":"length","value":60},
    {"name":"Top_Material","type":"material","group":"materials"}
  ],
  "types": [ {"name":"1600x800","values":{"Width":1600,"Depth":800}}, {"name":"1200x700","values":{"Width":1200,"Depth":700,"Height":720}} ],
  "shapes": [
    {"kind":"box","name":"Top","x":["-Width/2","Width/2"],"y":["-Depth/2","Depth/2"],"z":["Height-Top_Thickness","Height"],"material_parameter":"Top_Material"},
    {"kind":"box","name":"Leg1","x":["-Width/2+50","-Width/2+50+Leg_Size"],"y":["-Depth/2+50","-Depth/2+50+Leg_Size"],"z":["0","Height-Top_Thickness"]},
    {"kind":"cylinder","name":"Foot","center":["0","0"],"radius":"Leg_Size/2","z":["0","20"]},
    {"kind":"prism","plane":"yz","points":[["-50","0"],["50","0"],["50","100"],["-50","100"]],"range":["0","Width"],"name":"Rail"}
  ],
  "connectors": [],
  "load": true, "save": true, "close": true
}
```
- Expressions are written with parameter names, numbers (mm), `+ - * / ( )` and `min()/max()`. Constants are millimetres.
- `box`: `x`, `y`, `z` are `[from, to]`. `cylinder`: vertical, `center:[x,y]`, `radius`, `z:[from,to]`. `prism`: polygon `points:[[u,v],...]` on `plane` (`xy`: u=x,v=y, extruded along z `range`; `xz`: u=x,v=z along y; `yz`: u=y,v=z along x).
- Optional per shape: `solid:false` (void), `material`, `material_parameter`, `subcategory`, `detail_levels`.
- Parameter names with spaces are allowed; write them in brackets inside expressions: `"-[Foundation Thickness]"`.
- **Tokens for template geometry**: `"plane:Left"` / `"plane:Right"` = an existing reference plane of the template (e.g. a beam's Start/End planes), `"level:Upper Ref Level"` = a level of the family (columns: `"z":["level:Lower Ref. Level","level:Upper Ref Level"]` so the column follows its placed base/top levels).
- Spec flags: `clear_geometry:true` deletes the template's placeholder forms first (needed for structural framing/foundation templates that ship with a default extrusion); `replace_types` (default true) removes the template's default types when you list your own; `flex_test`, `save`, `load`, `close`, `project`.
- Template facts: *structural_column* has levels `Lower Ref. Level`/`Upper Ref Level`, type params `Width`/`Depth` (reuse these names) and the instance material parameter `Structural Material` (use `"material_parameter":"Structural Material"`); *structural_framing* has planes `Left`/`Right` (beam ends) and the beam profile is drawn in the `yz` plane; *structural_foundation* has `Length`/`Width`/`Foundation Thickness`; door/window templates already contain a working parametric door/window — usually just edit parameters/types (`family_add_type`, `family_set_parameter_value`) and add forms.
- `connectors`: `[{ "kind":"duct","shape":"shape name","point":[x,y,z],"system_type":"supply_air","shape_type":"rectangular","width":600,"height":400 }]`.
- Result lists the created parameters/planes/shapes, flex-test result and the loaded type names.

## Quality checklist (LOD 300 families)
Correct category + placement type; origin & insertion sensible; ≥ 1 type per standard size; every dimension parametric; material parameters; subcategories (Frame/Panel/Glass...) for graphics control; fine/medium/coarse visibility (simplified coarse); symbolic plan lines for doors/equipment; flex test passes; connectors for MEP; naming `Category-Description-Manufacturer`.
""");

        Add("family_recipes", "Family recipes (door, window, column, beam, footing...)", "Concrete steps for common content types using family_build_from_spec / family_* tools.", """
# Family recipes
**Library & QA (fast path):** `list_standard_families` / `create_standard_family(kind)` builds ready parametric families (table, chair, bed, sofa, wardrobe, kitchen_base_cabinet, wc, washbasin, bathtub, ceiling_light, air_diffuser, fan_coil, ahu, pump, electrical_panel, round_column, door_single/double, window_standard) with types, material parameters and MEP connectors. `family_qa_report` scores the open family; `family_export_type_catalog` writes the type catalog .txt; `family_import_types_csv` creates types from a CSV (Excel); `family_add_nested` nests another family. Keep expressions flex-safe: avoid `A/2 - constant` that can turn negative when parameters shrink (use proportions).
Always: `family_create` → geometry → `family_add_type` → `family_flex_test` → `family_save` → `family_load_into_project`.

## Concrete rectangular column (structural_column)
`family_build_from_spec` with template `structural_column`, parameters `b`, `h` (length) and shapes: `box x:[-b/2,b/2] y:[-h/2,h/2] z:[0, …]`. Column height is driven by the placed instance's base/top levels — in the structural column template the extrusion is already tied to the *Lower/Upper* reference levels: use `family_add_extrusion(plane='xy', ... end_plane=<upper ref plane>)` (see `family_get_info` planes). Types like `300x300`, `400x400`, `500x500`.

## Rectangular beam (structural_framing)
Template `structural_framing`. Profile in `yz` (b × h) centred on the beam axis, extruded along X between the template's Start/End planes (`family_get_info` shows them). Parameters `b`, `h`. Types `250x500` etc. `z Justification`/top at 0 by template convention.

## Isolated footing (structural_foundation)
Template `structural_foundation`: box `x:[-L/2,L/2] y:[-B/2,B/2] z:[-T,0]` + optional pedestal. Parameters `Length`, `Width`, `Thickness`.

## Door (door template)
Wall-hosted: `Width`, `Height` parameters with `family_setup_dimensions`-style planes on the wall opening; panel = box (Thickness 40) hung in the opening; frame = boxes; add a **void** box (opening cut) is built into the template. Add `symbolic lines` for the plan swing arc (`family_add_symbolic_lines`). Types `0900x2100`, `1000x2100`.

## Window
Same as door with `Sill_Height` instance parameter; glass panel box with a *Glass* material parameter (`transparency`), frame boxes (subcategory `Frame`), sash.

## Furniture
Use the `furniture` template: boxes/cylinders/blends; material parameters for each part; types per size.

## Equipment with connectors
`mechanical_equipment` / `electrical_equipment` / `plumbing_fixture`: box body + `family_add_connector` on faces (duct/pipe/electrical). Add a `Flow`/`Power` parameter and symbolic plan outline.

## Light fixture / diffuser (ceiling based)
`lighting_fixture_ceiling`: small box/cylinder body, a `Light Source`/connector as needed, instance parameter for mounting height.
""");
    }
}
