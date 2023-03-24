using Aki.Reflection.Patching;
using CombatStances;
using Comfort.Common;
using EFT;
using EFT.Animations;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;



namespace CombatStances
{

    public class SetAimingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.Player.FirearmController).GetMethod("set_IsAiming", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.Player.FirearmController __instance, bool value, ref bool ____isAiming)
        {
            Player player = (Player)AccessTools.Field(typeof(EFT.Player.ItemHandsController), "_player").GetValue(__instance);
            if (__instance.Item.WeapClass == "pistol")
            {
                player.Physical.Aim((!____isAiming || !(player.MovementContext.StationaryWeapon == null)) ? 0f : __instance.ErgonomicWeight * 0.2f);
            }
        }
    }

    public class GetAimingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.Player.FirearmController).GetMethod("get_IsAiming", BindingFlags.Public | BindingFlags.Instance);
        }

        private static bool SetCanAds = false;
        private static bool SetActiveAimADS = false;
        private static bool SetRunAnim = false;
        private static bool ResetRunAnim = false;

        private static bool checkProtruding(ProtrudableComponent x)
        {
            return x.IsProtruding();
        }

        private static bool IsAllowedADSWithFS(Weapon weapon, Player.FirearmController fc) 
        {
            if (weapon.WeapClass == "pistol" && (float)weapon.CalculateCellSize().X < 3 && !fc.IsSilenced) 
            {
                return true;
            }
            if (weapon.Folded == true)
            {
                return true;
            }

            if (weapon.CompactHandling)
            {
                bool flag = false;
                IEnumerable<ProtrudableComponent> enumerable = Enumerable.Empty<ProtrudableComponent>();
                FoldableComponent foldableComponent;
                if (GClass2426.CanFold(weapon, out foldableComponent))
                {
                    if (foldableComponent.FoldedSlot == null)
                    {
                        flag |= !foldableComponent.Folded;
                    }
                    else if (foldableComponent.FoldedSlot.ContainedItem != null)
                    {
                        enumerable = Enumerable.ToArray<ProtrudableComponent>(foldableComponent.FoldedSlot.ContainedItem.GetItemComponentsInChildren<ProtrudableComponent>(true));
                        bool flag2 = flag;
                        bool flag3;
                        if (!foldableComponent.Folded)
                        {
                            flag3 = Enumerable.Any<ProtrudableComponent>(enumerable, new Func<ProtrudableComponent, bool>(checkProtruding));
                        }
                        else
                        {
                            flag3 = false;
                        }
                        flag = (flag2 || flag3);
                    }
                }
                IEnumerable<ProtrudableComponent> enumerable2 = Enumerable.Except<ProtrudableComponent>(weapon.GetItemComponentsInChildren<ProtrudableComponent>(true), enumerable);
                flag |= Enumerable.Any<ProtrudableComponent>(enumerable2, new Func<ProtrudableComponent, bool>(checkProtruding));
                return !flag;
            }
            return false;
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.Player.FirearmController __instance, ref bool ____isAiming)
        {
            if (Utils.IsReady == true)
            {
                Player player = (Player)AccessTools.Field(typeof(EFT.Player.FirearmController), "_player").GetValue(__instance);
                if (!player.IsAI && __instance.Item != null)
                {
                    FaceShieldComponent fsComponent = player.FaceShieldObserver.Component;
                    NightVisionComponent nvgComponent = player.NightVisionObserver.Component;
                    bool fsIsON = fsComponent != null && (fsComponent.Togglable == null || fsComponent.Togglable.On);
                    bool nvgIsOn = nvgComponent != null && (nvgComponent.Togglable == null || nvgComponent.Togglable.On);
                    bool isAllowedADSFS = IsAllowedADSWithFS(__instance.Item, __instance);
                    if ((Plugin.EnableNVGPatch.Value == true && nvgIsOn == true && Plugin.HasOptic) || (Plugin.EnableFSPatch.Value == true && (fsIsON && !isAllowedADSFS)))
                    {
                        if (!SetCanAds)
                        {
                            Plugin.IsAllowedADS = false;
                            player.MovementContext.SetAimingSlowdown(false, 0.33f);
                            player.ProceduralWeaponAnimation.IsAiming = false;
                            SetCanAds = true;
                        }
                    }
                    else
                    {
                        Plugin.IsAllowedADS = true;
                        SetCanAds = false;
                    }

                    if (StanceController.IsActiveAiming == true)
                    {
                        if (!SetActiveAimADS)
                        {
                            Plugin.IsAllowedADS = false;
                            player.ProceduralWeaponAnimation.IsAiming = false;
                            player.MovementContext.SetAimingSlowdown(true, 0.33f);
                            SetActiveAimADS = true;
                        }

                    }
                    if (!StanceController.IsActiveAiming && !____isAiming)
                    {
                        player.MovementContext.SetAimingSlowdown(false, 0.33f);
                        SetActiveAimADS = false;
                    }

                    if ((StanceController.IsHighReady == true || StanceController.WasHighReady == true) && !Plugin.RightArmBlacked)
                    {
                        if (!SetRunAnim)
                        {
                            player.BodyAnimatorCommon.SetFloat(GClass1645.WEAPON_SIZE_MODIFIER_PARAM_HASH, 2f);

                            SetRunAnim = true;
                            ResetRunAnim = false;
                        }

                    }
                    else
                    {
                        if (!ResetRunAnim)
                        {
                            player.BodyAnimatorCommon.SetFloat(GClass1645.WEAPON_SIZE_MODIFIER_PARAM_HASH, (float)__instance.Item.CalculateCellSize().X);
                            ResetRunAnim = true;
                            SetRunAnim = false;
                        }

                    }

                    if (player.ProceduralWeaponAnimation.OverlappingAllowsBlindfire == true)
                    {
                        Plugin.IsAiming = ____isAiming;
                        StanceController.PistolIsColliding = false;
                    }
                    else if (__instance.Item.WeapClass == "pistol")
                    {
                        StanceController.PistolIsColliding = true;
                    }

                }
            }
        }
    }

    public class ToggleAimPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.Player.FirearmController).GetMethod("ToggleAim", BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPrefix]
        private static bool Prefix(EFT.Player.FirearmController __instance)
        {
            Player player = (Player)AccessTools.Field(typeof(EFT.Player.ItemHandsController), "_player").GetValue(__instance);
            if (Plugin.EnableFSPatch.Value == true && !player.IsAI)
            {
                return Plugin.IsAllowedADS;
            }
            return true;
        }
    }
}