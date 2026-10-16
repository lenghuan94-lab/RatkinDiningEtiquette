"""Prepare a native caregiver-first captive meal, without executing the game.

The hungry child has a controlled native sleeping wait so a warden's normal
work scan initiates supply first. Sleep is an input condition, not a claim that
the game selected this initial job. No RDE job/request is injected. Only the
repository's isolated profile is written; the original native save is kept.
"""
import hashlib
import json
import re
import xml.etree.ElementTree as ET

from PrepareGameFixture import PROFILE, ROOT, prepare, put


if __name__ == '__main__':
    # Validate the observed DefMap schema before creating/replacing any input.
    diagnostic_path = PROFILE / 'Player-visible.log'
    log = diagnostic_path.read_text(encoding='utf-8', errors='replace')
    indices = re.findall(r'Native work indices: Warden=(\d+), Childcare=(\d+)', log)
    assert indices, 'Observe the native work-index diagnostic before preparing this fixture'
    warden_index = int(indices[-1][0])
    record = prepare('PrisonerProviderSleep', True, True)
    path = ROOT / record['path']
    tree = ET.parse(path)
    game = tree.getroot().find('game')
    things = game.find('maps/li/things')
    child = next(x for x in things if x.findtext('id') == 'Ratkin38447')
    assert child.findtext('jobs/curJob/def') == 'Wait_Wander'
    put(child, 'jobs/curJob/def', 'Wait')
    put(child, 'jobs/curJob/expiryInterval', 30000)
    # A controlled native sleeping wait prevents the toddler's constant food
    # think tree from winning the race before a warden's first work scan.
    # This is an input fixture, not proof that the game chose this sleep job.
    put(child, 'jobs/curJob/forceSleep', 'True')
    put(child, 'jobs/curDriver/asleep', 'True')
    for need in child.findall('needs/needs/li'):
        if need.get('Class') == 'Need_Rest':
            put(need, 'curLevel', '.50')
    put(child, 'jobs/curJob/playerForced', 'False')
    helpers = []
    for index, helper in enumerate(x for x in things if x.get('Class') == 'Pawn'
                                   and x.findtext('def') == 'Human'):
        # Keep the supply walk observable before pickup/placement completes.
        position = f'({135 + index}, 0, 135)'
        put(helper, 'pos', position)
        put(helper, 'pather/nextCell', position)
        # Native DefMap serializes only indexed vals, not Def-name keys. The
        # index is read from this profile's actual runtime diagnostic, never
        # assumed from a different installation or Def order.
        priorities = helper.findall('workSettings/priorities/vals/li')
        assert 0 <= warden_index < len(priorities)
        for index_work, priority in enumerate(priorities):
            priority.text = '1' if index_work == warden_index else '0'
        helpers.append(helper.findtext('id'))
    assert not child.findall('carryTracker/innerContainer/innerList/li')
    manager = game.find('maps/li/components/li[@Class="RatkinDiningEtiquette.MapComponent_RatToddlerDining"]')
    assert manager is None or not manager.findall('requests/li')
    tree.write(path, encoding='utf-8', xml_declaration=True)
    record.update(sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                  helperWork=f'Only Warden enabled; native diagnostic index {warden_index}', helpers=helpers,
                  childJob='Controlled native Wait with forceSleep; expiry 30000, rest .50, playerForced false',
                  initialRequests=0, initialHeldFood=None,
                  purpose='Autonomous warden-first BabyFood supply')
    (PROFILE / 'provider-fixture.json').write_text(
        json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(record, ensure_ascii=False, indent=2))
