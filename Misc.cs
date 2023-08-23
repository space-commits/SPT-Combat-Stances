﻿using Aki.Reflection.Patching;
using EFT.InventoryLogic;
using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Comfort.Common;
using static EFT.Player;
using PlayerInterface = GInterface114;
using WeaponSkills = SkillsClass.GClass1743;

namespace CombatStances
{
    public class PlayerInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {

            if (__instance.IsYourPlayer == true)
            {
                StanceController.StanceBlender.Target = 0f;
                StanceController.SelectedStance = 0;
                StanceController.IsLowReady = false;
                StanceController.IsHighReady = false;
                StanceController.IsActiveAiming = false;
                StanceController.WasHighReady = false;
                StanceController.WasLowReady = false;
                StanceController.IsShortStock = false;
                StanceController.WasShortStock = false;
                StanceController.IsPatrolStance = false;
            }
        }
    }

    public class PwaWeaponParamsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("method_21", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void PatchPostfix(ref EFT.Animations.ProceduralWeaponAnimation __instance)
        {
            PlayerInterface playerInterface = (PlayerInterface)AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "ginterface114_0").GetValue(__instance);

            if (playerInterface != null && playerInterface.Weapon != null)
            {
                Weapon weapon = playerInterface.Weapon;
                Player player = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(weapon.Owner.ID);
                if (player != null && player.MovementContext.CurrentState.Name != EPlayerState.Stationary && player.IsYourPlayer)
                {
                    Plugin.HasOptic = __instance.CurrentScope.IsOptic ? true : false;
                    Plugin.AimSpeed = (float)AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "float_9").GetValue(__instance);
                    Plugin.ErgoDelta = weapon.ErgonomicsDelta;
                }
            }
        }
    }

    public class UpdateWeaponVariablesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("UpdateWeaponVariables", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(ref EFT.Animations.ProceduralWeaponAnimation __instance)
        {
            PlayerInterface playerInterface = (PlayerInterface)AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "ginterface114_0").GetValue(__instance);

            if (playerInterface != null && playerInterface.Weapon != null)
            {
                Weapon weapon = playerInterface.Weapon;
                Player player = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(weapon.Owner.ID);
                if (player != null && player.MovementContext.CurrentState.Name != EPlayerState.Stationary && player.IsYourPlayer)
                {
                    if (!Plugin.RecoilStandaloneIsPresent)
                    {
                        Plugin.HandsIntensity = __instance.HandsContainer.HandsRotation.InputIntensity;
                        Plugin.BreathIntensity = __instance.Breath.Intensity;
                        Plugin.RecoilIntensity = __instance.Shootingg.Intensity;
                        Plugin.HandsDamping = __instance.HandsContainer.HandsPosition.Damping;
                        Plugin.Convergence = __instance.HandsContainer.Recoil.ReturnSpeed;
                    }
                }
            }
        }
    }


    public class SyncWithCharacterSkillsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.Player.FirearmController).GetMethod("SyncWithCharacterSkills", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(ref EFT.Player.FirearmController __instance)
        {
            Player player = (Player)AccessTools.Field(typeof(Player.FirearmController), "_player").GetValue(__instance);
            if (player.IsYourPlayer == true)
            {
                WeaponSkills skillsClass = (WeaponSkills)AccessTools.Field(typeof(EFT.Player.FirearmController), "gclass1743_0").GetValue(__instance);
                Plugin.WeaponSkillErgo = skillsClass.DeltaErgonomics;
                Plugin.AimSkillADSBuff = skillsClass.AimSpeed;
            }
        }
    }


    public class RegisterShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player.FirearmController).GetMethod("RegisterShot", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void PatchPostfix(Player.FirearmController __instance)
        {
            Player player = (Player)AccessTools.Field(typeof(EFT.Player.FirearmController), "_player").GetValue(__instance);
            if (player.IsYourPlayer == true)
            {
                Plugin.Timer = 0f;
                StanceController.StanceShotTime = 0f;
                StanceController.IsFiringFromStance = true;
                Plugin.IsFiring = true;
                Plugin.ShotCount++;
            }
        }
    }

    public class PlayerLateUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static void PlayerInjuryStateCheck(Player player)
        {
            bool rightArmDamaged = player.MovementContext.PhysicalConditionIs(EPhysicalCondition.RightArmDamaged);
            bool leftArmDamaged = player.MovementContext.PhysicalConditionIs(EPhysicalCondition.LeftArmDamaged);
            bool tremor = player.MovementContext.PhysicalConditionIs(EPhysicalCondition.Tremor);

            Plugin.RightArmBlacked = rightArmDamaged;
            Plugin.LeftArmBlacked = leftArmDamaged;

            if (!rightArmDamaged && !leftArmDamaged && !tremor)
            {
                Plugin.AimMoveSpeedInjuryReduction = 0f;
                Plugin.ADSInjuryMulti = 1f;
            }
            if (tremor == true)
            {
                Plugin.AimMoveSpeedInjuryReduction = 0.025f;
                Plugin.ADSInjuryMulti = 0.85f;
            }
            if ((rightArmDamaged == true && !leftArmDamaged))
            {
                Plugin.AimMoveSpeedInjuryReduction = 0.07f;
                Plugin.ADSInjuryMulti = 0.6f;
            }
            if ((!rightArmDamaged && leftArmDamaged == true))
            {
                Plugin.AimMoveSpeedInjuryReduction = 0.05f;
                Plugin.ADSInjuryMulti = 0.7f;
            }
            if (rightArmDamaged == true && leftArmDamaged == true)
            {
                Plugin.AimMoveSpeedInjuryReduction = 0.1f;
                Plugin.ADSInjuryMulti = 0.5f;
            }
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            if (Utils.CheckIsReady() && __instance.IsYourPlayer)
            {
                Player.FirearmController fc = __instance.HandsController as Player.FirearmController;
                PlayerInjuryStateCheck(__instance);
                Plugin.IsSprinting = __instance.IsSprintEnabled;
                Plugin.IsInInventory = __instance.IsInventoryOpened;

                if (fc != null)
                {
                    AimController.ADSCheck(__instance, fc);

                    if (Plugin.EnableStanceStamChanges.Value == true)
                    {
                        StanceController.SetStanceStamina(__instance, fc);
                    }

                    Plugin.RemainingArmStamPercentage = Mathf.Min(__instance.Physical.HandsStamina.Current * 1.65f, __instance.Physical.HandsStamina.TotalCapacity) / __instance.Physical.HandsStamina.TotalCapacity;
                }
                else if (Plugin.EnableStanceStamChanges.Value == true)
                {
                    StanceController.ResetStanceStamina(__instance);
                }

                __instance.Physical.HandsStamina.Current = Mathf.Max(__instance.Physical.HandsStamina.Current, 1f);

                float mountingSwayBonus = StanceController.IsMounting ? StanceController.MountingSwayBonus : StanceController.BracingSwayBonus;
                float mountingRecoilBonus = StanceController.IsMounting ? StanceController.MountingRecoilBonus : StanceController.BracingRecoilBonus;

                if (!Plugin.RecoilStandaloneIsPresent)
                {
                    __instance.ProceduralWeaponAnimation.Shootingg.Intensity = Plugin.RecoilIntensity * mountingRecoilBonus;
                    __instance.ProceduralWeaponAnimation.Breath.Intensity = Plugin.BreathIntensity * mountingSwayBonus; //default if no recoil standalone, otherwise 
                    __instance.ProceduralWeaponAnimation.HandsContainer.HandsRotation.InputIntensity = Plugin.HandsIntensity * mountingSwayBonus; //default if no recoil standalone, otherwise 
                }

                if (Plugin.IsFiring && !Plugin.RecoilStandaloneIsPresent)
                {
                    StanceController.IsPatrolStance = false;
                    __instance.HandsController.FirearmsAnimator.SetPatrol(false);
                    __instance.ProceduralWeaponAnimation.HandsContainer.HandsPosition.Damping = Plugin.HandsDamping; //default if no recoil standalone, otherwise its value
                    __instance.ProceduralWeaponAnimation.HandsContainer.Recoil.ReturnSpeed = Plugin.Convergence; //default if no recoil standalone, otherwise its value
                }
                else if (!Plugin.IsFiring)
                {
                    __instance.HandsController.FirearmsAnimator.SetPatrol(StanceController.IsPatrolStance);

                    if (StanceController.CanResetDamping)
                    {
                        __instance.ProceduralWeaponAnimation.HandsContainer.HandsPosition.Damping = Mathf.Lerp(__instance.ProceduralWeaponAnimation.HandsContainer.HandsPosition.Damping, 0.45f, 0.01f);
                    }
                    else
                    {
                        __instance.ProceduralWeaponAnimation.HandsContainer.HandsPosition.Damping = 0.75f;
                        __instance.ProceduralWeaponAnimation.Shootingg.ShotVals[3].Intensity = 0;
                        __instance.ProceduralWeaponAnimation.Shootingg.ShotVals[4].Intensity = 0;
                    }
                    __instance.ProceduralWeaponAnimation.HandsContainer.Recoil.ReturnSpeed = Mathf.Lerp(__instance.ProceduralWeaponAnimation.HandsContainer.Recoil.ReturnSpeed, 10f * StanceController.WiggleReturnSpeed, 0.01f);
                }
            }
        }
    }
}
