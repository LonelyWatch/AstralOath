"""Generate the server enhancement catalog from reviewable, selected numeric CSVs.

--import-export is deliberately explicit: it reads a local investigation export,
never traverses or imports the external original-assets directory.
"""
from pathlib import Path
from decimal import Decimal, ROUND_CEILING, ROUND_FLOOR, ROUND_HALF_UP
import argparse, csv, json, hashlib

ROOT=Path(__file__).resolve().parents[2]
TABLES=ROOT/'Config/Tables'
OUT=ROOT/'Server/Host/Inventory/EquipmentEnhancementSource.g.cs'
KIND={'att':'attack','max_hp':'health','def':'defense','speed':'speed','att_rate':'attackPct',
      'max_hp_rate':'healthPct','def_rate':'defensePct','cri':'critChanceBp','cri_dmg':'critDamageBp',
      'acc':'effectivenessBp','res':'resistanceBp'}
FAMILY={'attack':'attack','health':'health','defense':'defense','speed':'speed','attackPct':'percent',
        'healthPct':'percent','defensePct':'percent','effectivenessBp':'percent','resistanceBp':'percent',
        'critChanceBp':'crit_chance','critDamageBp':'crit_damage'}
SLOT={'w':'weapon','h':'helmet','a':'armor','b':'boots','n':'necklace','r':'ring'}

def write_csv(name, fields, rows):
    with (TABLES/name).open('w',newline='',encoding='utf-8') as f:
        writer=csv.DictWriter(f,fieldnames=fields); writer.writeheader(); writer.writerows(rows)

def import_selected(folder):
    def rows(name): return json.loads((folder/(name+'.source.json')).read_text(encoding='utf-8'))['rows']
    items={r['id']:r for r in rows('equip_item')}
    stats=rows('equip_stat'); revisions={r['stat_type']:r for r in rows('item_equip_stat_revision')}
    enhance=rows('itemenhance'); grades={r['grade']:r for r in rows('itemgrade')}
    profile_rows=[]
    for level,tier in [(70,5),(85,6),(90,7)]:
        for short,slot in SLOT.items():
            item=items[f'ecw{tier}{short}' if level!=90 else f'ecw6{short}_u']
            for stat in stats:
                if not stat['id'].startswith(item['main_stat']+'_'): continue
                kind=KIND[stat['stat_type']]; is_pct=kind not in ['attack','health','defense','speed']
                raw=Decimal(stat['val_min'])
                factor=Decimal(revisions[stat['stat_type']]['revise_max'])
                basis=int((raw*factor*(100 if is_pct else 1)).to_integral_value(rounding=ROUND_FLOOR))
                for grade_id,grade in [('1','normal'),('2','good'),('3','rare'),('4','heroic'),('5','epic')]:
                    xp=Decimal(item['xp'])*Decimal(grades[grade_id]['need_xp_rate'])
                    for step in range(16):
                        # The equipment model stores integer displayed percentage points in BP.
                        # +9 HP% is visibly 34% (12%+22%) in the reference. This project
                        # quantizer matches that display; it does not recover hidden source precision.
                        rounding=ROUND_HALF_UP if is_pct else ROUND_FLOOR
                        value=int((Decimal(basis)*Decimal(enhance[step]['power'])).to_integral_value(rounding=rounding))*(100 if is_pct else 1)
                        threshold=0 if step==0 else int((xp*Decimal(enhance[step-1]['exp'])).to_integral_value(rounding=ROUND_CEILING))
                        profile_rows.append(dict(itemLevel=level,grade=grade,slot=slot,mainKind=kind,enhance=step,mainValue=value,xpThreshold=threshold))
    write_csv('equipment_enhancement_profiles.csv',list(profile_rows[0]),profile_rows)
    official=json.loads((folder/'official/distributions.json').read_text(encoding='utf-8'))
    roll_rows=[]
    for level,band in [(70,'58-71'),(85,'72-85')]:
        for family,by_grade in official['distributions'][band].items():
            for grade,outcomes in by_grade.items():
                for outcome in outcomes:
                    roll_rows.append(dict(itemLevel=level,grade=grade,family=family,value=outcome['amount'],
                        weight=int(Decimal(str(outcome['probability_percent']))*1000)))
    write_csv('equipment_enhancement_rolls.csv',list(roll_rows[0]),roll_rows)
    material_rows=[]
    scopes={**{short:[slot] for short,slot in SLOT.items()},'e':['weapon','helmet','armor','boots'],'j':['necklace','ring']}
    for material in rows('item_material'):
        name=material['id']
        if not name.startswith('ma_est') or len(name)!=8 or name[6] not in '1246' or name[7] not in scopes:continue
        material_rows.append(dict(id=name,slots='|'.join(scopes[name[7]]),experience=material['get_xp'],
            goldPerUnit={'1':1600,'2':14400,'4':43200,'6':129600}[name[6]]))
    write_csv('equipment_enhancement_materials.csv',list(material_rows[0]),material_rows)
    manifest={'sourceRelease':'pack-15-r4262','tables':[],
      'officialProbability':{k:official[k] for k in ['source_page','source_api','edited_utc','body_probability_effective_date','warning']},
      'mainRuleEvidence':'raw main val_min * stat_revision.revise_max, floor fixed-stat basis before power; 2026-11-25 video85 boots displayed HP% +3=19/+6=26/+9=34 (69s,12%+22%); project uses ROUND_HALF_UP to integer displayed percentage points then BP, without claiming recovered hidden source precision; official table01 flat ATK100; 2026-11-17 video00:08.5 90 red/purple weapon525/armor310, helmet2835 supports fixed DEF floor62.5 before power5; intermediate speed rounding not separately verified',
      'experienceEvidence':'ceil(item.xp * itemgrade.need_xp_rate * itemenhance[level-1].exp), cumulative; video 1982 to+3 with13 retained and next1050'}
    manifest['economyScope']='recorded-economy-v1: recorded account effective costs, without inferring hidden discounts; project experience bonus is 0%'
    manifest['economyEvidence']='Screenrecorder-2026-11-25-19-54-45-217.mp4: 70.5/71.5/72.5s auto budgets147200/328000/684800; 74s17600;89-90s43200; powder1600/lesser14400/greater43200/epic129600'
    manifest['precisionBoundary']='Percentage main stats use ROUND_HALF_UP to integer displayed percentage points before BP as project policy; original internal BP precision is not recovered. Fixed-stat basis and enhanced fixed/speed values retain floor; intermediate speed rounding is not separately verified. Integer cumulative thresholds use ceil as a deterministic project quantizer; video magic-control+15 shows82031EXP versus project threshold82032, which may reflect retained fractions or special full-enhance handling, so that path does not verify ordinary threshold precision. Published probability weights are rounded official percentages normalized in each draw, not recovered original RNG weights. GM initial stat validation checks published numeric bounds and upgrade chronology; no initial-drop probability claim.'
    for name in ['equip_item','equip_stat','item_equip_stat_revision','itemgrade','itemenhance','item_material']:
        source=json.loads((folder/(name+'.source.json')).read_text(encoding='utf-8'))['source']
        manifest['tables'].append({k:source[k] for k in ['name','sha256','bytes','rows']})
    (TABLES/'equipment_enhancement_sources.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def read_csv(name):
    with (TABLES/name).open(encoding='utf-8-sig',newline='') as f:return list(csv.DictReader(f))

def cs(value):return json.dumps(value,ensure_ascii=False)
def generate():
    profiles=read_csv('equipment_enhancement_profiles.csv'); rolls=read_csv('equipment_enhancement_rolls.csv'); materials=read_csv('equipment_enhancement_materials.csv')
    grouped={}
    for row in profiles:grouped.setdefault(tuple(row[k] for k in ['itemLevel','grade','slot','mainKind']),[]).append(row)
    lines=['// <auto-generated />','// Run Tools/Config/Generate-EquipmentEnhancement.py; edit Config/Tables/equipment_enhancement_*.csv.',
           'namespace AstralOath.Server.Inventory;','internal static class EquipmentEnhancementSource','{',
           '    internal static EquipmentEnhancementProfile[] Profiles() =>','    [']
    for (level,grade,slot,kind),rows in grouped.items():
        rows.sort(key=lambda r:int(r['enhance']));assert [int(r['enhance']) for r in rows]==list(range(16))
        mains=','.join(r['mainValue'] for r in rows);xp=','.join(r['xpThreshold'] for r in rows)
        supported=grade in ['rare','heroic','epic'] and level!='90'
        lines.append(f'        new({cs("source-"+"-".join([level,grade,slot,kind]))}, {level}, {cs(grade)}, {cs(slot)}, {cs(kind)}, [{mains}], [{xp}], {str(supported).lower()}, '+('null' if supported else '"equipment_enhancement_cap"' if level=='90' else '"equipment_grade_unsupported"')+'),')
    lines.extend(['    ];','    internal static EquipmentEnhancementRoll[] Rolls() =>','    ['])
    for row in rolls:
        lines.append('        new('+','.join([row['itemLevel'],cs(row['grade']),cs(row['family']),row['value'],row['weight']])+'),')
    lines.extend(['    ];','    internal static EquipmentEnhancementMaterialRule[] Materials() =>','    ['])
    for row in materials:
        lines.append('        new('+cs(row['id'])+', ['+','.join(cs(s) for s in row['slots'].split('|'))+'], '+row['experience']+', '+row['goldPerUnit']+'),')
    lines.extend(['    ];','}'])
    OUT.write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(f'Generated {len(grouped)} profiles and {len(rolls)} published probability outcomes: {OUT}')

parser=argparse.ArgumentParser();parser.add_argument('--import-export',type=Path);args=parser.parse_args()
if args.import_export:import_selected(args.import_export)
generate()
