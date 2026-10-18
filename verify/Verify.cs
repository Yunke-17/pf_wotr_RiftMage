using System;
using System.Linq;
using dnlib.DotNet;

// 校验补丁目标 + 路线 C 的反射字段是否真实存在
class Verify
{
    static int missing = 0;

    static void Main()
    {
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed";
        var game = ModuleDefMD.Load(System.IO.Path.Combine(managed, "Assembly-CSharp.dll"));

        Console.WriteLine("=== MOD 程序集类型与补丁特性 ===");
        var mod = ModuleDefMD.Load(@"C:\DSH\WizardArchetype\bin\Debug\WizardArchetype.dll");
        foreach (var t in mod.GetTypes().Where(t => !t.FullName.Contains("<>") && !t.FullName.Contains("<Module")))
        {
            var hasPatch = t.CustomAttributes.Any(c => c.TypeFullName == "HarmonyLib.HarmonyPatch");
            Console.WriteLine($"  {t.FullName}{(hasPatch ? "   [HarmonyPatch]" : "")}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 补丁目标 ===");
        CheckMethod(game, "Kingmaker.UnitLogic.Abilities.MetamagicHelper", "DefaultCost", "补丁D");
        CheckMethod(game, "Kingmaker.RuleSystem.Rules.RuleCollectMetamagic", "AddMetamagic", "补丁E");
        CheckMethod(game, "Kingmaker.Blueprints.JsonSystem.BlueprintsCache", "Init", "蓝图注册");
        CheckMethod(game, "Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.Metamagic.SpellbookMetamagicSelectorVM", "GetCost", "补丁D-UI");

        Console.WriteLine();
        Console.WriteLine("=== 路线C 反射字段存在性 ===");
        CheckField(game, "Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig", "m_BaseValueType");
        CheckField(game, "Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig", "m_Progression");
        CheckField(game, "Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig", "m_StepLevel");
        CheckField(game, "Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig", "m_UseMin");
        CheckField(game, "Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig", "m_Min");
        CheckField(game, "Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig", "m_UseMax");
        CheckField(game, "Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig", "m_Max");
        CheckField(game, "Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig", "m_Class");
        CheckField(game, "Kingmaker.EntitySystem.Properties.Getters.StatGetter", "m_Stat");
        CheckField(game, "Kingmaker.EntitySystem.Properties.Getters.StatGetter", "m_StatValueType");
        CheckField(game, "Kingmaker.EntitySystem.Properties.Getters.StatGetter", "m_SpecificModifier");
        CheckField(game, "Kingmaker.Blueprints.Facts.BlueprintUnitFact", "m_DisplayName");
        CheckField(game, "Kingmaker.Blueprints.Facts.BlueprintUnitFact", "m_Description");

        Console.WriteLine();
        Console.WriteLine("=== 能力X 依赖的类型与字段 ===");
        // 合格判定用到的属性链：spell.Spellbook.Blueprint.CharacterClass
        CheckType(game, "Kingmaker.UnitLogic.Abilities.AbilityData", "AbilityData");
        CheckType(game, "Kingmaker.Blueprints.Classes.Spells.BlueprintSpellbook", "BlueprintSpellbook");
        CheckProperty(game, "Kingmaker.UnitLogic.Abilities.AbilityData", "Spellbook", "AbilityData.Spellbook");
        CheckProperty(game, "Kingmaker.UnitLogic.Abilities.AbilityData", "SpellLevel", "AbilityData.SpellLevel");
        CheckProperty(game, "Kingmaker.Blueprints.Classes.Spells.BlueprintSpellbook", "CharacterClass", "BlueprintSpellbook.CharacterClass");
        // 资源上限 / 组件字段
        CheckField(game, "Kingmaker.Blueprints.BlueprintAbilityResource", "m_MaxAmount");
        CheckField(game, "Kingmaker.UnitLogic.Abilities.Components.CasterCheckers.AbilityCasterHasNoFacts", "m_Facts");
        CheckField(game, "Kingmaker.UnitLogic.Mechanics.Actions.ContextActionApplyBuff", "m_Buff");
        // Buff 移除路径依赖（缺了会 NRE）
        CheckField(game, "Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff", "FxOnRemove");

        Console.WriteLine();
        Console.WriteLine("=== 嵌套枚举 StatGetter.ValueType ===");
        var sg = game.Find("Kingmaker.EntitySystem.Properties.Getters.StatGetter", false);
        var nested = sg?.NestedTypes.FirstOrDefault(t => t.Name == "ValueType");
        if (nested != null)
        {
            Console.WriteLine($"  [OK] ValueType 存在: {string.Join(", ", nested.Fields.Where(f => f.IsLiteral).Select(f => f.Name))}");
        }
        else
        {
            Console.WriteLine("  [缺失] ValueType 嵌套枚举");
            missing++;
        }

        Console.WriteLine();
        Console.WriteLine(missing == 0 ? "=== 全部校验通过 ===" : $"=== 有 {missing} 项缺失 ===");
        Environment.Exit(missing == 0 ? 0 : 1);
    }

    static void CheckMethod(ModuleDefMD mod, string typeName, string methodName, string label)
    {
        var t = mod.Find(typeName, false);
        if (t == null) { Console.WriteLine($"  [缺失] {label}: 类型不存在"); missing++; return; }
        var ms = t.Methods.Where(m => m.Name == methodName).ToArray();
        if (ms.Length == 0) { Console.WriteLine($"  [缺失] {label}: 方法不存在"); missing++; return; }
        Console.WriteLine($"  [OK] {label}: {t.Name}.{methodName}()");
    }

    static void CheckField(ModuleDefMD mod, string typeName, string field)
    {
        var t = mod.Find(typeName, false);
        if (t == null) { Console.WriteLine($"  [缺失] 类型 {typeName}"); missing++; return; }

        // 逐级向上查基类，与 ReflectionFields.Set 的行为一致
        var cur = t;
        while (cur != null)
        {
            var f = cur.Fields.FirstOrDefault(x => x.Name == field);
            if (f != null)
            {
                string owner = cur == t ? t.Name.String : $"{t.Name.String}(继承自 {cur.Name.String})";
                Console.WriteLine($"  [OK] {owner}.{field}  ({f.FieldType.TypeName})  private={f.IsPrivate}");
                return;
            }
            cur = cur.BaseType?.ResolveTypeDef();
        }

        Console.WriteLine($"  [缺失] {t.Name}.{field}（含基类）");
        missing++;
    }

    static void CheckType(ModuleDefMD mod, string typeName, string label)
    {
        var t = mod.Find(typeName, false);
        if (t == null) { Console.WriteLine($"  [缺失] {label}: 类型不存在"); missing++; return; }
        Console.WriteLine($"  [OK] {label}");
    }

    static void CheckProperty(ModuleDefMD mod, string typeName, string propName, string label)
    {
        var t = mod.Find(typeName, false);
        if (t == null) { Console.WriteLine($"  [缺失] {label}: 类型不存在"); missing++; return; }

        // 逐级向上查基类
        var cur = t;
        while (cur != null)
        {
            var p = cur.Properties.FirstOrDefault(x => x.Name == propName);
            if (p != null)
            {
                string owner = cur == t ? t.Name.String : $"{t.Name.String}(继承自 {cur.Name.String})";
                Console.WriteLine($"  [OK] {owner}.{propName}  ({p.PropertySig.RetType.TypeName})");
                return;
            }
            cur = cur.BaseType?.ResolveTypeDef();
        }

        Console.WriteLine($"  [缺失] {t.Name}.{propName}（含基类）");
        missing++;
    }

    static void CheckNested(ModuleDefMD mod, string typeName, string nestedName)
    {
        var t = mod.Find(typeName, false);
        if (t == null) { Console.WriteLine($"  [缺失] 类型 {typeName}"); missing++; return; }
        var n = t.NestedTypes.FirstOrDefault(x => x.Name == nestedName);
        if (n == null) { Console.WriteLine($"  [缺失] {t.Name}.{nestedName}"); missing++; return; }
        Console.WriteLine($"  [OK] 嵌套 {t.Name}.{nestedName}  private={n.IsNestedPrivate}");
    }
}
