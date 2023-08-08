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
using InventoryItemHandler = GClass2672;


namespace CombatStances
{

    public static class AimController
    {
        private static bool hasSetActiveAimADS = false;
        private static bool wasToggled = false;
        private static bool hasSetCanAds = false;

        private static bool checkProtruding(ProtrudableComponent x)
        {
            return x.IsProtruding();
        }

        //bsg's bullshit
        private static bool IsAllowedADSWithFS(Weapon weapon, Player.FirearmController fc)
        {
            if (weapon.CompactHandling)
            {
                bool stockIsDeployed = false;
                IEnumerable<ProtrudableComponent> foldableStockComponents = Enumerable.Empty<ProtrudableComponent>();
                FoldableComponent foldableComponent;
                if (InventoryItemHandler.CanFold(weapon, out foldableComponent))
                {
                    if (foldableComponent.FoldedSlot == null)
                    {
                        stockIsDeployed |= !foldableComponent.Folded;
                    }
                    else if (foldableComponent.FoldedSlot.ContainedItem != null)
                    {
                        foldableStockComponents = Enumerable.ToArray<ProtrudableComponent>(foldableComponent.FoldedSlot.ContainedItem.GetItemComponentsInChildren<ProtrudableComponent>(true));
                        bool stockIsProtruding;
                        if (!foldableComponent.Folded)
                        {
                            stockIsProtruding = Enumerable.Any<ProtrudableComponent>(foldableStockComponents, new Func<ProtrudableComponent, bool>(checkProtruding));
                        }
                        else
                        {
                            stockIsProtruding = false;
                        }
                        stockIsDeployed = (stockIsDeployed || stockIsProtruding);
                    }
                }
                IEnumerable<ProtrudableComponent> stocks = Enumerable.Except<ProtrudableComponent>(weapon.GetItemComponentsInChildren<ProtrudableComponent>(true), foldableStockComponents);
                stockIsDeployed |= Enumerable.Any<ProtrudableComponent>(stocks, new Func<ProtrudableComponent, bool>(checkProtruding));
                return !stockIsDeployed;
            }

            if (weapon.WeapClass == "pistol")
            {
                return true;
            }

            return false;
        }

        public static void ADSCheck(Player player, EFT.Player.FirearmController fc)
        {
            if (!player.IsAI && fc.Item != null)
            {
                bool isAiming = (bool)AccessTools.Field(typeof(EFT.Player.FirearmController), "_isAiming").GetValue(fc);
                FaceShieldComponent fsComponent = player.FaceShieldObserver.Component;
                NightVisionComponent nvgComponent = player.NightVisionObserver.Component;
                bool fsIsON = fsComponent != null && (fsComponent.Togglable == null || fsComponent.Togglable.On);
                bool nvgIsOn = nvgComponent != null && (nvgComponent.Togglable == null || nvgComponent.Togglable.On);
                bool isAllowedADSFS = IsAllowedADSWithFS(fc.Item, fc);
                if ((Plugin.EnableNVGPatch.Value && nvgIsOn && Plugin.HasOptic) || (Plugin.EnableFSPatch.Value && (fsIsON && !isAllowedADSFS)))
                {
                    if (!hasSetCanAds)
                    {
                        Plugin.IsAllowedADS = false;
                        player.ProceduralWeaponAnimation.IsAiming = false;
                        AccessTools.Field(typeof(EFT.Player.FirearmController), "_isAiming").SetValue(fc, false);
                        hasSetCanAds = true;
                    }
                }
                else
                {
                    Plugin.IsAllowedADS = true;
                    hasSetCanAds = false;
                }

                if (StanceController.IsActiveAiming && !isAiming)
                {
                    if (!hasSetActiveAimADS)
                    {
                        Plugin.IsAllowedADS = false;
                        player.ProceduralWeaponAnimation.IsAiming = false;
                        AccessTools.Field(typeof(EFT.Player.FirearmController), "_isAiming").SetValue(fc, false);
                        player.MovementContext.SetAimingSlowdown(true, 0.33f);
                        hasSetActiveAimADS = true;
                    }

                }
                if (!StanceController.IsActiveAiming && hasSetActiveAimADS)
                {
                    player.MovementContext.SetAimingSlowdown(false, 0.33f);
                    hasSetActiveAimADS = false;
                }

                if (isAiming)
                {
                    player.MovementContext.SetAimingSlowdown(true, 0.33f);
                }

                if (!wasToggled && (fsIsON || nvgIsOn))
                {
                    wasToggled = true;
                }
                if (wasToggled == true && (!fsIsON && !nvgIsOn))
                {
                    StanceController.WasActiveAim = false;
                    if (Plugin.ToggleActiveAim.Value)
                    {
                        StanceController.IsActiveAiming = false;
                        Plugin.StanceBlender.Target = 0f;
                    }
                    wasToggled = false;
                }

                if (player.ProceduralWeaponAnimation.OverlappingAllowsBlindfire)
                {
                    Plugin.IsAiming = isAiming;
                    StanceController.PistolIsColliding = false;
                }
                else if (fc.Item.WeapClass == "pistol")
                {
                    StanceController.PistolIsColliding = true;
                }

            }
        }
    }

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
            if ((Plugin.EnableFSPatch.Value || Plugin.EnableNVGPatch.Value) && !player.IsAI)
            {
                return Plugin.IsAllowedADS;
            }
            return true;
        }
    }
}