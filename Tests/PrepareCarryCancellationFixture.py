"""Prepare an isolated, longer native carry route; this does not run the game.

Starts from the tested provider input, preserves its native sleeping child and
Warden-only work setup, and adds connected prison corridors so the real carry
stage can be observed. No dining request, carried child, or RDE job is seeded.
"""
import copy
import hashlib
import json
import xml.etree.ElementTree as ET

from PrepareGameFixture import PROFILE, ROOT, put


if __name__ == '__main__':
    source = PROFILE / 'Saves/RDE-Test-PrisonerProviderSleep.rws'
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    assert source_hash == '3cb86370da8900f2b6a5e51ea9b1c7ce064d5109662944bc6fd58426d18d8724'
    tree = ET.parse(source)
    game = tree.getroot().find('game')
    things = game.find('maps/li/things')
    child = next(t for t in things if t.findtext('id') == 'Ratkin38447')
    food = next(t for t in things if t.findtext('id') == 'BabyFood38492')
    chair = next(t for t in things if t.findtext('id') == 'DiningChair38491')
    put(child, 'pos', '(151, 0, 139)')
    put(child, 'pather/nextCell', '(151, 0, 139)')
    put(food, 'pos', '(152, 0, 139)')
    put(chair, 'pos', '(159, 0, 131)')
    helpers = [t for t in things if t.get('Class') == 'Pawn' and t.findtext('def') == 'Human']
    for index, helper in enumerate(helpers):
        position = f'({151 + index}, 0, 133)'
        put(helper, 'pos', position)
        put(helper, 'pather/nextCell', position)
    template = next(t for t in things if t.findtext('def') == 'Wall')
    next_id = int(game.findtext('uniqueIDsManager/nextThingID'))
    walls = []
    for z, gap in ((134, 159), (136, 151), (138, 159)):
        for x in range(151, 160):
            if x == gap:
                continue
            assert not any(t.findtext('pos') == f'({x}, 0, {z})' for t in things)
            wall = copy.deepcopy(template)
            put(wall, 'id', f'Wall{next_id}')
            put(wall, 'pos', f'({x}, 0, {z})')
            next_id += 1
            things.append(wall)
            walls.append((x, z))
    put(game, 'uniqueIDsManager/nextThingID', next_id)
    manager = next(t for t in game.findall('maps/li/components/li')
                   if t.get('Class') == 'RatkinDiningEtiquette.MapComponent_RatToddlerDining')
    assert not manager.findall('requests/li')
    assert not child.findall('carryTracker/innerContainer/innerList/li')
    assert all(t.find('jobs/curJob').get('IsNull') == 'True' for t in helpers)
    path = PROFILE / 'Saves/RDE-Test-CarryCancellation.rws'
    tree.write(path, encoding='utf-8', xml_declaration=True)
    record = {'path': str(path.relative_to(ROOT)), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
              'source': str(source.relative_to(ROOT)), 'sourceSha256': source_hash,
              'executedInGame': False, 'initialRequests': 0, 'initialHeldFood': None,
              'initialChildJob': 'Controlled native sleeping Wait, not an injected dining job',
              'child': child.findtext('pos'), 'chair': chair.findtext('pos'), 'food': food.findtext('pos'),
              'walls': walls, 'purpose': 'Observe actual carrying, then disable assistance through UI'}
    (PROFILE / 'carry-cancellation-fixture.json').write_text(
        json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(record, ensure_ascii=False, indent=2))
