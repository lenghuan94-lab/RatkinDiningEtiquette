"""Extract facts from isolated saves produced by the native game UI.

This does not execute or reproduce gameplay, or mark unrun fixtures as passed.
"""
import hashlib
import json
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROFILE = ROOT / '.tools/game-validation-1.0.1'


def summarize(path):
    game = ET.parse(path).getroot().find('game')
    pawn = next(x for x in game.iter() if x.findtext('id') == 'Ratkin38447')
    parents = {c: p for p in game.iter() for c in p}
    ancestor = parents.get(pawn)
    holder = None
    while ancestor is not None:
        if ancestor.get('Class') == 'Pawn':
            holder = {'pawn': ancestor.findtext('id'), 'position': ancestor.findtext('pos'),
                      'job': ancestor.findtext('jobs/curJob/def')}
            break
        ancestor = parents.get(ancestor)
    food = next(x for x in pawn.findall('needs/needs/li') if x.get('Class') == 'Need_Food')
    manager = game.find('maps/li/components/li[@Class="RatkinDiningEtiquette.MapComponent_RatToddlerDining"]')
    return {
        'file': str(path.relative_to(ROOT)),
        'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
        'gameTick': game.findtext('tickManager/ticksGame'),
        'pawn': pawn.findtext('id'), 'position': pawn.findtext('pos'),
        'savedMap': pawn.findtext('map'), 'pawnHolder': holder,
        'faction': pawn.findtext('faction'), 'hostFaction': pawn.findtext('guest/hostFaction'),
        'guestStatus': pawn.findtext('guest/guestStatus'),
        'job': pawn.findtext('jobs/curJob/def'),
        'driver': pawn.find('jobs/curDriver').attrib if pawn.find('jobs/curDriver') is not None else None,
        'heldFood': pawn.findtext('carryTracker/innerContainer/innerList/li/id'),
        'mealTarget': pawn.findtext('jobs/curJob/targetA'),
        'caregiverJobs': [{'pawn': x.findtext('id'), 'position': x.findtext('pos'),
                          'job': x.findtext('jobs/curJob/def'),
                          'workGiver': x.findtext('jobs/curJob/workGiverDef'),
                          'target': x.findtext('jobs/curJob/targetA')}
                         for x in game.iter() if x.get('Class') == 'Pawn'
                         and x.findtext('jobs/curJob/targetA') == 'Thing_' + pawn.findtext('id')],
        'foodCurLevel': food.findtext('curLevel'),
        'requests': [{c.tag: c.text for c in x} for x in manager.findall('requests/li')],
        'nextRequestId': manager.findtext('nextRequestId'),
        'memories': [{c.tag: c.text for c in x}
                     for x in pawn.findall('needs/needs/li/thoughts/memories/memories/li')],
        'babyFoodStacks': [{'id': x.findtext('id'), 'count': x.findtext('stackCount', '1')}
                           for x in game.iter() if x.findtext('def') == 'BabyFood'],
    }


if __name__ == '__main__':
    paths = sorted(set((PROFILE / 'Saves').glob('RDE-Result-*.rws'))
                   | set((PROFILE / 'Saves').glob('RDE-InProgress-*.rws'))
                   | set((PROFILE / 'Saves').glob('RDE-MidMeal-*.rws')))
    records = [summarize(p) for p in paths]
    destination = PROFILE / 'native-result-summary.json'
    destination.write_text(json.dumps(records, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Extracted {len(records)} native saves into {destination.relative_to(ROOT)}.')
    for r in records:
        print(Path(r['file']).stem, r['job'], 'food=' + str(r['foodCurLevel']), 'requests=' + str(len(r['requests'])))
