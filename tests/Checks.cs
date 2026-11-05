using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using IGM;
class Checks {
 static int checks;
 static void Check(bool b,string name){if(!b)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
 static void Refuses(Action a,string name){bool refused=false;try{a();}catch(Exception e){if(e is InvalidDataException||e is FormatException||e is IOException)refused=true;else throw;}Check(refused,name);}
 static SystemRecord Record(string ua,string id,string name){return new SystemRecord{UA=ua,Alliance=id,Name=name};}
 static void Main(string[] args){
  string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
  Check(typeof(Data).Assembly.GetName().Version.ToString()=="0.28.0.0","version 0.28");
  Check(Assets.Symbols.Length>100&&Assets.HasSymbol(Assets.Symbols[0]),"all original embedded symbols");
  using(var im=Assets.Image("Logo"))Check(im.Width>0,"embedded logo");
  Check(Data.Json().Deserialize<Dictionary<string,string[]>>(Assets.Text("Languages.json")).Values.All(x=>x.Length==6),"original six languages embedded");
  var empty=new Project();ImperialCore.Apply(empty);Check(empty.Systems.Count==0&&empty.Alliances.Count==0,"no injected obsolete catalogue");
  Check(Data.UA("0x2044A37323B6")=="00002044a37323b6","system UA retains system/galaxy bits");
  for(int g=0;g<256;g++){
   var gal=new Dictionary<string,object>{{"VoxelX",-2048},{"VoxelY",-128},{"VoxelZ",2047},{"SolarSystemIndex",12}};
   string u=Data.Pack(new Dictionary<string,object>{{"RealityIndex",g},{"GalacticAddress",gal}});
   Check(new SystemRecord{UA=u}.Galaxy==g,"galaxy "+g);
  }
  Refuses(()=>Data.UA("0x10000000000000"),"zero after planet mask rejected");
  byte[] b=Data.ReadSave(Path.Combine(root,"literal.hg"));Check(Data.Utf8.GetString(b).StartsWith("{"),"LZ4 literal chunk");
  var mapped=Data.Import(Path.Combine(root,"mapped.json"),"");var obfuscated=Data.Import(Path.Combine(root,"obfuscated.json"),"");
  Check(mapped.Systems.Count==3&&obfuscated.Systems.Count==3,"mapped and obfuscated save records");
  Check(mapped.Systems.Count(x=>x.Owned)==2&&mapped.Owner=="xbox-owner","Xbox stations included without Steam filter");
  Check(mapped.Systems.Any(x=>x.Name=="Новая столица"),"UTF8 discovery names");
  Check(mapped.Skipped==1,"invalid discovery skipped without losing valid systems");
  Check(mapped.Systems.Single(x=>x.Name=="Новая столица").UA==obfuscated.Systems.Single(x=>x.Name=="Новая столица").UA,"address equivalence");
  var exp=Data.Import(Path.Combine(root,"expedition.json"),"");Check(exp.Systems.Count==3,"active expedition context");
  string season=Path.Combine(root,"season.json");File.WriteAllText(season,File.ReadAllText(Path.Combine(root,"expedition.json")).Replace("\"Expedition\"","\"Season\""));Check(Data.Import(season,"").Systems.Count==3,"active Season context alias");
  int assigned=Data.AssignOwned(mapped,"1_2_3_4");Check(assigned==1&&mapped.Systems.Count(x=>x.Alliance=="1_2_3_4")==3,"explicit profile applies only to unassigned owned stations");
  var conflicting=Data.Import(Path.Combine(root,"conflict.json"),"");Check(conflicting.Skipped==2&&conflicting.Systems.Any(x=>x.Evidence=="conflicting_cache"&&!x.Selected),"conflicting cache quarantined");
  string dir=Path.Combine(root,"IGM_datos");Directory.CreateDirectory(dir);
  string original="{\"system_ua\":\"0000010000000001\",\"alliance_id_words\":[1,2,3,4],\"cached_alliance_name\":\"Existing\",\"future_field\":{\"value\":42}}\r\n";
  File.WriteAllText(Path.Combine(dir,"catalogo_observado.jsonl"),original,Data.Utf8);
  string exploration="0000010000000001\t0\t1\t3\t"+Data.Hex("Custom name")+"\r\n0000010000000001\t2\t-1\t3\t"+Data.Hex("Planet name")+"\r\n";
  File.WriteAllText(Path.Combine(dir,"exploracion.tsv"),exploration,Data.Utf8);
  foreach(string file in new[]{"gestion_sistemas.tsv","ajustes.ini","captura.ini","colores_alianzas.tsv"})File.WriteAllText(Path.Combine(dir,file),"UNRELATED "+file,Data.Utf8);
  var p=new Project();p.GetAlliance("1_2_3_4").Name="Future compatible alliance";p.Systems.Add(Record("0000010000000001","1_2_3_4","Cannot overwrite"));p.Systems.Add(Record("0000020900000002","1_2_3_4","New system"));
  using(var bitmap=new Bitmap(180,180)){using(var g=Graphics.FromImage(bitmap))g.Clear(Color.Red);using(var stream=new MemoryStream()){bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);p.Alliances[0].PreviewPng=Convert.ToBase64String(stream.ToArray());}}
  string result=Data.Export(p,dir,false);Check(result.StartsWith("1 / names: 1 / icons: 1"),"merge exports one new record, name and BMP");
  Check(File.ReadAllText(Path.Combine(dir,"catalogo_observado.jsonl")).StartsWith(original),"unknown JSON fields and original bytes retained");
  Check(File.ReadAllText(Path.Combine(dir,"exploracion.tsv")).StartsWith(exploration),"custom names, pirates and planet rows retained");
  foreach(string file in new[]{"gestion_sistemas.tsv","ajustes.ini","captura.ini","colores_alianzas.tsv"})Check(File.ReadAllText(Path.Combine(dir,file))=="UNRELATED "+file,"untouched "+file);
  byte[] journal=File.ReadAllBytes(Path.Combine(dir,"catalogo_observado.jsonl"));Check(Data.Export(p,dir,false).StartsWith("0 / names: 0 / icons: 0"),"repeat export idempotent");
  Check(journal.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"catalogo_observado.jsonl"))),"repeat export preserves exact bytes");
  Check(Directory.GetFiles(Path.Combine(dir,"copias_companion"),"catalogo_observado.jsonl",SearchOption.AllDirectories).Any(x=>File.ReadAllText(x)==original),"original catalogue backup retained");
  File.AppendAllText(Path.Combine(dir,"exploracion.tsv"),"0000030000000003\t0\t-1\t1\t4142\tFUTURE\n",Data.Utf8);
  p.Systems.Add(Record("0000040000000004","1_2_3_4","New name"));
  Refuses(()=>Data.Export(p,dir,false),"unknown TSV schema refuses write");Check(journal.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"catalogo_observado.jsonl"))),"schema refusal is all-or-nothing");
  string snap=Path.Combine(root,"atomic");Directory.CreateDirectory(snap);File.WriteAllText(Path.Combine(snap,"one"),"newer");
  Refuses(()=>Data.Commit(snap,new Dictionary<string,byte[]>{{"one",Data.Utf8.GetBytes("overwrite")}},new Dictionary<string,byte[]>{{"one",Data.Utf8.GetBytes("old")}}),"concurrent change refused");
  Check(File.ReadAllText(Path.Combine(snap,"one"))=="newer","concurrent update retained");
  Refuses(()=>Data.Commit(snap,new Dictionary<string,byte[]>{{"one",Data.Utf8.GetBytes("overwrite")},{"../escape",new byte[0]}}),"export path cannot escape destination");
  Check(File.ReadAllText(Path.Combine(snap,"one"))=="newer"&&!Directory.GetFiles(snap,"*.tmp").Any(),"staging failure preserves original and removes temp");
  string pref=Path.Combine(snap,"preferences.json");Data.SaveLocal(pref,"old");Data.SaveLocal(pref,"new");Check(File.ReadAllText(pref)=="new"&&File.ReadAllText(pref+".bak")=="old","bounded preference backup");
  Check(!Directory.GetFiles(dir,"*.tmp",SearchOption.AllDirectories).Any()&&!File.Exists(Path.Combine(dir,".companion-export.lock")),"no leftover staging files");
  Console.WriteLine("TOTAL "+checks+" checks passed");
 }
}
