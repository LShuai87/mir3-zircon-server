#!/usr/bin/env python3
"""Join source-derived character routing to proposed geometry; build-time only."""
import argparse
from copy import deepcopy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
LAYOUT = 'build/game-ui/layout-official-v1.json'
OUTPUT = 'build/game-ui/character-bindings.json'


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


routing = module('character_routing', 'Prepare-CharacterBindings.py')
geometry = module('character_geometry', 'Validate-OfficialUiLayout.py')
require = geometry.require


def hash_file(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def join(layout, bindings):
    checks = geometry.validate(layout, bindings)
    nodes = {n['id']: n for n in layout['nodes']}
    used = set()

    def attach(row, node_id, interactive=False):
        require(node_id in nodes and node_id not in used, 'Missing or repeated binding node: ' + node_id)
        n = nodes[node_id]
        require(n['owner'] == 'character' and n['page'] == row['targetTab'], 'Wrong character page: ' + node_id)
        if interactive:
            require(n['kind'] == 'control' and 'hitRect' in n, 'Missing action hit rectangle: ' + node_id)
        used.add(node_id)
        result = deepcopy(row)
        result['layoutNode'] = node_id
        result['visualRect'] = n['rect'][:]
        for key in ('hitRect', 'iconRect', 'valueRect', 'captionRect'):
            if key in n:
                result[key] = n[key][:]
        return result

    equipment = [attach(r, 'character.' + r['id'], True) for r in bindings['equipment']]
    stats = [attach(r, 'character.stat.' + r['id']) for r in bindings['statBindings']]
    controls = [attach(r, 'character.control.' + r['control'], r['control'] == 'ShowHelmetBox') for r in bindings['existingControls']]
    additional = [attach(r, 'character.immediate.' + r['id']) for r in bindings['additionalBindings']]
    actions = []
    for row in bindings['actions']:
        node_id = 'character.control.ShowHelmetBox' if row['id'] == 'character.showHelmet' else 'character.action.' + row['id']
        n = nodes[node_id]
        require(n['kind'] == 'control' and n['page'] == row['targetTab'], 'Action page/hit target mismatch')
        require(row['reuseExistingHandler'] is True, 'Replacement packet handler forbidden')
        expected_action = 'existing-helmet-toggle' if row['id'] == 'character.showHelmet' else row['id']
        require(n['action'] == expected_action, 'Original action dispatch drift: ' + node_id)
        # Helmet is intentionally the same original checkbox referenced above.
        result = deepcopy(row)
        result.update(layoutNode=node_id, visualRect=n['rect'][:], hitRect=n['hitRect'][:])
        actions.append(result)
    all_geometry = [deepcopy(n) for n in layout['nodes'] if n['owner'] == 'character']
    require(len({r['layoutNode'] for r in equipment + stats + controls + additional}) == 85, 'Character field routing is incomplete')
    return dict(schemaVersion=1, kind='official-ui-character-binding-contract',
                status='pending-scene-integration', runtimeReady=False, productionBindingAllowed=False,
                releaseAccepted=False, layoutFrozen=False, referenceResolution=[1024, 768],
                coordinateSpace='character-window-relative; logical pixels; no DPI scaling',
                scope=deepcopy(layout['scope']), tabs=deepcopy(bindings['tabs']),
                sourceHashes=deepcopy(bindings['sourceHashes']),
                equipment=equipment, statBindings=stats, existingControls=controls,
                additionalBindings=additional, actions=actions, geometry=all_geometry,
                preview=deepcopy(nodes['character.preview']),
                placements=[dict(resolution=[r['width'], r['height']], character=r['placements']['character'][:],
                                 inventory=r['placements']['inventory'][:], safeRect=r['safeRect'][:]) for r in layout['resolutions']],
                refreshConstraints=deepcopy(bindings['refreshConstraints']),
                retentionPolicy=dict(originalEquipmentCells=16, originalDictionaryLabels=57,
                                     originalNamedControls=8, hudMirrors=4,
                                     originalPacketActions=9, reuseHandlers=True, preserveReferences=True,
                                     confirmationPopups='Legacy', npcCards='Legacy'),
                geometryChecks=checks,
                remainingAcceptance=['Compiled candidate geometry must match this joined contract.',
                                     'Unified scene installation, live data/packet parity and combined-window acceptance.',
                                     'Full asset/layout/action contract freeze and real IME/device-reset/performance/rollback acceptance.'])


def expected():
    inputs = [ROOT / LAYOUT, Path(__file__).with_name('Prepare-CharacterBindings.py'),
              Path(__file__).with_name('Validate-OfficialUiLayout.py')]
    hashes = {str(p.relative_to(ROOT)).replace('\\', '/'): hash_file(p) for p in inputs}
    layout = geometry.read_json(inputs[0])
    bindings = routing.expected_contract()
    result = join(layout, bindings)
    result['inputs'] = hashes
    require(all(hash_file(p) == hashes[str(p.relative_to(ROOT)).replace('\\', '/')] for p in inputs), 'Input changed during binding assembly')
    require(all(hash_file(ROOT / row['path']) == row['sha256'] for row in result['sourceHashes']), 'Source changed during binding assembly')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / OUTPUT)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    contract = expected()
    data = (json.dumps(contract, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    output = args.output.resolve()
    protected = {ROOT / row['path'] for row in contract['sourceHashes']} | {ROOT / key for key in contract['inputs']} | {Path(__file__).resolve()}
    require(output not in {p.resolve() for p in protected}, 'Output aliases source input')
    if args.check:
        require(output.is_file() and output.read_bytes() == data, 'Joined character bindings are missing or stale')
    else:
        require(not output.exists(), 'Output already exists; use --check')
        output.parent.mkdir(parents=True, exist_ok=True)
        with output.open('xb') as stream:
            stream.write(data)
    print('Character bindings verified: 16 equipment cells, 57 stat labels, 8 named controls, 4 HUD mirrors, 9 original packet actions, 6 resolutions; activation disabled.')
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, OSError, KeyError, TypeError) as error:
        print('Character bindings rejected: ' + str(error), file=sys.stderr)
        sys.exit(1)
