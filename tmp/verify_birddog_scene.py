from pathlib import Path
import re, difflib, json

def blocks(path):
    text = Path(path).read_text(encoding='utf-8-sig')
    return {m.group(2): (m.group(1), m.group(0)) for m in re.finditer(r'^--- !u!(\d+) &(-?\d+).*?\n.*?(?=^--- !u!|\Z)', text, re.M | re.S)}

old = blocks('Assets/Scenes/BirdDog_Classic.unity')
new = blocks('Assets/Scenes/BirdDog_RingSkinC.unity')
assert old.keys() <= new.keys(), 'Original objects removed'
lines = [f'All {len(old)} original BirdDog objects retained.']
allowed = {
    'cfde81bd5fb618d42a44bde19aa60c4f': {'visualSkin', 'visualEffectRoot', 'showArrivalIndicator'},
    '320914a73f83cc847be6f46a2d1da993': {'completeVisualRoot', 'leftHandColor', 'rightHandColor', 'leftFootColor', 'rightFootColor'},
    '0cbc58e61a32eb047a96998f293f9c84': {'visualSkin'},
}
same = modified = 0
for ident, (kind, content) in old.items():
    if kind != '114': continue
    other = new[ident][1]
    if content == other:
        same += 1
        continue
    guid = re.search(r'm_Script:.*guid: (\w+)', content).group(1)
    fields = allowed.get(guid, set())
    def without_visual(text):
        return '\n'.join(line for line in text.splitlines() if not any(line.startswith('  '+key+':') for key in fields))
    diff = list(difflib.unified_diff(content.splitlines(), other.splitlines(), lineterm=''))
    lines.extend([f'Component {ident}, script {guid}:', *diff])
    assert fields and without_visual(content) == without_visual(other), f'Unexpected logic change in {ident}'
    modified += 1
lines.insert(1, f'{same} original script components byte-identical (normalized line endings); {modified} have visual-only overrides.')
Path('output/scene-organization/birddog-logic-preservation.txt').write_text('\n'.join(lines)+'\n', encoding='utf-8')
print('\n'.join(lines[:2]))
