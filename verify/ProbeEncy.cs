using System;
using System.Linq;
using dnlib.DotNet;

class ProbeEncy
{
    static void Main()
    {
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed";
        var game = ModuleDefMD.Load(System.IO.Path.Combine(managed, "Assembly-CSharp.dll"));

        foreach (var name in new[]
        {
            "Kingmaker.Blueprints.SimpleBlueprint",
            "Kingmaker.Blueprints.BlueprintScriptableObject",
            "Kingmaker.Blueprints.Encyclopedia.BlueprintEncyclopediaNode",
            "Kingmaker.Blueprints.Encyclopedia.BlueprintEncyclopediaPage",
            "Kingmaker.Blueprints.Encyclopedia.Blocks.BlueprintEncyclopediaBlock",
            "Kingmaker.Blueprints.Encyclopedia.Blocks.BlueprintEncyclopediaBlockText",
        })
        {
            var t = game.Find(name, false);
            if (t == null) { Console.WriteLine($"[缺失] {name}"); continue; }

            Console.WriteLine($"=== {t.Name} ===");
            Console.WriteLine($"  基类: {t.BaseType?.TypeName}");
            Console.WriteLine($"  是值类型={t.IsValueType}  sealed={t.IsSealed}  abstract={t.IsAbstract}");

            var ctors = t.Methods.Where(m => m.IsConstructor).ToArray();
            Console.WriteLine($"  构造器数: {ctors.Length}");
            foreach (var c in ctors)
                Console.WriteLine($"    .ctor({string.Join(", ", c.Parameters.Select(p => p.Type.TypeName))})  public={c.IsPublic}");

            // 是否继承自 UnityEngine.ScriptableObject
            var cur = t;
            bool isSO = false;
            while (cur != null)
            {
                if (cur.FullName == "UnityEngine.ScriptableObject") { isSO = true; break; }
                cur = cur.BaseType?.ResolveTypeDef();
            }
            Console.WriteLine($"  继承 ScriptableObject: {isSO}");
            Console.WriteLine();
        }
    }
}
