using System;
using System.Linq;
using dnlib.DotNet;

class ProbeCfg
{
    static void Main()
    {
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed";
        var ui = ModuleDefMD.Load(System.IO.Path.Combine(managed, "Owlcat.Runtime.UI.dll"));

        var t = ui.Find("Owlcat.Runtime.UI.Tooltips.TooltipConfig", false);
        if (t == null)
        {
            Console.WriteLine("[缺失] TooltipConfig");
            return;
        }
        Console.WriteLine("类型: " + t.FullName + "  IsValueType=" + t.IsValueType + "  IsSealed=" + t.IsSealed);
        Console.WriteLine();
        Console.WriteLine("=== 字段 ===");
        foreach (var f in t.Fields)
            Console.WriteLine($"  {f.Name}  : {f.FieldType.TypeName}  public={f.IsPublic} static={f.IsStatic}");

        Console.WriteLine();
        Console.WriteLine("=== 属性 ===");
        foreach (var p in t.Properties)
            Console.WriteLine($"  {p.Name}  : {p.PropertySig.RetType.TypeName}  setter={(p.SetMethod != null)}  setPublic={(p.SetMethod != null && p.SetMethod.IsPublic)}");

        Console.WriteLine();
        Console.WriteLine("=== 方法 ===");
        foreach (var m in t.Methods)
            Console.WriteLine($"  {m.Name}({string.Join(", ", m.Parameters.Select(x => x.Type.TypeName))})  public={m.IsPublic}");
    }
}
