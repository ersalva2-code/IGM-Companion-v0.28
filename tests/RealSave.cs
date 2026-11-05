using System;using System.Linq;using IGM;
class RealSave {static void Main(string[] a){var p=Data.Import(a[0],"");if(p.Systems.Count==0)throw new Exception("No systems imported");Console.WriteLine("PASS real decoded Xbox save: "+p.Systems.Count+" systems, "+p.Alliances.Count+" alliances, "+p.Systems.Count(x=>x.Owned)+" owned stations; skipped "+p.Skipped);}}
