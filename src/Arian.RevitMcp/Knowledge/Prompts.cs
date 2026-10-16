namespace Arian.RevitMcp.Knowledge;

/// <summary>Ready-made workflows exposed as MCP prompts (slash commands in many clients).</summary>
public static class PromptCatalog
{
    static PromptArg A(string n, string d, bool req = false) => new() { Name = n, Description = d, Required = req };

    public static readonly List<PromptDef> All = new()
    {
        new PromptDef
        {
            Name = "start_architectural_project",
            Description = "Set up a new Revit architectural project and model a building from a brief (Phase 1 / schematic).",
            Args = new[] { A("brief", "Building type, plot size, storeys, program, style", true), A("storey_height_mm", "Floor-to-floor height"), A("units", "Metric/imperial preference") },
            Template = """
You are a senior architect-BIM modeller using the Arian Revit MCP server. Read `get_guidance` topics `workflow`, `architecture`, `lod` first.

PROJECT BRIEF: {brief}
Storey height: {storey_height_mm}

Do a Phase 1 (schematic, LOD 200) model:
1. `get_session_info`; create a project with `create_project` (template 'architectural' unless MEP/structure is needed -> 'multi'); `set_project_info`.
2. Create levels, a structural grid, walls (perimeter + partitions), floors, roof, doors/windows from the loaded families, rooms with names and areas, stairs, and a few 3D/plan/section views.
3. After each major step render `get_view_image` and check geometry against the brief (areas, proportions, circulation). Fix problems immediately.
4. Finish with a room area schedule (`create_schedule`), `get_warnings`, and a summary of what was modelled, assumptions made, and suggested next steps (Phase 2).
Reply in the user's language.
"""
        },
        new PromptDef
        {
            Name = "phase2_design_development",
            Description = "Take an existing schematic model to Phase 2 / design development (LOD 300): real assemblies, structure, schedules, sheets.",
            Args = new[] { A("focus", "Optional focus: architecture, structure, mep, documentation, all") },
            Template = """
You are a BIM lead. Upgrade the OPEN Revit model to Phase 2 / design development (LOD 300). Focus: {focus}.
Read `get_guidance` topics `lod`, `architecture`, `structure`, `mep`, `materials`, `views`.
1. Audit: `get_model_overview`, `audit_model_health`, `get_warnings`, `list_element_types` for walls/floors/roofs/doors/windows. Note generic types.
2. Replace generic types with real assemblies (`create_material`, `create_compound_type`, `change_element_type`), add ceilings, finishes (room parameters), railings, openings.
3. Structure: grid, columns, beams, slabs, foundations coordinated with architecture (`find_clashes`).
4. MEP (if requested): equipment, main routes, terminals, circuits.
5. Documentation: dimensions, tags, sections/elevations, sheets, door/window/room-finish/quantity schedules.
6. Clean: resolve warnings, `purge_unused` (dry-run first), `audit_model_health`.
Verify visually with `get_view_image` at each step. Summarise changes and open issues.
"""
        },
        new PromptDef
        {
            Name = "model_structure",
            Description = "Create a coordinated structural model (grid, columns, beams, slabs, foundations) for the open architectural model.",
            Args = new[] { A("system", "RC frame / steel frame / load-bearing masonry / flat slab..."), A("assumptions", "Spans, loads, materials") },
            Template = """
Model the structure for the open model. System: {system}. Assumptions: {assumptions}.
Read `get_guidance` topics `structure`, `family`. Inspect architecture with `get_model_overview`, `list_grids`, `query_elements`.
Steps: confirm/create grid aligned to walls -> create missing structural families (`family_build_from_spec`: concrete columns/beams/footings with parameters and types) -> columns at grid intersections -> beams along grids -> structural floors -> foundations -> bracing/cores if needed -> optional reinforcement -> `get_structural_summary` -> `find_clashes` vs architecture -> render views. State preliminary sizing assumptions explicitly.
"""
        },
        new PromptDef
        {
            Name = "mep_design",
            Description = "Model HVAC / plumbing / electrical systems for the open model.",
            Args = new[] { A("disciplines", "hvac, plumbing, electrical, fire"), A("requirements", "Loads, lux levels, outlets, fixtures") },
            Template = """
Model MEP systems: {disciplines}. Requirements: {requirements}.
Read `get_guidance` topics `mep`, `family`. Check the template has system types/fittings (else create/open a project from the 'multi' template and link the architecture). Create spaces, place or build equipment/terminals/fixtures, route mains and branches with fittings, create systems and circuits, check `find_open_connectors`, `find_clashes`, and render views. Report assumptions (airflows, pipe sizes, loads).
"""
        },
        new PromptDef
        {
            Name = "create_family",
            Description = "Create a new parametric Revit family from a description (any category).",
            Args = new[] { A("description", "What the family is, dimensions, sizes, materials, MEP connections", true), A("load", "Load into the open project (yes/no)") },
            Template = """
Create a parametric Revit family: {description}. Load into project: {load}.
Read `get_guidance` topics `family` and `family_recipes`. Choose the correct template/category/placement. Prefer `family_build_from_spec`; fall back to `family_*` tools. Include size types, material parameters, connectors for MEP, symbolic plan lines where relevant. Run `family_flex_test`, `family_save`, then load, place one instance, and show `get_view_image` in 3D to verify.
"""
        },
        new PromptDef
        {
            Name = "produce_drawing_set",
            Description = "Create plans, elevations, sections, schedules and sheets for the open model.",
            Args = new[] { A("sheet_prefix", "e.g. A for architecture"), A("scale", "e.g. 100") },
            Template = """
Produce a drawing set for the open model. Sheet prefix: {sheet_prefix}. Scale: {scale}.
Read `get_guidance` topic `views`. Create floor plans, 4 elevations, 2 sections (through stairs), a 3D view, schedules (door, window, room finish, wall quantities), dimension grids/walls, tag doors/windows/rooms, make sheets with a title block and place everything, then verify with `get_view_image`. Export a PDF if asked.
"""
        },
        new PromptDef
        {
            Name = "model_audit",
            Description = "Audit the open model and fix what is safely fixable.",
            Args = new[] { A("fix", "yes to apply safe fixes") },
            Template = """
Audit the open Revit model; apply safe fixes: {fix}.
Run `audit_model_health`, `get_warnings(details=true)`, `find_clashes` for obvious pairs, `find_open_connectors` for MEP, and `list_rooms` for unenclosed rooms. Fix duplicates (`resolve_warnings` dry-run first), missing names/numbers/marks, unplaced rooms. Report a prioritised list.
"""
        },
    };
}
