"""Reproducible scaffolding; does not touch the legacy project or user saves."""
from pathlib import Path
import json
R=Path(__file__).resolve().parents[1]
def write(path, data):
    p=R/path;p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(data if isinstance(data,str) else json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')

write('ProjectSettings/ProjectVersion.txt','m_EditorVersion: 6000.3.10f1\nm_EditorVersionWithRevision: 6000.3.10f1 (e35f0c77bd8e)\n')
write('Packages/manifest.json',{'dependencies':{
 'com.unity.render-pipelines.universal':'17.3.0','com.unity.inputsystem':'1.18.0',
 'com.unity.ugui':'2.0.0','com.unity.xr.openxr':'1.16.1','com.unity.xr.management':'4.5.4',
 'com.unity.test-framework':'1.6.0','com.unity.modules.physics':'1.0.0',
 'com.unity.modules.vehicles':'1.0.0','com.unity.modules.jsonserialize':'1.0.0',
 'com.unity.modules.imgui':'1.0.0','com.unity.modules.ui':'1.0.0','com.unity.modules.audio':'1.0.0',
 'com.unity.modules.animation':'1.0.0','com.unity.modules.imageconversion':'1.0.0'}})
write('.gitignore','Library/\nTemp/\nObj/\nLogs/\nUserSettings/\nBuilds/\n*.csproj\n*.sln\n*.blend1\n*.log\nartifacts/test-userdata/\n')
modules={'Contracts':[], 'Simulation':['Contracts'], 'World':['Contracts'], 'Input':['Contracts'],
 'Rules':['Contracts'], 'Learning':['Contracts'], 'Presentation':['Contracts','Presentation.Physics','Simulation','World','Input','Rules','Learning'],
 'Editor':['Contracts','Simulation','World','Learning','Presentation','Presentation.Physics']}
for m,refs in modules.items():
    obj={'name':'DS.'+m,'rootNamespace':'DrivingSchool.'+m,'references':['DS.'+x for x in refs]}
    if m in ('Contracts','Simulation','Rules','Learning'):obj['noEngineReferences']=True
    if m=='Input':obj['references']+=['Unity.InputSystem']
    if m=='Presentation':obj['references']+=['Unity.InputSystem','Unity.RenderPipelines.Universal.Runtime','Unity.RenderPipelines.Core.Runtime']
    if m=='Editor':obj['includePlatforms']=['Editor'];obj['references']+=['Unity.RenderPipelines.Universal.Runtime','Unity.RenderPipelines.Core.Runtime','Unity.InputSystem','Unity.XR.Management','Unity.XR.OpenXR']
    write(f'Assets/DrivingSchool/Code/{m}/DS.{m}.asmdef',obj)
write('Assets/DrivingSchool/Code/Presentation/Physics/DS.Presentation.Physics.asmdef',{'name':'DS.Presentation.Physics','rootNamespace':'DrivingSchool.Presentation.Physics','references':['DS.Contracts','DS.Simulation']})
write('Assets/DrivingSchool/Code/Tests/DS.Tests.asmdef',{'name':'DS.Tests','references':['DS.Contracts','DS.Simulation','DS.World','DS.Learning','DS.Input','DS.Rules','DS.Presentation.Physics'], 'optionalUnityReferences':['TestAssemblies'],'includePlatforms':['Editor']})

nodes=[{'id':'south','x':0,'y':0,'z':-250},{'id':'centre','x':0,'y':0,'z':0},{'id':'north','x':0,'y':0,'z':250},{'id':'west','x':-250,'y':0,'z':0},{'id':'east','x':250,'y':0,'z':0}]
segments=[{'id':n+'-centre','fromNode':n,'toNode':'centre','widthM':14,'laneCount':4,'speedLimitKph':60} for n in ('south','north','west','east')]
lanes=[]
for s in segments:
    for direction in (0,1):
        for index in (0,1):
            start=s['fromNode'] if direction==0 else 'centre';end='centre' if direction==0 else s['fromNode']
            successors=[f"{t['id']}:1:{index}" for t in segments if t['id']!=s['id']] if end=='centre' else []
            lanes.append({'id':f"{s['id']}:{direction}:{index}",'segmentId':s['id'],'fromNode':start,'toNode':end,'widthM':3.5,'index':index,'successors':successors})
world={'schemaVersion':1,'id':'training-district','name':'Учебный квартал','chunkSizeM':256,'nodes':nodes,'segments':segments,'lanes':lanes,'objects':[{'id':'spawn','catalogId':'spawn-car','x':-1.75,'y':.1,'z':-40,'yawDeg':0}], 'districts':[{'id':'demo','minX':-250,'minZ':-250,'sizeM':500}]}
write('Assets/StreamingAssets/Examples/world.json',world)
write('Assets/StreamingAssets/Examples/lesson.json',{'schemaVersion':1,'id':'start-stop-demo','title':'Начало движения и остановка','worldId':'training-district','timeLimitSeconds':120,'targetDistanceM':20,'stopSpeedMps':.14,'requiredStopSeconds':2,'contentStatus':'demonstration'})
write('Assets/StreamingAssets/Examples/theory.json',{'schemaVersion':1,'id':'demo-author-course','revision':'2026-09-18-demo','source':'Авторский демонстрационный пример. Не официальный билет.','isOfficial':False,'questions':[{'id':'demo-clutch','text':'Что произойдёт при слишком резком отпускании сцепления на старте?','answers':['Двигатель может заглохнуть','Всегда включится нейтраль','Сцепление останется разомкнутым'],'correctIndex':0,'explanation':'Нагрузка может снизить обороты двигателя ниже устойчивого холостого хода.','ruleReference':'Учебная механика автомобиля','topic':'clutch'}]})
write('Assets/StreamingAssets/Examples/vehicle.json',{'schemaVersion':1,'id':'ds01','massKg':1350,'lengthM':4.5,'bodyWidthM':1.8,'mirrorWidthM':2.25,'heightM':1.5,'wheelbaseM':2.72,'trackM':1.71,'wheelRadiusM':.327,'centreOfMassM':[0,.51,-.1],'steeringWheelDegrees':900,'gearRatios':[3.6,2.1,1.4,1.05,.84,.69],'reverseRatio':-3.5,'finalDrive':4.1,'idleRpm':850,'redlineRpm':6500,'engineInertiaKgm2':.2,'maxClutchTorqueNm':240,'calibrationStatus':'design-target-not-measured'})
write('artifacts/visual-review/data/masterplan.json',{'sizeM':10000,'districts':[{'id':'centre','name':'Центр','x':3500,'z':3500,'w':2000,'h':2000},{'id':'residential','name':'Жилые кварталы','x':1200,'z':3300,'w':2100,'h':3000},{'id':'industry','name':'Промзона','x':6000,'z':3300,'w':2300,'h':1900},{'id':'suburb','name':'Пригород','x':3000,'z':6500,'w':4000,'h':2000},{'id':'autodrome','name':'Автодром','x':1900,'z':1700,'w':1000,'h':800}], 'routes':[{'id':'beginner','name':'Первый городской маршрут','points':[[2100,4000],[4300,4000],[4300,5100],[2100,5100],[2100,4000]]},{'id':'highway','name':'Загородное вождение','points':[[4300,5000],[4300,7600],[8000,7600],[8800,2000],[4300,2000],[4300,5000]]}], 'status':'masterplan-not-built-city'})
print('Project scaffolding and example data created')
