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
        Console.WriteLine("=== 能力X 第1步：作用域组件依赖 ===");
        // RuleCalculateAbilityParams：arm 阶段要读 AbilityData / Spell
        CheckType(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCalculateAbilityParams", "RuleCalculateAbilityParams");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCalculateAbilityParams", "AbilityData", "RCP.AbilityData");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCalculateAbilityParams", "Spell", "RCP.Spell");
        // RuleCastSpell：consume 阶段要读 Spell / Success / IsUMDFailed
        CheckType(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCastSpell", "RuleCastSpell");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCastSpell", "Spell", "RCS.Spell");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCastSpell", "Success", "RCS.Success");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCastSpell", "IsUMDFailed", "RCS.IsUMDFailed");
        // 组件基类：UnitBuffComponentDelegate<TData>
        CheckType(game, "Kingmaker.UnitLogic.Buffs.Components.UnitBuffComponentDelegate`1", "UnitBuffComponentDelegate<T>");
        // Data 序列化载体：m_Data 在 **嵌套类 ComponentRuntime** 上（不是外层类），
        // 且标了 [JsonProperty]（EntityFactComponentDelegate.cs:570）—— 这决定 Data 会被存档序列化，
        // 也是「记住的法术」必须 [JsonIgnore] 的原因。
        CheckField(game, "Kingmaker.EntitySystem.EntityFactComponentDelegate`2/ComponentRuntime", "m_Data");
        // AbilityData 内容相等性
        CheckMethod(game, "Kingmaker.UnitLogic.Abilities.AbilityData", "Equals", "AbilityData.Equals");
        CheckMethod(game, "Kingmaker.UnitLogic.Abilities.AbilityData", "GetHashCode", "AbilityData.GetHashCode");
        // 日志与后续档位都用
        CheckProperty(game, "Kingmaker.UnitLogic.Spellbook", "CasterLevel", "Spellbook.CasterLevel");

        Console.WriteLine();
        Console.WriteLine("=== 能力X 第2步：无视 SR ===");
        // RuleCastSpell.Context 是只读**字段**（构造时创建），原版范式靠它置位
        CheckField(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCastSpell", "Context");
        // ForceIgnoreSpellResist 是 AbilityExecutionContext 的可写属性
        CheckProperty(game, "Kingmaker.UnitLogic.Abilities.AbilityExecutionContext", "ForceIgnoreSpellResist",
            "AbilityExecutionContext.ForceIgnoreSpellResist");
        // SR 判定链读取 SourceAbilityContext（MechanicsContext.Root as AbilityExecutionContext）
        CheckProperty(game, "Kingmaker.UnitLogic.Mechanics.MechanicsContext", "SourceAbilityContext",
            "MechanicsContext.SourceAbilityContext");
        // SR 检定规则本身
        CheckType(game, "Kingmaker.RuleSystem.Rules.RuleSpellResistanceCheck", "RuleSpellResistanceCheck");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.RuleSpellResistanceCheck", "IsSpellResisted",
            "RuleSpellResistanceCheck.IsSpellResisted");
        // 时序依据：AbilityExecutionProcess 是协程，SR 在后续帧的 Tick 里跑
        CheckMethod(game, "Kingmaker.Controllers.AbilityExecutionProcess", "Tick", "AbilityExecutionProcess.Tick");

        Console.WriteLine();
        Console.WriteLine("=== 能力X 第2步补：SR 战斗日志抑制（补丁F）===");
        // 全程序集唯一的 SR 日志产生点（LogThreadService.cs:139 注册）
        CheckType(game,
            "Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Combat.RulebookSpellResistanceCheckLogThread",
            "RulebookSpellResistanceCheckLogThread");
        // HandleEvent 有两个重载，必须按签名取
        CheckMethodWithParams(game,
            "Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Combat.RulebookSpellResistanceCheckLogThread",
            "HandleEvent",
            new[] { "Kingmaker.RuleSystem.Rules.RuleSpellResistanceCheck" },
            "SR日志 HandleEvent(RuleSpellResistanceCheck)");
        // 抑制判断依赖 Context 链
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.RuleSpellResistanceCheck", "Context",
            "RuleSpellResistanceCheck.Context");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.RuleSpellResistanceCheck", "HasResistanceRoll",
            "RuleSpellResistanceCheck.HasResistanceRoll");

        Console.WriteLine();
        Console.WriteLine("=== 能力X 第2步补2：引擎静默通过钩子 ===");
        // RuleSpellResistanceCheck.IgnoreSpellResistance：OnTrigger 开头检查，置 true 则整段跳过
        // （不计算 SpellResistance、不掷骰 ⇒ HasResistanceRoll=false ⇒ 日志线程提前返回）
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.RuleSpellResistanceCheck", "IgnoreSpellResistance",
            "RuleSpellResistanceCheck.IgnoreSpellResistance");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.RuleSpellResistanceCheck", "TargetIsImmune",
            "RuleSpellResistanceCheck.TargetIsImmune");
        // 常驻订阅者用的基类（特性级组件）
        CheckType(game, "Kingmaker.UnitLogic.UnitFactComponentDelegate", "UnitFactComponentDelegate");

        Console.WriteLine();
        Console.WriteLine("=== 能力X 第3步：加骰面 ===");
        CheckType(game, "Kingmaker.RuleSystem.Rules.Damage.RuleCalculateDamage", "RuleCalculateDamage");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.Damage.RuleCalculateDamage", "DamageBundle",
            "RuleCalculateDamage.DamageBundle");
        // 骰式修改的载体
        CheckType(game, "Kingmaker.RuleSystem.Rules.ModifiableDiceFormula", "ModifiableDiceFormula");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.ModifiableDiceFormula", "ModifiedValue",
            "ModifiableDiceFormula.ModifiedValue");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.ModifiableDiceFormula", "Modifications",
            "ModifiableDiceFormula.Modifications");
        CheckMethodWithParams(game, "Kingmaker.RuleSystem.Rules.ModifiableDiceFormula", "Modify",
            new[] { "Kingmaker.RuleSystem.DiceFormula", "Kingmaker.EntitySystem.EntityFact" },
            "ModifiableDiceFormula.Modify(DiceFormula, EntityFact)");
        // BaseDamage.Dice
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.Damage.BaseDamage", "Dice", "BaseDamage.Dice");
        // DiceFormula 结构
        CheckType(game, "Kingmaker.RuleSystem.DiceFormula", "DiceFormula");
        CheckProperty(game, "Kingmaker.RuleSystem.DiceFormula", "Rolls", "DiceFormula.Rolls");
        CheckProperty(game, "Kingmaker.RuleSystem.DiceFormula", "Dice", "DiceFormula.Dice");
        // DiceFormulaModifier.Source（幂等保护依赖它）
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.DiceFormulaModifier", "Source", "DiceFormulaModifier.Source");

        Console.WriteLine();
        Console.WriteLine("=== 能力X 第4步：豁免多骰取低 ===");
        CheckType(game, "IWasRoll", "IWasRoll（全局订阅接口）");
        CheckMethodWithParams(game, "IWasRoll", "WasRoll",
            new[] { "Kingmaker.RuleSystem.RulebookEvent", "Kingmaker.RuleSystem.Rules.RuleRollD20" },
            "IWasRoll.WasRoll(RulebookEvent, RuleRollD20)");
        CheckType(game, "Kingmaker.RuleSystem.Rules.RuleSavingThrow", "RuleSavingThrow");
        // Reroll(fact, takeBest) 在父类 RuleRollDice 上（不是 RuleRollD20）
        CheckMethodWithParams(game, "Kingmaker.RuleSystem.Rules.RuleRollDice", "Reroll",
            new[] { "Kingmaker.EntitySystem.EntityFact", "System.Boolean" },
            "RuleRollDice.Reroll(EntityFact, bool)");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.RuleRollDice", "RollHistory", "RuleRollDice.RollHistory");
        // 过滤用：RuleReason.Context
        CheckProperty(game, "Kingmaker.RuleSystem.RuleReason", "Context", "RuleReason.Context");

        Console.WriteLine();
        Console.WriteLine("=== 阶段4 预览：特殊恍惚 + 免疫旁路（补丁G）===");
        // 条件存储（纯计数，无来源）
        CheckMethodWithParams(game, "Kingmaker.UnitLogic.UnitState", "HasCondition",
            new[] { "Kingmaker.UnitLogic.UnitCondition" }, "UnitState.HasCondition(UnitCondition)");
        CheckField(game, "Kingmaker.UnitLogic.UnitState", "m_Conditions");
        CheckField(game, "Kingmaker.UnitLogic.UnitState", "m_ConditionsImmunity");
        CheckProperty(game, "Kingmaker.UnitLogic.UnitState", "Owner", "UnitState.Owner");
        // 施加条件的组件
        CheckType(game, "Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition", "BuffStatusCondition");
        CheckField(game, "Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition", "Condition");
        // 战斗结束清除
        CheckType(game, "Kingmaker.Designers.Mechanics.Facts.CombatStateTrigger", "CombatStateTrigger");
        CheckType(game, "Kingmaker.UnitLogic.Mechanics.Actions.ContextActionRemoveSelf", "ContextActionRemoveSelf");
        // 施加 Buff
        CheckMethodWithParams(game, "Kingmaker.Designers.GameHelper", "ApplyBuff",
            new[] { "Kingmaker.EntitySystem.Entities.UnitEntityData", "Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff",
                    "System.Nullable`1" }, "GameHelper.ApplyBuff(UnitEntityData, BlueprintBuff, Rounds?)");
        // UnitCondition.Staggered 必须在枚举里
        CheckEnumMember(game, "Kingmaker.UnitLogic.UnitCondition", "Staggered");

        Console.WriteLine();
        Console.WriteLine("=== 阶段4：战斗开场二选一 + 特殊纠缠 ===");
        CheckType(game, "Kingmaker.PubSubSystem.IUnitCombatHandler", "IUnitCombatHandler");
        CheckMethodWithParams(game, "Kingmaker.PubSubSystem.IUnitCombatHandler", "HandleUnitJoinCombat",
            new[] { "Kingmaker.EntitySystem.Entities.UnitEntityData" }, "IUnitCombatHandler.HandleUnitJoinCombat");
        // 最终先攻值
        CheckProperty(game, "Kingmaker.Controllers.Combat.UnitCombatState", "Initiative", "UnitCombatState.Initiative");
        // 强制专注：CustomDC 可写，OnTrigger 读取
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCheckConcentration", "CustomDC",
            "RuleCheckConcentration.CustomDC");
        CheckProperty(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCheckConcentration", "Success",
            "RuleCheckConcentration.Success");
        // 移除 Buff
        CheckMethodWithParams(game, "Kingmaker.Designers.GameHelper", "RemoveBuff",
            new[] { "Kingmaker.EntitySystem.Entities.UnitEntityData", "Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff" },
            "GameHelper.RemoveBuff(UnitEntityData, BlueprintBuff)");

        Console.WriteLine();
        Console.WriteLine("=== 阶段4：专注失败的体质伤害（C13/C14）===");
        CheckType(game, "Kingmaker.RuleSystem.Rules.RuleDealStatDamage", "RuleDealStatDamage");
        CheckMethodWithParams(game, "Kingmaker.RuleSystem.Rules.RuleDealStatDamage", ".ctor",
            new[] { "Kingmaker.EntitySystem.Entities.UnitEntityData", "Kingmaker.EntitySystem.Entities.UnitEntityData",
                    "Kingmaker.EntitySystem.Stats.StatType", "Kingmaker.RuleSystem.DiceFormula", "System.Int32" },
            "RuleDealStatDamage 构造器");
        CheckEnumMember(game, "Kingmaker.EntitySystem.Stats.StatType", "Constitution");
        CheckEnumMember(game, "Kingmaker.RuleSystem.DiceType", "D2");

        Console.WriteLine();
        Console.WriteLine("=== 能力Y（§9）===");
        CheckType(game, "Kingmaker.UnitLogic.Abilities.Components.AbilityVariants", "AbilityVariants");
        CheckField(game, "Kingmaker.UnitLogic.Abilities.Components.AbilityVariants", "m_Variants");
        // CL/DC 修正 API
        CheckMethodWithParams(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCalculateAbilityParams",
            "AddBonusCasterLevel",
            new[] { "System.Int32", "Kingmaker.Enums.ModifierDescriptor" },
            "AddBonusCasterLevel(int, ModifierDescriptor)");
        CheckMethodWithParams(game, "Kingmaker.RuleSystem.Rules.Abilities.RuleCalculateAbilityParams",
            "AddBonusDC",
            new[] { "System.Int32", "Kingmaker.Enums.ModifierDescriptor" },
            "AddBonusDC(int, ModifierDescriptor)");
        // 资源退还
        CheckMethodWithParams(game, "Kingmaker.UnitLogic.UnitAbilityResourceCollection", "Restore",
            new[] { "Kingmaker.Blueprints.BlueprintScriptableObject", "System.Int32" },
            "UnitAbilityResourceCollection.Restore(blueprint, amount)");
        // 上下文动作基类
        CheckType(game, "Kingmaker.UnitLogic.Mechanics.Actions.ContextAction", "ContextAction");

        Console.WriteLine();
        Console.WriteLine("=== C15：CON < 3 禁施法（补丁I）===");
        CheckProperty(game, "Kingmaker.UnitLogic.Abilities.AbilityData", "CanBeCastByCaster",
            "AbilityData.CanBeCastByCaster");
        CheckProperty(game, "Kingmaker.UnitLogic.Abilities.AbilityData", "Caster", "AbilityData.Caster");
        CheckEnumMember(game, "Kingmaker.UnitLogic.UnitCondition", "SpellcastingForbidden");

        Console.WriteLine();
        Console.WriteLine("=== 能力Z（§10）===");
        CheckType(game, "Kingmaker.Blueprints.TurnBasedModifiers.AbilityIsFullRoundInTurnBased",
            "AbilityIsFullRoundInTurnBased");
        CheckField(game, "Kingmaker.Blueprints.TurnBasedModifiers.AbilityIsFullRoundInTurnBased",
            "FullRoundIfTurnBased");
        CheckEnumMember(game, "Kingmaker.UnitLogic.UnitCondition", "Exhausted");
        CheckEnumMember(game, "Kingmaker.UnitLogic.UnitCondition", "Fatigued");
        // 秒级时长施加：AddBuff 在 BuffCollection 上（UnitDescriptor.Buffs）
        CheckMethodWithParams(game, "Kingmaker.UnitLogic.Buffs.BuffCollection", "AddBuff",
            new[] { "Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff",
                    "Kingmaker.EntitySystem.Entities.UnitEntityData",
                    "System.Nullable`1", "Kingmaker.UnitLogic.Abilities.AbilityParams" },
            "BuffCollection.AddBuff(buff, caster, TimeSpan?, AbilityParams)");

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

    /// <summary>
    /// 按参数类型精确校验重载。同名方法有多个时必须用它，否则会误判「存在」。
    /// </summary>
    static void CheckMethodWithParams(ModuleDefMD mod, string typeName, string methodName,
        string[] paramTypes, string label)
    {
        var t = mod.Find(typeName, false);
        if (t == null) { Console.WriteLine($"  [缺失] {label}: 类型不存在"); missing++; return; }

        var ms = t.Methods.Where(m => m.Name == methodName).ToArray();
        if (ms.Length == 0) { Console.WriteLine($"  [缺失] {label}: 方法不存在"); missing++; return; }

        foreach (var m in ms)
        {
            // ★ dnlib 的 Parameters 对实例方法会把 this 计入第 0 个参数，
            //   而反射签名里没有 this。这里跳过它，否则永远匹配不上。
            int offset = m.IsStatic ? 0 : 1;
            var ps = m.Parameters;
            if (ps.Count - offset != paramTypes.Length) continue;

            bool all = true;
            for (int i = 0; i < paramTypes.Length; i++)
            {
                var actual = ps[i + offset].Type;
                // TypeName 只有短名（RuleSpellResistanceCheck），FullName 才是全名；
                // 泛型（Nullable`1）在 TypeName 里也不带命名空间，所以三种写法都接受。
                string want = paramTypes[i];
                string wantShort = want.Contains(".") ? want.Substring(want.LastIndexOf('.') + 1) : want;
                if (actual.TypeName != want && actual.FullName != want && actual.TypeName != wantShort)
                { all = false; break; }
            }
            if (!all) continue;

            Console.WriteLine($"  [OK] {label}: {t.Name}.{methodName}({string.Join(", ", paramTypes.Select(p => p.Split('.').Last()))})  " +
                              $"public={m.IsPublic}");
            return;
        }

        Console.WriteLine($"  [缺失] {label}: 存在 {ms.Length} 个同名重载但签名不匹配（" +
                          string.Join(" | ", ms.Select(m => m.Name + "(" + string.Join(", ", m.Parameters.Select(p => p.Type.TypeName)) + ")")) + "）");
        missing++;
    }

    /// <summary>校验枚举成员存在。</summary>
    static void CheckEnumMember(ModuleDefMD mod, string typeName, string member)
    {
        var t = mod.Find(typeName, false);
        if (t == null) { Console.WriteLine($"  [缺失] 枚举类型 {typeName}"); missing++; return; }

        var f = t.Fields.FirstOrDefault(x => x.Name == member);
        if (f == null || !f.IsLiteral)
        {
            Console.WriteLine($"  [缺失] {t.Name}.{member} 不是枚举成员");
            missing++;
            return;
        }

        Console.WriteLine($"  [OK] {t.Name}.{member} = {(f.Constant != null ? f.Constant.Value : "?")}");
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

        // 逐级向上查基类。
        // 注意：dnlib 里 Property 和 Field 是分开的元数据表。
        // 游戏里很多「看起来像属性」的成员其实是 public 字段
        // （例如 RuleCalculateAbilityParams.AbilityData、RuleCastSpell.Spell），
        // 所以两处都要查，否则会误报缺失。
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

            var f = cur.Fields.FirstOrDefault(x => x.Name == propName);
            if (f != null)
            {
                string owner = cur == t ? t.Name.String : $"{t.Name.String}(继承自 {cur.Name.String})";
                Console.WriteLine($"  [OK] {owner}.{propName}  ({f.FieldType.TypeName})  [字段]");
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
