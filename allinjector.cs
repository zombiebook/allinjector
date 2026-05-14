using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ItemStatsSystem;
using UnityEngine;

namespace allinjector
{
    // Duckov 로더가 찾는 엔트리:
    //   allinjector.ModBehaviour : Duckov.Modding.ModBehaviour
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static bool ShowLog = true;

        // 한 번에 케이스 안 주사기들 사용
        private static readonly KeyCode UseAllKey = KeyCode.L;
        
        // 디버그: 인벤토리 아이템 TypeID 출력용
        private static readonly KeyCode DebugInventoryKey = KeyCode.K;

        // 주사기 수납백 TypeID
        private static readonly HashSet<int> SyringeCaseIDs = new HashSet<int>
        {
            882,
        };

        // 실제 주사기 TypeID들
        private static readonly HashSet<int> SyringeItemIDs = new HashSet<int>
        {
            137, 398, 408, 409, 438, 797, 798, 800, 856, 857, 872, 875, 1247, 1070, 1071, 1072, 
        };

        private void Awake()
        {
            Log("[allinjector] Loaded");
        }

        private void OnEnable()
        {
            Log("[allinjector] Enabled");
        }

        private void OnDisable()
        {
            Log("[allinjector] Disabled");
        }

        private void OnDestroy()
        {
            Log("[allinjector] Unloaded");
        }

        private void Update()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead)
                return;

            // 디버그: K키로 인벤토리 아이템 TypeID 출력
            if (Input.GetKeyDown(DebugInventoryKey))
            {
                DebugInventoryItems(player);
            }

            if (Input.GetKeyDown(UseAllKey))
            {
                TryUseAllInCase(player);
            }
        }

        private static void Log(string msg)
        {
            if (ShowLog)
                Debug.Log("[allinjector] " + msg);
        }

        private static void LogError(string msg)
        {
            if (ShowLog)
                Debug.LogError("[allinjector] " + msg);
        }

        /// <summary>
        /// 디버그: 인벤토리의 모든 아이템 TypeID와 이름 출력
        /// </summary>
        private static void DebugInventoryItems(CharacterMainControl player)
        {
            if (player == null)
                return;

            Inventory inventory;
            Item characterItem = player.CharacterItem;
            if (characterItem == null)
            {
                inventory = null;
            }
            else
            {
                inventory = characterItem.Inventory;
            }

            if (inventory == null || inventory.IsEmpty())
            {
                Log("인벤토리가 비어 있음");
                CharacterMainControl.Main.PopText("인벤토리 비어 있음", -1f);
                return;
            }

            Log("=== 인벤토리 아이템 목록 ===");
            int count = 0;
            foreach (Item it in inventory)
            {
                if (it != null)
                {
                    count++;
                    string itemInfo = string.Format("Item #{0}: TypeID={1}, Type={2}",
                        count, it.TypeID, it.GetType().Name);
                    Log(itemInfo);

                    // 케이스 타입인지 확인
                    Type itemType = it.GetType();
                    FieldInfo slotsField = itemType.GetField(
                        "slots",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    );

                    if (slotsField != null)
                    {
                        Log("  -> 이 아이템은 'slots' 필드를 가지고 있음 (케이스일 가능성)");
                        
                        // slots 내용 확인
                        try
                        {
                            object slotsObj = slotsField.GetValue(it);
                            if (slotsObj != null)
                            {
                                IEnumerable slotsEnum = slotsObj as IEnumerable;
                                if (slotsEnum != null)
                                {
                                    int slotIndex = 0;
                                    foreach (object slotObj in slotsEnum)
                                    {
                                        if (slotObj == null)
                                            continue;

                                        Type slotType = slotObj.GetType();
                                        FieldInfo[] sFields = slotType.GetFields(
                                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                        );

                                        foreach (FieldInfo f in sFields)
                                        {
                                            if (!typeof(Item).IsAssignableFrom(f.FieldType))
                                                continue;

                                            Item slotItem = f.GetValue(slotObj) as Item;
                                            if (slotItem != null)
                                            {
                                                Log(string.Format("    Slot[{0}]: TypeID={1}",
                                                    slotIndex, slotItem.TypeID));
                                            }
                                        }
                                        slotIndex++;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError("slots 읽기 예외: " + ex);
                        }
                    }
                }
            }

            string msg = "인벤토리 아이템 " + count + "개 (로그 확인)";
            CharacterMainControl.Main.PopText(msg, -1f);
            Log("=== 총 " + count + "개 아이템 ===");
        }

        /// <summary>
        /// 인벤토리에서 케이스(882)를 찾고,
        /// 그 케이스의 slots 안에 들어있는 주사기들을 전부
        ///  - 실제 Use 계열 메서드 호출(효과 발동 시도)
        ///  - 스택이 1이면 슬롯 비우기, 2 이상이면 StackCount만 1 감소
        /// </summary>
        private static bool TryUseAllInCase(CharacterMainControl player)
        {
            if (player == null)
                return false;

            // 플레이어 기본 인벤(가방) 얻기
            Inventory inventory;
            Item characterItem = player.CharacterItem;
            if (characterItem == null)
            {
                inventory = null;
            }
            else
            {
                inventory = characterItem.Inventory;
            }

            if (inventory == null || inventory.IsEmpty())
            {
                CharacterMainControl.Main.PopText("인벤토리 없음", -1f);
                Log("플레이어 인벤토리가 비어 있음");
                return false;
            }

            // 1) 인벤에서 케이스(882) 하나 찾기
            Item syringeCase = null;
            foreach (Item it in inventory)
            {
                if (it != null && SyringeCaseIDs.Contains(it.TypeID))
                {
                    syringeCase = it;
                    Log("케이스 발견: TypeID=" + it.TypeID);
                    break;
                }
            }

            if (syringeCase == null)
            {
                // 케이스를 못 찾았을 때 더 자세한 정보 출력
                Log("케이스(882)를 찾지 못함. 인벤토리 아이템 TypeID 목록:");
                foreach (Item it in inventory)
                {
                    if (it != null)
                    {
                        Log("  - TypeID=" + it.TypeID);
                    }
                }
                CharacterMainControl.Main.PopText("주사기 수납백 없음 (K키로 디버그)", -1f);
                return false;
            }

            // 2) 케이스의 slots 필드 가져오기
            Type caseType = syringeCase.GetType();
            FieldInfo slotsField = caseType.GetField(
                "slots",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            if (slotsField == null)
            {
                CharacterMainControl.Main.PopText("슬롯 정보 없음", -1f);
                Log("케이스에 'slots' 필드를 찾지 못함");
                return false;
            }

            object slotsObj;
            try
            {
                slotsObj = slotsField.GetValue(syringeCase);
            }
            catch (Exception ex)
            {
                LogError("slots 필드 읽기 예외: " + ex);
                return false;
            }

            if (slotsObj == null)
            {
                CharacterMainControl.Main.PopText("케이스 슬롯 비어 있음", -1f);
                Log("케이스 slots == null");
                return false;
            }

            IEnumerable slotsEnum = slotsObj as IEnumerable;
            if (slotsEnum == null)
            {
                CharacterMainControl.Main.PopText("슬롯 구조 인식 실패", -1f);
                Log("slots 필드가 IEnumerable이 아님: " + slotsObj.GetType().FullName);
                return false;
            }

            // 3) 각 Slot 안의 Item 필드를 찾아서, 주사기만 모은다
            List<Item> foundItems = new List<Item>();
            List<object> foundSlots = new List<object>();
            List<FieldInfo> foundItemFields = new List<FieldInfo>();

            foreach (object slotObj in slotsEnum)
            {
                if (slotObj == null)
                    continue;

                Type slotType = slotObj.GetType();
                FieldInfo[] sFields = slotType.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                Item foundInThisSlot = null;
                FieldInfo itemFieldInThisSlot = null;

                for (int i = 0; i < sFields.Length; i++)
                {
                    FieldInfo f = sFields[i];
                    if (!typeof(Item).IsAssignableFrom(f.FieldType))
                        continue;

                    Item slotItem = null;
                    try
                    {
                        slotItem = f.GetValue(slotObj) as Item;
                    }
                    catch (Exception ex)
                    {
                        LogError("Slot 필드 읽기 예외: " + f.Name + " - " + ex);
                    }

                    if (slotItem == null)
                        continue;

                    // 주사기 ID만 대상
                    if (!SyringeItemIDs.Contains(slotItem.TypeID))
                        continue;

                    foundInThisSlot = slotItem;
                    itemFieldInThisSlot = f;
                    break; // 한 슬롯당 하나만 취급
                }

                if (foundInThisSlot != null && itemFieldInThisSlot != null)
                {
                    foundItems.Add(foundInThisSlot);
                    foundSlots.Add(slotObj);
                    foundItemFields.Add(itemFieldInThisSlot);
                }
            }

            if (foundItems.Count == 0)
            {
                CharacterMainControl.Main.PopText("수납백 안에 주사기 없음", -1f);
                Log("slots 안에서 SyringeItemIDs에 해당하는 Item을 찾지 못함");
                return false;
            }

            // 4) 찾은 주사기들 전부 Use 호출 후, 스택/슬롯 처리
            int usedStacks = 0;
            for (int i = 0; i < foundItems.Count; i++)
            {
                Item syringe = foundItems[i];
                object slotObj = foundSlots[i];
                FieldInfo itemField = foundItemFields[i];

                // 효과 발동 시도
                bool effectOk = TryInvokeSyringeUseMethod(player, syringe);

                // 스택 처리: StackCount > 1 이면 1만 줄이고, 아니면 슬롯 비우기
                try
                {
                    int stackCount = syringe.StackCount;
                    bool stackable = syringe.Stackable;

                    if (stackable && stackCount > 1)
                    {
                        syringe.StackCount = stackCount - 1;
                        Log("syringe TypeID=" + syringe.TypeID +
                            " stack " + stackCount + " -> " + syringe.StackCount +
                            " effectOk=" + effectOk);
                    }
                    else
                    {
                        itemField.SetValue(slotObj, null);
                        Log("syringe TypeID=" + syringe.TypeID +
                            " consumed (slot cleared), effectOk=" + effectOk);
                    }
                }
                catch (Exception ex)
                {
                    LogError("스택/슬롯 처리 예외: " + ex);
                }

                usedStacks++;
            }

            string msg = "수납백에서 주사기 " + usedStacks + "개 사용";
            CharacterMainControl.Main.PopText(msg, -1f);
            Log(msg);

            return usedStacks > 0;
        }

        /// <summary>
        /// 주사기 아이템을 직접 사용 (Use 메서드 호출)
        /// </summary>
        private static bool TryInvokeSyringeUseMethod(CharacterMainControl player, Item syringe)
        {
            if (syringe == null)
                return false;

            Log("주사기 사용 시도: TypeID=" + syringe.TypeID);

            try
            {
                Type syringeType = syringe.GetType();
                Log("주사기 타입: " + syringeType.FullName);
                
                // 모든 메서드 출력 (디버깅)
                Log("=== 사용 가능한 모든 메서드 ===");
                MethodInfo[] allMethods = syringeType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (MethodInfo m in allMethods)
                {
                    string paramStr = string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name + " " + p.Name));
                    Log("  " + m.ReturnType.Name + " " + m.Name + "(" + paramStr + ")");
                }

                // 1. Use() 메서드 찾기 - 모든 오버로드 시도
                MethodInfo[] useMethods = syringeType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "Use").ToArray();

                Log("Use 메서드 " + useMethods.Length + "개 발견");

                foreach (MethodInfo useMethod in useMethods)
                {
                    ParameterInfo[] parameters = useMethod.GetParameters();
                    string paramStr = string.Join(", ", Array.ConvertAll(parameters, p => p.ParameterType.Name));
                    Log("시도: Use(" + paramStr + ")");

                    try
                    {
                        if (parameters.Length == 0)
                        {
                            useMethod.Invoke(syringe, null);
                            Log("✓ 주사기 사용 성공 (Use): TypeID=" + syringe.TypeID);
                            return true;
                        }
                        else if (parameters.Length == 1)
                        {
                            // CharacterMainControl 타입 확인
                            if (typeof(CharacterMainControl).IsAssignableFrom(parameters[0].ParameterType))
                            {
                                useMethod.Invoke(syringe, new object[] { player });
                                Log("✓ 주사기 사용 성공 (Use with player): TypeID=" + syringe.TypeID);
                                return true;
                            }
                            // Health 타입 확인
                            else if (parameters[0].ParameterType.Name == "Health" || parameters[0].ParameterType.Name.Contains("Health"))
                            {
                                useMethod.Invoke(syringe, new object[] { player.Health });
                                Log("✓ 주사기 사용 성공 (Use with Health): TypeID=" + syringe.TypeID);
                                return true;
                            }
                            // bool 타입 확인
                            else if (parameters[0].ParameterType == typeof(bool))
                            {
                                useMethod.Invoke(syringe, new object[] { true });
                                Log("✓ 주사기 사용 성공 (Use with bool): TypeID=" + syringe.TypeID);
                                return true;
                            }
                            // Object 타입 확인 (player 전달)
                            else if (parameters[0].ParameterType == typeof(object))
                            {
                                useMethod.Invoke(syringe, new object[] { player });
                                Log("✓ 주사기 사용 성공 (Use with object): TypeID=" + syringe.TypeID);
                                return true;
                            }
                        }
                        else if (parameters.Length == 2)
                        {
                            // Use(Object, Boolean) 형태 처리
                            if (parameters[0].ParameterType == typeof(object) && parameters[1].ParameterType == typeof(bool))
                            {
                                useMethod.Invoke(syringe, new object[] { player, true });
                                Log("✓ 주사기 사용 성공 (Use with object, bool): TypeID=" + syringe.TypeID);
                                return true;
                            }
                            // Use(CharacterMainControl, Boolean) 형태 처리
                            else if (typeof(CharacterMainControl).IsAssignableFrom(parameters[0].ParameterType) && parameters[1].ParameterType == typeof(bool))
                            {
                                useMethod.Invoke(syringe, new object[] { player, true });
                                Log("✓ 주사기 사용 성공 (Use with player, bool): TypeID=" + syringe.TypeID);
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError("Use 메서드 호출 실패: " + ex.Message);
                        if (ex.InnerException != null)
                        {
                            LogError("  내부 예외: " + ex.InnerException.Message);
                        }
                    }
                }

                // 2. OnUse 메서드 시도
                MethodInfo[] onUseMethods = syringeType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "OnUse").ToArray();

                foreach (MethodInfo onUseMethod in onUseMethods)
                {
                    try
                    {
                        ParameterInfo[] parameters = onUseMethod.GetParameters();
                        if (parameters.Length == 0)
                        {
                            onUseMethod.Invoke(syringe, null);
                            Log("✓ 주사기 사용 성공 (OnUse): TypeID=" + syringe.TypeID);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError("OnUse 메서드 호출 실패: " + ex.Message);
                    }
                }

                // 3. Apply 계열 메서드 시도
                string[] applyNames = new string[] { "Apply", "ApplyEffect", "ApplyEffects", "Activate" };
                foreach (string methodName in applyNames)
                {
                    MethodInfo[] applyMethods = syringeType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(m => m.Name == methodName).ToArray();

                    foreach (MethodInfo applyMethod in applyMethods)
                    {
                        try
                        {
                            ParameterInfo[] parameters = applyMethod.GetParameters();
                            if (parameters.Length == 0)
                            {
                                applyMethod.Invoke(syringe, null);
                                Log("✓ 주사기 사용 성공 (" + methodName + "): TypeID=" + syringe.TypeID);
                                return true;
                            }
                            else if (parameters.Length == 1 && typeof(CharacterMainControl).IsAssignableFrom(parameters[0].ParameterType))
                            {
                                applyMethod.Invoke(syringe, new object[] { player });
                                Log("✓ 주사기 사용 성공 (" + methodName + " with player): TypeID=" + syringe.TypeID);
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError(methodName + " 메서드 호출 실패: " + ex.Message);
                        }
                    }
                }

                LogError("✗ 사용 가능한 메서드를 찾을 수 없음");
                return false;
            }
            catch (Exception ex)
            {
                LogError("주사기 사용 중 예외: " + ex.GetType().Name + " - " + ex.Message);
                LogError("스택 트레이스: " + ex.StackTrace);
                if (ex.InnerException != null)
                {
                    LogError("내부 예외: " + ex.InnerException.Message);
                }
                return false;
            }
        }


    }
}




