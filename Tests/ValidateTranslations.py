"""Check runtime translation coverage and preserve formatting/game tokens."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
LANGUAGES = ('ChineseSimplified', 'English', 'Korean', 'Japanese', 'Russian')
LANG_ROOT = ROOT / '1.6/Languages'


def entries(path):
    root = ET.fromstring(path.read_text(encoding='utf-8-sig', errors='strict'))
    assert root.tag == 'LanguageData', path
    result = {}
    for node in root:
        assert node.tag not in result, f'Duplicate key: {path}: {node.tag}'
        assert node.text and node.text.strip(), f'Empty translation: {path}: {node.tag}'
        result[node.tag] = node.text
    return result


base_keyed = entries(LANG_ROOT / 'English/Keyed/RatkinDiningEtiquette.xml')
base_injected = {'ThoughtDef': {}, 'JobDef': {}, 'WorkGiverDef': {}}
for filename in ('ThoughtDefs.xml', 'LegacyDiningThoughts.xml', 'JobDefs.xml', 'WorkGiverDefs.xml'):
    for definition in ET.parse(ROOT / '1.6/Defs' / filename).getroot():
        name = definition.findtext('defName')
        target = base_injected[definition.tag]
        if definition.tag == 'ThoughtDef':
            for index, stage in enumerate(definition.find('stages')):
                for field in ('label', 'description'):
                    target[f'{name}.stages.{index}.{field}'] = stage.findtext(field)
        else:
            fields = ('reportString',) if definition.tag == 'JobDef' else ('label', 'verb', 'gerund')
            for field in fields:
                target[f'{name}.{field}'] = definition.findtext(field)


def tokens(text):
    return sorted(re.findall(r'\{[^{}]+\}|Target[ABC]', text))


for language in LANGUAGES:
    localized = entries(LANG_ROOT / language / 'Keyed/RatkinDiningEtiquette.xml')
    assert localized.keys() == base_keyed.keys(), f'Keyed coverage: {language}'
    for key, original in base_keyed.items():
        text = localized[key]
        assert tokens(text) == tokens(original), f'Formatting tokens: {language}: {key}'
        if key.startswith('RDE_BabyTalk_') or key == 'RDE_RequestMote_Low':
            assert text == original, f'Keep letter babbling: {language}: {key}'
        if language != 'English' and not key.startswith('RDE_BabyTalk_') and key != 'RDE_RequestMote_Low':
            assert text != original, f'Untranslated Keyed text: {language}: {key}'
    tooltip = localized['RDE_EnableToddlerHelpTooltip']
    setting_name = '允许喂食能自主吃饭的婴儿' if language == 'ChineseSimplified' else 'Pawns feed toddlers who could feed themselves'
    assert setting_name in tooltip, f'Toddlers setting identifier: {language}'
    count = len(localized)
    for definition_type, expected in base_injected.items():
        translated = {}
        injection_path = LANG_ROOT / language / 'DefInjected' / definition_type
        if injection_path.exists():
            for path in injection_path.glob('*.xml'):
                values = entries(path)
                assert not translated.keys() & values.keys(), f'Duplicate injected keys: {language}: {path}'
                translated.update(values)
        if language == 'English':
            assert not translated.keys() - expected.keys(), f'Unknown English injections: {definition_type}'
            translated = {**expected, **translated}  # Native Def text is English.
        assert translated.keys() == expected.keys(), f'Def coverage: {language}: {definition_type}'
        for key, original in expected.items():
            assert tokens(translated[key]) == tokens(original), f'Job/formatting tokens: {language}: {key}'
            if language != 'English':
                assert translated[key] != original, f'Untranslated Def text: {language}: {key}'
        count += len(translated)
    print(f'PASS {language}: {count} strings; complete coverage and matching tokens.')

version_xml = {v: {p.relative_to(ROOT / v): p for p in (ROOT / v).rglob('*.xml')} for v in ('1.6', '1.7')}
assert version_xml['1.6'].keys() == version_xml['1.7'].keys(), 'Both versions must provide the same Defs/translations'
for name, path in version_xml['1.6'].items():
    assert path.read_bytes() == version_xml['1.7'][name].read_bytes(), f'Version content differs: {name}'
print('PASS 1.6/1.7 Defs and five-language content match exactly.')
xml_paths = list(version_xml['1.6'].values()) + list(version_xml['1.7'].values()) + list((ROOT / 'About').rglob('*.xml')) + [ROOT / 'LoadFolders.xml']
for path in xml_paths:
    ET.fromstring(path.read_text(encoding='utf-8-sig', errors='strict'))
print(f'PASS {len(xml_paths)} runtime XML files: strict UTF-8 and valid XML.')
