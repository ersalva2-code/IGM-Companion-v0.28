from pathlib import Path
import json,struct
r=Path(__file__).resolve().parent.parent/'test-output';r.mkdir(exist_ok=True)
def address(s,g=9):return {'RealityIndex':g,'GalacticAddress':{'VoxelX':-2048,'VoxelY':-128,'VoxelZ':2047,'SolarSystemIndex':s}}
def ua(s,g=9):return s<<40|g<<32|128<<24|2047<<12|2048
player={'PersistentPlayerBases':[{'BaseType':{'PersistentBaseTypes':'PlayerSpaceStationBase'},'Owner':{'UID':'xbox-owner','PTK':'XB'},'GalacticAddress':ua(s),'Name':'Station '+str(s)} for s in [12,13]],'TeleportEndpoints':[{'TeleporterType':'Spacestation','AllianceId':{'Data':[1,2,3,4]},'UniverseAddress':address(s)} for s in [12,14]]}
doc={'ActiveContext':'Main','BaseContext':{'PlayerStateData':player},'DiscoveryManagerData':{'DiscoveryData-v1':{'Store':{'Record':[{'DD':{'DT':'SolarSystem','UA':ua(12)},'DM':{'CN':'Новая столица'}},{'DD':{'DT':'SolarSystem','UA':'invalid'},'DM':{'CN':'Bad'}}]}}}}
(r/'mapped.json').write_text('\ufeff  '+json.dumps(doc,ensure_ascii=False),encoding='utf8')
mapping={'ActiveContext': 'XTp', 'BaseContext': 'vLc', 'ExpeditionContext': '2YS', 'PlayerStateData': '6f=', 'PersistentPlayerBases': 'F?0', 'BaseType': 'peI', 'PersistentBaseTypes': 'DPp', 'Owner': '3?K', 'UID': 'K7E', 'PTK': 'D6b', 'GalacticAddress': 'oZw', 'Name': 'NKm', 'TeleportEndpoints': 'nlG', 'TeleporterType': 'iAF', 'AllianceId': '6H2', 'Data': '8?J', 'UniverseAddress': 'yhJ', 'RealityIndex': 'Iis', 'VoxelX': 'dZj', 'VoxelY': 'IyE', 'VoxelZ': 'uXE', 'SolarSystemIndex': 'vby', 'DiscoveryManagerData': 'fDu', 'DiscoveryData-v1': 'ETO', 'Store': 'OsQ', 'Record': '?fB', 'DD': '8P3', 'DT': '<Dn', 'UA': '5L6', 'DM': 'q9a', 'CN': 'q5u'}
def obf(o):
 if isinstance(o,dict):return {mapping.get(k,k):obf(v) for k,v in o.items()}
 if isinstance(o,list):return [obf(x) for x in o]
 return o
(r/'obfuscated.json').write_text(json.dumps(obf(doc)),encoding='utf8')
exp=dict(doc,ActiveContext='Expedition',ExpeditionContext=doc['BaseContext'],BaseContext={});(r/'expedition.json').write_text(json.dumps(exp))
conf=json.loads(json.dumps(doc));conf['BaseContext']['PlayerStateData']['TeleportEndpoints'].append({'TeleporterType':'Spacestation','AllianceId':{'Data':[9,8,7,6]},'UniverseAddress':address(12)});(r/'conflict.json').write_text(json.dumps(conf))
b=json.dumps(doc).encode();n=len(b);block=bytes([0xf0])+bytes([255])*((n-15)//255)+bytes([(n-15)%255])+b;(r/'literal.hg').write_bytes(struct.pack('<4I',0xfeeda1e5,len(block),n,0)+block)
