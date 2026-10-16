"""Prepare isolated native-save fixtures; game execution is a separate check.

Only the repository's game-validation profile is written. The native baseline
and normal player saves are never modified. Schema comes from this installed
RimWorld 1.6 save and its read-only serialization implementation.
"""
import base64
import copy
import hashlib
import json
import re
import struct
import xml.etree.ElementTree as ET
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROFILE = ROOT / '.tools/game-validation-1.0.1'
BASE = PROFILE / 'Saves/RDE-Validation-Base.rws'


def put(node, path, value):
    current = node
    for name in path.split('/'):
        found = current.find(name)
        if found is None:
            found = ET.SubElement(current, name)
        current = found
    current.attrib.pop('IsNull', None)
    current.text = str(value)
    return current


def clear(node, path):
    child = node.find(path)
    if child is not None:
        child.clear()


def short_hash(name):
    # Verse.GenText.StableStringHash and ShortHashGiver, signed C# remainder.
    value = 23
    for char in name:
        value = (value * 31 + ord(char)) & 0xffffffff
    if value >= 0x80000000:
        value -= 0x100000000
    remainder = abs(value) % 65535 * (1 if value >= 0 else -1)
    return remainder & 0xffff


def prepare(case, toddler, prisoner, chair=True):
    tree = ET.parse(BASE)
    game = tree.getroot().find('game')
    map_node = game.find('maps/li')
    things = map_node.find('things')
    pawn = next(t for t in things if t.findtext('def') == 'Ratkin')
    wait_jobs = copy.deepcopy(next(t.find('jobs') for t in things
                                  if t.findtext('jobs/curJob/def') == 'Wait_Wander'))
    templates = {}
    for name in ('Wall', 'Door', 'AncientBed', 'MealSurvivalPack'):
        templates[name] = copy.deepcopy(next(t for t in things if t.findtext('def') == name))
    # Remove scenery in the test room; retain all native pawns and outside map.
    for thing in list(things):
        pos = re.findall(r'-?\d+', thing.findtext('pos', ''))
        if len(pos) == 3 and 148 <= int(pos[0]) <= 162 and 128 <= int(pos[2]) <= 142 and thing.get('Class') != 'Pawn':
            things.remove(thing)
    next_id = int(game.findtext('uniqueIDsManager/nextThingID'))

    def add(def_name, x, z, template, cls=None):
        nonlocal next_id
        thing = copy.deepcopy(templates[template])
        if cls:
            thing.set('Class', cls)
        put(thing, 'def', def_name)
        put(thing, 'id', def_name + str(next_id))
        next_id += 1
        put(thing, 'pos', f'({x}, 0, {z})')
        put(thing, 'faction', 'Faction_17')
        put(thing, 'forbidden', 'False')
        things.append(thing)
        return thing

    for x in range(150, 161):
        for z in range(130, 141):
            if x in (150, 160) or z in (130, 140):
                add('Door' if (x, z) == (150, 135) else 'Wall', x, z,
                    'Door' if (x, z) == (150, 135) else 'Wall')
    bed = add('Bed', 152, 132, 'AncientBed')
    put(bed, 'stuff', 'WoodLog')
    put(bed, 'health', 100)
    put(bed, 'rot', 0)
    put(bed, 'forOwnerType', 'Prisoner' if prisoner else 'Colonist')
    clear(bed, 'assignedPawns')
    put(bed, 'assignedPawns/li', 'Thing_' + pawn.findtext('id'))
    put(pawn, 'ownership/ownedBed', 'Thing_' + bed.findtext('id'))
    if chair:
        seat = add('DiningChair', 155, 135, 'Wall', 'Building')
        put(seat, 'stuff', 'WoodLog')
        put(seat, 'health', 100)
        put(seat, 'quality', 'Normal')
    # A normal preferred meal makes autonomous food selection deterministic.
    # Raw-food rejection is a separate vanilla preference, not a dining failure.
    food = add('BabyFood' if toddler else 'MealSimple', 157, 135, 'MealSurvivalPack', 'ThingWithComps')
    # Native ordinary meals have no faction; ownership on an item is not a
    # normal prisoner-food fixture and can alter reservation/food selection.
    put(food, 'faction', 'null')
    put(food, 'stackCount', 50 if toddler else 10)
    clear(food, 'ingredients')
    put(pawn, 'pos', '(154, 0, 135)')
    put(pawn, 'pather/nextCell', '(154, 0, 135)')
    put(pawn, 'pather/moving', 'False')
    put(pawn, 'faction', 'Faction_8' if prisoner else 'Faction_17')
    put(pawn, 'guest/hostFaction', 'Faction_17' if prisoner else 'null')
    put(pawn, 'guest/guestStatus', 'Prisoner' if prisoner else 'Guest')
    put(pawn, 'guest/interactionMode', 'MaintainOnly')
    clear(pawn, 'healthTracker/hediffSet/hediffs')
    clear(pawn, 'inventory/innerContainer/innerList')
    clear(pawn, 'inventory/itemsNotForSale')
    if toddler:
        # Candidate heating layout only: native loading still minifies this
        # battery, so it does not prove a powered or constant-temperature room.
        battery = add('Battery', 158, 132, 'Wall', 'Building_Battery')
        # Keep the intended 1x2 footprint north. Native spawn behavior must be
        # checked separately before claiming the heater is powered.
        put(battery, 'rot', 0)
        put(battery, 'storedPower', 600)
        heater = add('Heater', 157, 132, 'Wall', 'Building_Heater')
        put(heater, 'targetTemperature', 24)
        for x in (157, 158):
            add('PowerConduit', x, 132, 'Wall', 'Building')
        put(pawn, 'ageTracker/ageBiologicalTicks', 7200000)
        put(pawn, 'ageTracker/birthAbsTicks', -6525888)
        put(pawn, 'story/bodyType', 'Baby')
        put(pawn, 'story/adulthood', 'null')
        clear(pawn, 'apparel/wornApparel/innerList')
        hed = pawn.find('healthTracker/hediffSet/hediffs')
        hid = int(game.findtext('uniqueIDsManager/nextHediffID'))
        for offset, (name, cls, severity) in enumerate((
                ('LearningManipulation', 'Toddlers.Hediff_LearningManipulation', '.45'),
                ('LearningToWalk', 'Toddlers.Hediff_LearningToWalk', '.85'))):
            li = ET.SubElement(hed, 'li', {'Class': cls})
            for key, value in {'def': name, 'loadID': hid + offset,
                               'severity': severity, 'ageTicks': 0}.items():
                put(li, key, value)
        put(game, 'uniqueIDsManager/nextHediffID', hid + 2)
    for index, p in enumerate(t for t in things if t.get('Class') == 'Pawn' and t.findtext('def') in ('Human', 'Ratkin')):
        put(p, 'jobs/curJob', '').set('IsNull', 'True')
        put(p, 'jobs/curDriver', '').set('IsNull', 'True')
        clear(p, 'jobs/jobQueue/jobs')
        for need in p.findall('needs/needs/li'):
            if need.get('Class') == 'Need_Food':
                # Ratkin toddler's native food maximum is 0.1 here: 0.02 is
                # 20%, above the 3% critical bypass and below want-eat level.
                put(need, 'curLevel', '.02' if p is pawn and toddler else '.15' if p is pawn else '.95')
            elif need.get('Class') == 'Need_Rest':
                put(need, 'curLevel', '.95')
        if p is not pawn:
            put(p, 'pos', f'({145-index}, 0, 135)')
            put(p, 'pather/nextCell', p.findtext('pos'))
            put(p, 'pather/moving', 'False')
            for priority in p.findall('workSettings/priorities/vals/li'):
                priority.text = '1'
            for hour in p.findall('timetable/times/li'):
                hour.text = 'Anything'
    # Let native room/bed registries settle before the first prisoner AI scan.
    if prisoner:
        jobs = pawn.find('jobs')
        for path in ('curJob', 'curDriver'):
            jobs.remove(jobs.find(path))
            jobs.append(copy.deepcopy(wait_jobs.find(path)))
        jid = int(game.findtext('uniqueIDsManager/nextJobID'))
        tick = int(game.findtext('tickManager/ticksGame'))
        put(jobs, 'curJob/loadID', jid)
        put(jobs, 'curJob/startTick', tick)
        put(jobs, 'curJob/expiryInterval', 300)
        put(jobs, 'curDriver/startTick', tick)
        put(jobs, 'curDriver/ticksLeftThisToil', 0)
        put(game, 'uniqueIDsManager/nextJobID', jid + 1)
    # Removed native jobs must not leave references in map reservation records.
    changed_pawns = {'Thing_' + t.findtext('id') for t in things
                     if t.get('Class') == 'Pawn' and t.findtext('def') in ('Human', 'Ratkin')}
    for records in map_node.findall('pawnDestinationReservationManager/reservedDestinations/values/li/list'):
        for record in list(records):
            if record.findtext('claimant') in changed_pawns:
                records.remove(record)
    # MapFileCompressor restores compressible rocks after ordinary things;
    # clear this grid too, otherwise those rocks can replace fixture walls.
    compressed = map_node.find('compressedThingMapDeflate')
    compressed_data = bytearray(zlib.decompress(base64.b64decode(compressed.text), -15))
    assert len(compressed_data) == 250 * 250 * 2
    for x in range(148, 163):
        for z in range(128, 143):
            struct.pack_into('<H', compressed_data, 2 * (x + z * 250), 0)
    compressor = zlib.compressobj(wbits=-15)
    compressed.text = base64.b64encode(compressor.compress(compressed_data) + compressor.flush()).decode()
    if toddler:
        temperatures = map_node.find('temperatureCache/temperaturesDeflate')
        temperature_data = bytearray(zlib.decompress(base64.b64decode(temperatures.text), -15))
        assert len(temperature_data) == 250 * 250 * 2
        for x in range(150, 161):
            for z in range(130, 141):
                struct.pack_into('<H', temperature_data, 2 * (x + z * 250), 32768 + 22 * 16)
        compressor = zlib.compressobj(wbits=-15)
        temperatures.text = base64.b64encode(compressor.compress(temperature_data) + compressor.flush()).decode()
    roof = map_node.find('roofGrid/roofsDeflate')
    data = bytearray(zlib.decompress(base64.b64decode(roof.text), -15))
    assert len(data) == 250 * 250 * 2
    constructed = short_hash('RoofConstructed')
    assert constructed in struct.unpack('<62500H', data), 'Native constructed-roof hash unavailable'
    for x in range(150, 161):
        for z in range(130, 141):
            struct.pack_into('<H', data, 2 * (x + z * 250), constructed)
    compressor = zlib.compressobj(wbits=-15)
    roof.text = base64.b64encode(compressor.compress(data) + compressor.flush()).decode()
    put(game, 'uniqueIDsManager/nextThingID', next_id)
    put(game, 'cameraMap/camRootPos', '(154, 28.2653065, 135)')
    put(game, 'cameraMap/desiredSize', 12)
    path = PROFILE / 'Saves' / f'RDE-Test-{case}.rws'
    tree.write(path, encoding='utf-8', xml_declaration=True)
    # Fixture saves do not override mod settings. Custody categories other than
    # colonists are now off by default; record the explicit opt-in required for
    # the upcoming game check rather than silently changing the profile.
    required_settings = {}
    if chair:
        if toddler:
            required_settings['enableToddlerHelp'] = True
            required_settings['enablePrisonerToddlerHelp' if prisoner else 'enableColonistToddlerHelp'] = True
        else:
            required_settings['enableChairTable'] = True
    return {'case': case, 'path': str(path.relative_to(ROOT)),
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
            'pawn': pawn.findtext('id'), 'toddler': toddler, 'prisoner': prisoner,
            'chair': chair, 'requiredSettings': required_settings, 'executedInGame': False}


if __name__ == '__main__':
    records = [prepare('Adult', False, False), prepare('PrisonerAdult', False, True),
               prepare('Toddler', True, False), prepare('PrisonerToddler', True, True),
               prepare('PrisonerToddlerFloor', True, True, False)]
    (PROFILE / 'fixture-manifest.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
    print(json.dumps(records, indent=2))
