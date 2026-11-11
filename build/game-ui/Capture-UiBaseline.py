#!/usr/bin/env python3
"""Capture reviewable source baselines; runtime screenshots/packet traces remain separate evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def records(text, pattern, source, kind):
    return [{'kind': kind, 'source': source, 'line': text.count('\n', 0, m.start()) + 1,
             'text': m.group(0).strip()} for m in re.finditer(pattern, text, re.MULTILINE)]


def capture(repo, output):
    repo = repo.resolve(strict=True)
    if output.exists():
        raise ValueError('Baseline output must be new; do not overwrite an earlier snapshot.')
    historical_path = 'docs/design/assets/official-ingame-ui/code-baseline.json'
    historical = json.loads((repo / historical_path).read_text(encoding='utf-8-sig'))
    def read(name):
        return (repo / name).read_text(encoding='utf-8-sig')
    def git(*args):
        return subprocess.check_output(['git', '-C', str(repo), *args], text=True, encoding='utf-8').strip()
    changes = []
    for name, previous in historical['files'].items():
        current = sha(repo / name)
        changes.append({'path': name, 'historicalSha256': previous, 'currentSha256': current,
                        'matchesHistorical': previous == current,
                        'explanation': 'unchanged' if previous == current else 'UI foundation implementation; review current worktree diff, not a rewrite of the historical baseline'})
    character_path = 'Client/Scenes/Views/CharacterDialog.cs'
    character = read(character_path)
    stats = []
    for m in re.finditer(r'(\w*Stats)\[Stat\.(\w+)\]\s*=\s*new DXLabel', character):
        group, stat = m.groups()
        destination = '角色'
        if group.startswith('Hermit'):
            destination = '修炼'
        elif stat == 'PhysicalResistance' or (group == 'DisplayStats' and stat not in {'MaxAC','MaxMR','MaxDC','MaxMC','MaxSC','Accuracy','Agility','Luck','Comfort'}):
            destination = '状态'
        stats.append({'binding': group, 'stat': stat, 'source': character_path,
                      'line': character.count('\n', 0, m.start()) + 1,
                      'targetPage': destination,
                      'validation': 'UI-18 binding/formatter comparison; preserve current Stats/HermitStats source and sign/color behavior'})
    enum_path = 'Library/Enum.cs'
    equipment = re.search(r'public enum EquipmentSlot\s*\{(.*?)\n    \}', read(enum_path), re.S).group(1)
    slots = [{'id': int(m.group(3)), 'name': m.group(2), 'label': m.group(1),
              'targetPage': '角色' if int(m.group(3)) in {0,1,2,4,5,6,7,8,9} else '扩展装备',
              'validation': 'UI-17/18 assert Grid[id], drag/drop, equip/unequip and packets retain slot ID'}
             for m in re.finditer(r'\[Description\("([^"]+)"\)\]\s*(\w+)\s*=\s*(\d+)', equipment)]
    if len(slots) != 16:
        raise ValueError('Equipment slot inventory is incomplete')
    npc_path = 'Client/Scenes/Views/NPCDialog.cs'
    npc_text = read(npc_path)
    declarations = list(re.finditer(r'^    public (?:sealed )?class (\w+)\s*:\s*(\w+)', npc_text, re.M))
    task_doc = json.loads(read('docs/tasks/official-ingame-ui/tasks.json'))
    owners = {name: task['id'] for task in task_doc['tasks'] for name in task.get('npcWindows', [])}
    windows = []
    for i, declaration in enumerate(declarations):
        name, base = declaration.groups()
        if base != 'DXWindow':
            continue
        end = declarations[i + 1].start() if i + 1 < len(declarations) else len(npc_text)
        body = npc_text[declaration.start():end]
        packet_records = records(body, r'new C\.\w+', npc_path, 'packet-constructor')
        for item in packet_records:
            item['line'] += npc_text.count('\n', 0, declaration.start())
        windows.append({'class': name, 'source': npc_path,
                        'line': npc_text.count('\n', 0, declaration.start()) + 1,
                        'ownerTask': owners.get(name), 'target': 'OfficialV1 对应 NPC 服务卡片；业务 handler 原样复用',
                        'packetConstructors': packet_records,
                        'validation': 'cancel/confirm/quantity validation and payload comparison with pre-change handler'})
    if len(windows) != 17 or any(not item['ownerTask'] for item in windows):
        raise ValueError('NPC window inventory/ownership is incomplete')
    binding_path = 'Client/Envir/CEnvir.cs'
    route_path = 'Client/Scenes/GameScene.cs'
    defaults = read(binding_path)
    routes = read(route_path)
    keybindings = []
    for m in re.finditer(r'case KeyBindAction\.(\w+):([\s\S]*?)(?=\n\s*case KeyBindAction\.|\n\s*default:)', defaults):
        name = m.group(1)
        related = []
        for route in re.finditer(r'case KeyBindAction\.' + re.escape(name) + r':([\s\S]*?)(?=\n\s*case KeyBindAction\.|\n\s*default:)', routes):
            related.append({'source': route_path, 'line': routes.count('\n', 0, route.start()) + 1,
                            'handler': route.group(0).strip()})
        keybindings.append({'action': name, 'source': binding_path, 'line': defaults.count('\n', 0, m.start()) + 1,
                            'defaultDefinition': m.group(0).strip(), 'routes': related,
                            'target': 'Existing action routing and configured KeyBinds retained; OfficialV1 only changes UI placement',
                            'validation': 'UI-13..16/27 execute configured action, not a hardcoded replacement key'})
    source_paths = [character_path, 'Client/Scenes/Views/InventoryDialog.cs', 'Client/Scenes/Views/BeltDialog.cs',
                    'Client/Scenes/Views/MainPanel.cs', 'Client/Scenes/Views/StorageDialog.cs', npc_path,
                    'Client/Controls/DXItemCell.cs', 'Client/Scenes/Views/ChatDialog.cs',
                    'Client/Scenes/Views/ChatTextBox.cs', 'Client/Scenes/Views/MiniMapDialog.cs']
    # Some UI components share a source file; do not fabricate a path if it is absent.
    source_paths = [name for name in source_paths if (repo / name).is_file()]
    actions = []
    labels = []
    prompts = []
    for name in source_paths:
        text = read(name)
        actions += records(text, r'new C\.\w+', name, 'packet-constructor')
        labels += records(text, r'^\s*public DX(?:Label|ImageControl|CheckBox)[^;\n]*;', name, 'public-ui-binding')
        prompts += records(text, r'new DX(?:MessageBox|ItemAmountWindow|InputWindow)\b', name, 'confirmation-or-input-dialog')
    main_path = 'Client/Scenes/Views/MainPanel.cs'
    main = read(main_path)
    buttons = records(main, r'\w+Button\.MouseClick\s*\+=\s*[^;]+;', main_path, 'hud-entry-handler-excerpt')
    manifest = {
        'schemaVersion': 1, 'capturedAtLocalDate': '2026-11-07', 'kind': 'official-ui-source-baseline',
        'head': git('rev-parse', 'HEAD'), 'worktreeStatus': git('status', '--short'),
        'captureTiming': 'Captured after foundation implementation; original task-generation HEAD and historical source hashes remain unchanged. This is not a pristine before-code runtime snapshot.',
        'historicalBaseline': {'path': historical_path, 'head': historical['head'], 'sha256': sha(repo / historical_path)},
        'sourceComparisons': changes, 'equipment': slots, 'characterStatBindings': stats,
        'capacities': {'inventory':49, 'inventoryGrid':[7,7], 'beltLinks':10, 'equipment':16,
                       'cellSize':[36,36], 'cellStride':35, 'inventoryGridPixels':[246,246]},
        'capacityEvidence': records(read('Library/Globals.cs'), r'(?:MaxBeltCount|InventorySize|EquipmentSize)\s*=\s*\d+', 'Library/Globals.cs', 'capacity-constant'),
        'npcWindows': windows, 'keybindings': keybindings, 'publicUiBindings': labels, 'hudEntries': buttons,
        'packetConstructors': actions, 'confirmationDialogs': prompts,
        'behaviorContracts': [
            {'surface':'角色', 'target':'角色/扩展装备/状态/修炼', 'invariants':'16 fixed slot IDs; all public Grid/label/stat dictionaries remain bindable; keep ShowHelmet/Marriage/Hermit handlers', 'verification':'UI-17/18 source mapping plus runtime operations'},
            {'surface':'包裹/快捷栏', 'target':'OfficialV1 inventory and belt', 'invariants':'49 cells, 10 link identities; ItemMove/Use/Split/Lock/BeltLinkChanged and current item tooltip paths unchanged', 'verification':'UI-14 bindings, hover, drag/drop, split/use/sell/lock and packet comparison'},
            {'surface':'HUD/聊天/地图', 'target':'OfficialV1 HUD/chat/minimap frame', 'invariants':'All existing HUD labels/buttons and configured key routes remain available; retain IME/focus and map-coordinate conversion', 'verification':'UI-13/15/16/27 six resolutions and real input'},
            {'surface':'NPC/仓库/弹框', 'target':'17 NPC cards and associated StorageDialog/modal controls', 'invariants':'Same service preconditions, quantity/confirmation/cancel semantics, payload construction, storage capacities and server close behavior', 'verification':'UI-19..25 Bindings/Npc suites and runtime packet traces'}],
        'runtimeEvidence': {'legacyScreenshots':'not_collected', 'legacyPacketTraces':'not_collected',
                            'officialLoggedInScreenshots':'not_collected',
                            'reason':'No authenticated reference session or reproducible logged-in test fixture has been established.',
                            'effect':'Source baseline is usable for infrastructure; dependent visual/behavior acceptance remains open.'}
    }
    output.mkdir(parents=True)
    (output / 'baseline.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = capture(args.repo, args.output)
    print(json.dumps({'slots':len(result['equipment']), 'statBindings':len(result['characterStatBindings']),
                      'npcWindows':len(result['npcWindows']), 'keybindings':len(result['keybindings'])}))
