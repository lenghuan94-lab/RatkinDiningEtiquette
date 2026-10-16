"""Prepare a satiated captive toddler with no chair or dining request.

This is a native baby-play control, not an executed gameplay result. It only
writes the repository's isolated profile and leaves the native baseline intact.
"""
import hashlib
import copy
import json
import xml.etree.ElementTree as ET

from PrepareGameFixture import PROFILE, ROOT, prepare, put


if __name__ == '__main__':
    record = prepare('PrisonerToddlerPlayControl', True, True, chair=False)
    path = ROOT / record['path']
    tree = ET.parse(path)
    game = tree.getroot().find('game')
    pawn = next(x for x in game.iter() if x.findtext('id') == 'Ratkin38447')
    for need in pawn.findall('needs/needs/li'):
        if need.get('Class') == 'Need_Food':
            put(need, 'curLevel', '.1')
        elif need.get('Class') == 'Need_Play':
            put(need, 'curLevel', '0')
    needs = pawn.find('needs/needs')
    if needs.find('li[@Class="Need_Play"]') is None:
        native = ET.parse(PROFILE / 'Saves/RDE-Result-PrisonerToddler-Reload.rws').getroot().find('game')
        native_pawn = next(x for x in native.iter() if x.findtext('id') == 'Ratkin38447')
        play = copy.deepcopy(native_pawn.find('needs/needs/li[@Class="Need_Play"]'))
        put(play, 'curLevel', '0')
        needs.append(play)
    # Match the caregiver's proximity after dropping a child at a chair.
    # Native work scans can use nearby targets before their global lister.
    for index, helper in enumerate(x for x in game.find('maps/li/things')
                                   if x.get('Class') == 'Pawn' and x.findtext('def') == 'Human'):
        position = f'({152 + index}, 0, 135)'
        put(helper, 'pos', position)
        put(helper, 'pather/nextCell', position)
    manager = game.find('maps/li/components/li[@Class="RatkinDiningEtiquette.MapComponent_RatToddlerDining"]')
    assert manager is None or not manager.findall('requests/li')
    assert not any(x.findtext('def') == 'DiningChair' for x in game.find('maps/li/things'))
    tree.write(path, encoding='utf-8', xml_declaration=True)
    record.update(sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                  initialFoodLevel=.1, initialPlayLevel=0,
                  purpose='Native baby play without a chair or active dining request')
    (PROFILE / 'play-control-fixture.json').write_text(
        json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(record, ensure_ascii=False, indent=2))
