"""Cross-checks the tool names mentioned in the knowledge base against the live tool list.
Usage: node scripts/call.mjs tools/list > tools.txt ; python scripts/check_knowledge.py tools.txt
"""
import re, glob, sys, os

tools = set()
for l in open(sys.argv[1], encoding='utf-8'):
    parts = l.split()
    if parts and re.match(r'^[a-z_0-9]+$', parts[0]):
        tools.add(parts[0])
print(len(tools), 'tools registered')

unknown = {}
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'Arian.RevitMcp', 'Knowledge')
for f in glob.glob(os.path.join(root, '*.cs')):
    s = open(f, encoding='utf-8').read()
    for m in re.finditer(r'`([a-z][a-z0-9]*(?:_[a-z0-9]+)+)(?:\(|`)', s):
        n = m.group(1)
        if n not in tools:
            unknown.setdefault(n, set()).add(os.path.basename(f))
VERBS = ('create_', 'get_', 'set_', 'list_', 'family_', 'place_', 'find_', 'run_', 'export_', 'delete_', 'define_', 'tag_', 'copy_', 'move_', 'rotate_',
         'mirror_', 'number_', 'connect_', 'route_', 'resolve_', 'purge_', 'audit_', 'dismiss_', 'link_', 'load_', 'edit_', 'open_', 'search_', 'select_', 'snapshot_', 'tap_', 'override_', 'join_', 'attach_', 'duplicate_')
NOT_TOOLS = {'family_recipes', 'load_case'}  # a guide topic and an argument name
bad = [n for n in sorted(unknown) if n.startswith(VERBS) and n not in NOT_TOOLS]
for n in bad:
    print('?', n, sorted(unknown[n]))
print(len(bad), 'unknown tool-like names')
sys.exit(1 if bad else 0)
