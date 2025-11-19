using System;
using System.Collections;
using System.Collections.Generic;
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
                    break;
                }
            }

            if (syringeCase == null)
            {
                CharacterMainControl.Main.PopText("주사기 수납백 없음", -1f);
                Log("케이스(882)를 찾지 못함");
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
        /// 주사기 아이템에서 "사용" 계열 메서드를 Reflection으로 찾아 호출
        /// </summary>
        private static bool TryInvokeSyringeUseMethod(CharacterMainControl player, Item syringe)
        {
            if (syringe == null)
                return false;

            Type itemType = syringe.GetType();
            MethodInfo chosen;
            object[] args;

            // 1) 매개변수 없는 Use()
            chosen = itemType.GetMethod(
                "Use",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null
            );
            if (chosen != null)
            {
                args = null;
                return InvokeSafely(syringe, chosen, args);
            }

            // 2) (CharacterMainControl) 한 개 받는 메서드 찾기
            string[] candidateNames = new string[]
            {
                "Use",
                "UseItem",
                "Apply",
                "ApplyEffect",
                "OnUse"
            };

            MethodInfo[] methods = itemType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (!NameMatchesAny(m.Name, candidateNames))
                    continue;

                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 1)
                {
                    Type pType = ps[0].ParameterType;
                    if (pType.IsAssignableFrom(typeof(CharacterMainControl)))
                    {
                        args = new object[] { player };
                        return InvokeSafely(syringe, m, args);
                    }
                }
                else if (ps.Length == 0)
                {
                    args = null;
                    return InvokeSafely(syringe, m, args);
                }
            }

            Log("주사기 Type=" + itemType.Name + " 에서 Use/UseItem 메서드를 찾지 못함");
            return false;
        }

        private static bool NameMatchesAny(string name, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(name, candidates[i], StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool InvokeSafely(object target, MethodInfo method, object[] args)
        {
            if (target == null || method == null)
                return false;

            try
            {
                method.Invoke(target, args);
                return true;
            }
            catch (Exception ex)
            {
                LogError("메서드 호출 중 예외: " + ex);
                return false;
            }
        }
    }
}




