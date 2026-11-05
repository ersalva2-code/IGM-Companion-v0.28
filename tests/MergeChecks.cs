using System;using System.IO;using System.Linq;using IGM;
class MergeChecks {
 static int tests;
 static void Check(bool b,string name){if(!b)throw new Exception(name);tests++;Console.WriteLine("PASS "+name);}
 static Project ProjectWith(string ua,string id,string name){var p=new Project();p.GetAlliance(id).Name=name;p.Systems.Add(new SystemRecord{UA=ua,Alliance=id,Name=name});return p;}
 static void Main(string[] args){
  var p=ProjectWith("0000010000000001","1_2_3_4","Save name");var local=ProjectWith("0000010000000001","1_2_3_4","Local name");local.Alliances[0].ColorRGB=123;local.Alliances[0].Icon="IGMICON2 0 96\n";
  local.Systems.Add(new SystemRecord{UA="0000020000000002",Alliance="5_6_7_8"});local.GetAlliance("5_6_7_8").Name="Local only";
  Check(p.MergeLocal(local)==1&&p.Alliances.Count==2&&p.Systems.Count==2,"local-only alliance and system included");
  Check(p.Systems[0].Name=="Local name"&&p.Alliances[0].Name=="Local name","existing local names retained");
  Check(p.Alliances[0].ColorRGB==123&&p.Alliances[0].Icon.Length>0,"local icon and colour retained");
  Check(!p.Systems[1].Selected&&p.Systems[0].Selected,"local-only records need no re-export; save selections retained");
  Check(p.MergeLocal(local)==0&&p.Systems.Count==2,"repeated merge idempotent");
  p.Alliances[0].Name="User edit";p.Alliances[0].NameEdited=true;p.MergeLocal(local);Check(p.Alliances[0].Name=="User edit","in-session name customisation preserved");
  var conflict=ProjectWith("0000010000000001","9_9_9_9","Conflicting");p.MergeLocal(conflict);Check(p.Conflicts==1&&!p.Systems[0].Selected&&p.Systems[0].Alliance=="1_2_3_4","conflicting assignment remains unselected and unreassigned");
  p.MergeLocal(conflict);Check(p.Conflicts==1,"conflict count is idempotent");
  p.Owner="owner";p.Systems[0].Owned=true;p.Systems[0].Owner="owner";p.Systems[0].Alliance="";Check(Data.AssignOwned(p,"1_2_3_4")==0,"profile assignment cannot resolve a conflict silently");
  var a=typeof(Data).Assembly;Check(!a.GetTypes().SelectMany(t=>t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance)).Any(m=>(m.Attributes&System.Reflection.MethodAttributes.PinvokeImpl)!=0),"no unmanaged process/file access imports in Companion assembly");
  if(args.Length==2){
   var original=File.ReadAllBytes(args[1]);string folder=Path.Combine(Path.GetTempPath(),"igm028-merge-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   try{File.WriteAllBytes(Path.Combine(folder,"catalogo_observado.jsonl"),original);var catalogue=Data.LoadMod(folder);Check(catalogue.Alliances.Count==9,"supplied catalogue contains nine alliances");var imported=Data.Import(args[0],"");Check(imported.Alliances.Count==6,"supplied save contains six alliances");int added=imported.MergeLocal(catalogue);Check(added==5&&imported.Alliances.Count==11,"real save+catalogue union has eleven alliances");Check(imported.Systems.Select(x=>x.Key).Distinct().Count()==imported.Systems.Count,"no duplicate systems in real merge");Check(File.ReadAllBytes(args[1]).SequenceEqual(original),"original uploaded catalogue remains unchanged");Console.WriteLine("REAL MERGE: "+imported.Systems.Count+" systems; "+imported.Alliances.Count+" alliances; "+imported.Conflicts+" conflicts");}
   finally{Directory.Delete(folder,true);}
  }
  Console.WriteLine("TOTAL "+tests+" merge/audit checks passed");
 }
}
