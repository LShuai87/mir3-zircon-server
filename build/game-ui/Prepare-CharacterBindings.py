#!/usr/bin/env python3
"""Prepare/check the unfrozen character-binding input for UI-06; no game IO."""
import argparse
import collections
import hashlib
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
BASE = 'docs/design/assets/official-ingame-ui/acceptance/baseline/implementation-start/baseline.json'
PLAN = 'docs/design/official-ingame-ui-implementation-plan.md'
CHAR = 'Client/Scenes/Views/CharacterDialog.cs'
ENUM = 'Library/Enum.cs'
SCENE = 'Client/Scenes/GameScene.cs'
USER = 'Client/Models/UserObject.cs'
CONNECTION = 'Client/Envir/CConnection.cs'
MATCHING = 'docs/design/assets/official-ingame-ui/acceptance/reference/panel-matching.json'
READOUTS = 'Client/Envir/GameUiCharacterReadouts.cs'
HUD = 'Client/Scenes/Views/MainPanel.cs'
DEFAULT = ROOT / 'docs/design/assets/official-ingame-ui/acceptance/preparatory/character-composition/character-bindings.draft.json'
MAIN_SLOTS = set(range(16))
MAIN_STATS = {'MaxAC', 'MaxMR', 'MaxDC', 'MaxMC', 'MaxSC', 'AttackSpeed', 'Accuracy', 'Agility', 'Luck', 'Comfort'}
GROUPS = {
    'DisplayStats': ('User.Stats', 'existing-get-format'),
    'AttackStats': ('User.Stats', 'existing-positive-attack'),
    'AdvantageStats': ('User.Stats', 'existing-positive-resistance'),
    'DisadvantageStats': ('User.Stats', 'existing-negative-resistance'),
    'HermitDisplayStats': ('User.HermitStats', 'existing-get-format'),
    'HermitAttackStats': ('User.HermitStats', 'existing-positive-hermit-attack'),
}

def require(condition, message):
    if not condition:
        raise ValueError(message)

def reject_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON key: ' + key)
        result[key] = value
    return result

def load(path):
    return json.loads(pathlib.Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=reject_duplicate_keys)

def sha(path):
    return hashlib.sha256((ROOT / path).read_bytes()).hexdigest()

def text(path):
    return (ROOT / path).read_text(encoding='utf-8-sig')

def evidence(path, anchor):
    content = text(path)
    require(content.count(anchor) == 1, 'Expected unique source anchor: ' + anchor)
    return {'path': path, 'line': content[:content.index(anchor)].count('\n') + 1, 'anchor': anchor}

def expected_contract():
    baseline = load(ROOT / BASE)
    character = text(CHAR)
    plan_hash = sha(PLAN)
    require(plan_hash == 'f8c0a002b3821fabb54993792c354c4eff35eec51f9cfdc418535017c13069e9', 'Frozen plan changed; review before preparing bindings')
    enum_body = text(ENUM).split('public enum EquipmentSlot', 1)[1].split('}', 1)[0]
    enum_rows = re.findall(r'\[Description\("([^"]+)"\)\]\s*(\w+)\s*=\s*(\d+)', enum_body)
    require(len(enum_rows) == 16, 'Expected 16 explicit equipment IDs')
    enum_ids = {name: int(slot) for label, name, slot in enum_rows}
    require(set(enum_ids.values()) == set(range(16)), 'Equipment IDs changed')
    require(enum_ids == {row['name']: row['id'] for row in baseline['equipment']}, 'Equipment enum differs from historical baseline')
    cells = re.findall(r'Grid\[\(int\)EquipmentSlot\.(\w+)\]\s*=\s*cell\s*=\s*new DXItemCell\s*\{(.*?)\};', character, re.S)
    require(len(cells) == 16 and len({name for name, body in cells}) == 16, 'Equipment cells missing or duplicated')
    require({name for name, body in cells} == set(enum_ids), 'Equipment constructor coverage changed')
    for name, body in cells:
        require(re.search(r'Slot\s*=\s*\(int\)EquipmentSlot\.' + re.escape(name) + r'\s*,', body), 'Grid/Slot identity mismatch: ' + name)
        require('ItemGrid = GameScene.Game.Equipment,' in body and 'GridType = GridType.Equipment,' in body, 'Equipment backing grid changed: ' + name)
    equipment = []
    positions = collections.Counter()
    for label, name, slot in enum_rows:
        slot = int(slot)
        tab = 'character'
        equipment.append({'id': 'equipment.' + name, 'slotId': slot, 'enumName': name, 'label': label,
                          'control': 'Grid[' + str(slot) + ']', 'targetTab': tab, 'orderInTab': positions[tab],
                          'visualRect': None, 'hitRect': None, 'iconRect': None})
        positions[tab] += 1
    stat_rows = re.findall(r'\b(' + '|'.join(GROUPS) + r')\[Stat\.(\w+)\]\s*=\s*new DXLabel', character)
    historical = [(row['binding'], row['stat']) for row in baseline['characterStatBindings']]
    require(len(stat_rows) == 57 and collections.Counter(stat_rows) == collections.Counter(historical), 'Character attribute coverage differs from 57 historical bindings')
    require(len(set(stat_rows)) == len(stat_rows), 'Duplicate character stat control')
    stats = []
    for group, stat in stat_rows:
        data, policy = GROUPS[group]
        tab = 'hermit' if group.startswith('Hermit') else ('character' if ((group == 'DisplayStats' and stat in MAIN_STATS) or (group != 'DisplayStats' and stat != 'PhysicalResistance')) else 'status')
        stats.append({'id': group + '.' + stat, 'control': group + '[Stat.' + stat + ']', 'stat': stat,
                      'targetTab': tab, 'dataSource': data, 'formatPolicy': policy,
                      'refreshAt': 'CharacterDialog.UpdateStats',
                      'keepTagIcon': group not in ('DisplayStats', 'HermitDisplayStats'),
                      'sourceEvidence': evidence(CHAR, '            ' + group + '[Stat.' + stat + '] = new DXLabel')})
    control_definitions = [
        ('CharacterNameLabel', 'character', 'User.Name', USER, 'GameScene.Game.CharacterBox.CharacterNameLabel.Text = Name;'),
        ('GuildNameLabel', 'character', 'User.Title', USER, 'GameScene.Game.CharacterBox.GuildNameLabel.Text = Title;'),
        ('GuildRankLabel', 'character', 'User.GuildRank', USER, 'GameScene.Game.CharacterBox.GuildRankLabel.Text = GuildRank;'),
        ('WearWeightLabel', 'character', 'User.WearWeight / User.Stats[Stat.WearWeight]', SCENE, 'CharacterBox.WearWeightLabel.Text ='),
        ('HandWeightLabel', 'character', 'User.HandWeight / User.Stats[Stat.HandWeight]', SCENE, 'CharacterBox.HandWeightLabel.Text ='),
        ('RemainingLabel', 'hermit', 'User.HermitPoints', CHAR, 'RemainingLabel.Text = MapObject.User.HermitPoints.ToString();'),
        ('MarriageIcon', 'character', 'Partner.Name', SCENE, 'CharacterBox.MarriageIcon.Visible = !string.IsNullOrEmpty(Partner?.Name);'),
        ('ShowHelmetBox', 'character', '!S.HelmetToggle.HideHelmet', CONNECTION, 'GameScene.Game.CharacterBox.ShowHelmetBox.Checked = !p.HideHelmet;'),
    ]
    controls = []
    for name, tab, data, source, anchor in control_definitions:
        require(len(re.findall(r'\b' + name + r'\s*=\s*new DX(?:Label|ImageControl|CheckBox)\b', character)) == 1, 'Named control not unique: ' + name)
        controls.append({'id': 'existing.' + name, 'control': name, 'targetTab': tab, 'dataSource': data,
                         'preserveReference': True, 'sourceEvidence': evidence(source, anchor)})
    hermit_stats = re.findall(r'new C\.Hermit\s*\{\s*Stat\s*=\s*Stat\.(\w+)\s*\}', character)
    expected_hermit = ['MaxAC', 'MaxMR', 'Health', 'Mana', 'MaxDC', 'MaxMC', 'MaxSC', 'WeaponElement']
    require(collections.Counter(hermit_stats) == collections.Counter({stat: 2 for stat in expected_hermit}), 'Hermit action paths changed; review confirmation and direct paths')
    require(character.count('if (MapObject.User.HermitPoints == 0) return;') == 8, 'Hermit zero-point guards changed')
    require(character.count('if (check.Checked)') == 8, 'Hermit confirmation branches changed')
    actions = [{'id': 'hermit.' + stat, 'targetTab': 'hermit', 'packet': 'C.Hermit', 'stat': stat,
                'reuseExistingHandler': True, 'preserveZeroPointGuard': True, 'preserveConfirmationAndCancel': True}
               for stat in expected_hermit]
    actions.append({'id': 'character.showHelmet', 'targetTab': 'character', 'packet': 'C.HelmetToggle',
                    'reuseExistingHandler': True, 'sourceEvidence': evidence(CHAR, 'CEnvir.Enqueue(new C.HelmetToggle{ HideHelmet = ShowHelmetBox.Checked});')})
    additional = []
    for name, data, hook, anchor in [
        ('level', 'User.Level', 'LevelChanged', 'MainPanel.LevelLabel.Text = User.Level.ToString();'),
        ('experience', 'User.Experience / User.MaxExperience', 'ExperienceChanged', 'MainPanel.ExperienceLabel.Text ='),
        ('health', 'User.CurrentHP / User.Stats[Stat.Health]', 'HealthChanged', 'MainPanel.HealthLabel.Text ='),
        ('mana', 'User.CurrentMP / User.Stats[Stat.Mana]', 'ManaChanged', 'MainPanel.ManaLabel.Text ='),
    ]:
        source_control = name.capitalize() + 'Label'
        additional.append({'id': 'new.' + name, 'targetTab': 'character', 'dataSource': data, 'refreshAt': 'GameScene.' + hook,
                           'status': 'implemented_opt_in', 'runtimeEnabled': False,
                           'sourceControl': 'MainPanel.' + source_control,
                           'reuseHudFormatting': True, 'sourceEvidence': evidence(SCENE, anchor),
                           'implementationEvidence': {
                               'entrypoint': evidence(CHAR, 'public void UseGameUiReadouts(MainPanel source, GameUiCharacterReadoutLayout layout)'),
                               'target': evidence(READOUTS, 'public DXLabel ' + source_control + ' =>'),
                               'copy': evidence(READOUTS, 'labels[i].Text = sourceLabels[i].Text;'),
                               'subscribe': evidence(READOUTS, 'control.TextChanged += Refresh;'),
                               'unsubscribe': evidence(READOUTS, 'control.TextChanged -= Refresh;'),
                               'test': 'tests/game-ui/OfficialUiCharacterReadoutTests.cs',
                               'verification': 'docs/design/assets/official-ingame-ui/acceptance/preparatory/character-readouts/verification.json',
                           }})
    character_matches = [row for row in load(ROOT / MATCHING)['matches'] if row['role'] == 'character']
    require(len(character_matches) == 3 and all(any(c['library'] == 'UI' and c['index'] == 2765 and c['width'] == 412 and c['height'] == 562 for c in row['candidates']) for row in character_matches), 'Runtime character reference changed; review source selection')
    source_paths = [BASE, PLAN, CHAR, ENUM, SCENE, USER, CONNECTION, MATCHING, READOUTS, HUD,
                    'Client/Scenes/Views/CharacterDialog.GameUi.cs', 'Client/Envir/GameUiCharacterEquipmentLayout.cs',
                    'Client/Controls/DXTabControl.cs', 'Client/Envir/GameUiCharacterAttributeLayout.cs',
                    'Client/Scenes/Views/CharacterDialog.GameUiAttributes.cs', 'Client/Envir/GameUiCharacterControlLayout.cs',
                    'Client/Scenes/Views/CharacterDialog.GameUiControls.cs', 'Client/Controls/DXCheckBox.cs',
                    'Client/Envir/GameUiCharacterCompositionLayout.cs', 'Client/Scenes/Views/CharacterDialog.GameUiComposition.cs']
    return {'schemaVersion': 1, 'kind': 'draft-character-binding-contract', 'status': 'draft',
            'productionBindingAllowed': False, 'releaseAccepted': False, 'layoutFrozen': False,
            'tabs': [{'id': 'character', 'label': '角色'},
                     {'id': 'status', 'label': '状态'}, {'id': 'hermit', 'label': '修炼'}],
            'sourceHashes': [{'path': path, 'sha256': sha(path)} for path in source_paths],
            'historicalTargetText': {'authoritativeForRouting': False,
                'replacementCharacterCount': sum(row['targetPage'].count('\ufffd') for row in baseline['characterStatBindings']),
                'reason': 'Historical target-page text contains decoding replacement characters; routes are re-derived from frozen plan section 6.3 using stable IDs.'},
            'equipmentPageImplementation': {
                'status': 'implemented_opt_in', 'runtimeEnabled': False,
                'entrypoint': evidence('Client/Scenes/Views/CharacterDialog.GameUi.cs', 'public void UseGameUiEquipmentPages(GameUiCharacterEquipmentLayout layout)'),
                'catalog': evidence('Client/Envir/GameUiCharacterEquipmentLayout.cs', 'public static GameUiCharacterEquipmentLayout CreateOfficialCandidate()'),
                'test': 'tests/game-ui/OfficialUiCharacterPageTests.cs',
                'verification': 'docs/design/assets/official-ingame-ui/acceptance/preparatory/character-pages/verification.json',
                'scope': 'Four headers/pages and original 16 equipment cells. Attribute placement has its own opt-in API below; composition has its own optional API below; final art remains pending.'},
            'attributePlacementImplementation': {
                'status': 'implemented_opt_in', 'runtimeEnabled': False,
                'entrypoint': evidence('Client/Scenes/Views/CharacterDialog.GameUiAttributes.cs', 'public void UseGameUiAttributes(GameUiCharacterAttributeLayout layout)'),
                'catalog': evidence('Client/Envir/GameUiCharacterAttributeLayout.cs', 'public static GameUiCharacterAttributeLayout CreateOfficialCandidate()'),
                'test': 'tests/game-ui/OfficialUiCharacterAttributeTests.cs',
                'verification': 'docs/design/assets/official-ingame-ui/acceptance/preparatory/character-attributes/verification.json',
                'originalValues': 57, 'originalCaptions': 27, 'originalTagIcons': 30,
                'scope': 'Reuse and route all 57 dictionary values, original captions and Tag icons; fixed overflow bounds and complete value tooltips; preserve UpdateStats and restore legacy geometry/z-order/subscriptions. Named labels and controls have a separate optional API below; composition has its own optional API below; artwork remains pending.'},
            'namedControlPlacementImplementation': {
                'status': 'implemented_opt_in', 'runtimeEnabled': False,
                'entrypoint': evidence('Client/Scenes/Views/CharacterDialog.GameUiControls.cs', 'public void UseGameUiControls(GameUiCharacterControlLayout layout)'),
                'catalog': evidence('Client/Envir/GameUiCharacterControlLayout.cs', 'public static GameUiCharacterControlLayout CreateOfficialCandidate()'),
                'test': 'tests/game-ui/OfficialUiCharacterControlTests.cs',
                'verification': 'docs/design/assets/official-ingame-ui/acceptance/preparatory/character-controls/verification.json',
                'originalControls': 20, 'addedCaptions': 4,
                'scope': 'Preserve original name/guild/weight/points/marriage/helmet/confirmation/buttons and handlers; fixed checkbox hit areas; separate point caption/value; preserve live text/colors/visibility/checked state on rollback. Local in-memory packet queues only; composition has its own optional API below; final artwork remains pending.'},
            'compositionImplementation': {
                'status': 'implemented_opt_in', 'runtimeEnabled': False,
                'entrypoint': evidence('Client/Scenes/Views/CharacterDialog.GameUiComposition.cs', 'public void UseGameUiComposition(MainPanel source, GameUiCharacterCompositionLayout layout)'),
                'catalog': evidence('Client/Envir/GameUiCharacterCompositionLayout.cs', 'public static GameUiCharacterCompositionLayout CreateOfficialCandidate()'),
                'test': 'tests/game-ui/OfficialUiCharacterCompositionTests.cs',
                'verification': 'docs/design/assets/official-ingame-ui/acceptance/preparatory/character-composition/verification.json',
                'scope': 'Compose four original HUD mirrors with the optional layouts. Preview uses a compiled window-relative pivot and crops image/overlay/effect layers without scaling. Geometry and hidden controls tested; actual rendering and final artwork remain pending.'},
            'equipment': equipment, 'statBindings': stats, 'existingControls': controls, 'additionalBindings': additional,
            'actions': actions,
            'refreshConstraints': [
                'Keep StatsChanged -> CharacterBox.UpdateStats and existing public dictionary/control identities.',
                'Keep User.Stats and User.HermitStats setters refreshing; S.StatsUpdate assigns HermitPoints, Stats, then HermitStats.',
                'Refresh new health/mana labels on StatsChanged as well as current-value changes, since maxima can change.',
                'Refresh new experience on LevelChanged as well as ExperienceChanged; MaxExperience changes through the existing setter.',
                'Preserve original GetFormat ranges, resistance sign/color, overweight colors, icon Tag and confirmation/cancel handlers.',
            ],
            'referenceDecision': {'historicalPlanImage': {'library': 'UI', 'index': 100, 'width': 366, 'height': 484},
                'runtimeMatchedImage': {'library': 'UI', 'index': 2765, 'width': 412, 'height': 562},
                'sourceSelectionFrozen': False,
                'decisionRequired': 'Resolve current runtime image versus historical plan image in UI-05/06 before deriving final rectangles; never scale old slot coordinates wholesale.'},
            'missingInputs': ['Final asset selection and alpha/blend verification', 'Final 16 slot visual/hit/icon rectangles and character pivot',
                'Field/button rectangles, overflow and scroll behavior', 'Six-resolution placement and combined-window checks',
                'Runtime field/packet comparison and real IME checks'],
            'verificationScope': 'Static source/contract coverage; four readouts, three pages with all 16 equipment cells on the character homepage and all 57 dictionary attributes with original captions/Tag icons implemented via opt-in APIs. Named labels, 20 original controls and 4 captions also have an opt-in adapter. Readout composition and preview pivot/clipping now have a separate optional API; final art and actual rendering remain pending. No GameScene activation, rendering or A03/A05 acceptance is claimed.'}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write-draft', action='store_true')
    parser.add_argument('--manifest', type=pathlib.Path, default=DEFAULT)
    parser.add_argument('--report', type=pathlib.Path)
    args = parser.parse_args()
    contract = expected_contract()
    if args.write_draft:
        args.manifest.parent.mkdir(parents=True, exist_ok=True)
        args.manifest.write_text(json.dumps(contract, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    require(load(args.manifest) == contract, 'Draft differs from current verified contract; review changes before regenerating')
    report = {'schemaVersion': 1, 'result': 'pass', 'kind': 'static-character-binding-draft-validation',
              'toolSha256': sha('build/game-ui/Prepare-CharacterBindings.py'),
              'manifestSha256': hashlib.sha256(args.manifest.read_bytes()).hexdigest(),
              'equipmentCount': len(contract['equipment']), 'mainSlots': sorted(MAIN_SLOTS),
              'extraSlots': sorted(set(range(16)) - MAIN_SLOTS),
              'statBindingCount': len(contract['statBindings']),
              'statCountsByTab': dict(collections.Counter(row['targetTab'] for row in contract['statBindings'])),
              'existingControlCount': len(contract['existingControls']), 'additionalBindingCount': len(contract['additionalBindings']),
              'retainedPacketActionCount': len(contract['actions']),
              'productionBindingAllowed': False, 'gameAcceptanceExecuted': False, 'layoutFrozen': False}
    if args.report:
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, ensure_ascii=True, indent=2))

if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError, json.JSONDecodeError) as error:
        print('Character binding draft rejected: ' + str(error), file=sys.stderr)
        sys.exit(1)
