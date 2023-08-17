using Aki.Reflection.Patching;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using static EFT.Player;
using MovementContext = GClass1667;
using ValueHandler = GClass765;

namespace CombatStances
{

    public class ClampSpeedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(MovementContext).GetMethod("ClampSpeed", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool Prefix(MovementContext __instance, float speed, ref float __result)
        {

            Player player = (Player)AccessTools.Field(typeof(MovementContext), "player_0").GetValue(__instance);
            if (player.IsYourPlayer == true)
            {
                float stanceFactor = StanceController.IsPatrolStance ? 1.25f : StanceController.IsHighReady ? 0.9f : 1f;

                __result = Mathf.Clamp(speed, 0f, __instance.StateSpeedLimit * stanceFactor);
                return false;
            }
            return true;

        }
    }


    public class SetAimingSlowdownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(MovementContext).GetMethod("SetAimingSlowdown", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool Prefix(ref MovementContext __instance, bool isAiming, float slow)
        {

            Player player = (Player)AccessTools.Field(typeof(MovementContext), "player_0").GetValue(__instance);
            if (player.IsYourPlayer == true)
            {
                if (isAiming)
                {
                    //slow is hard set to 0.33 when called, 0.4-0.43 feels best.
                    float baseSpeed = slow + 0.07f - Plugin.AimMoveSpeedInjuryReduction;
                    float totalSpeed = StanceController.IsActiveAiming ? baseSpeed * 1.45f : baseSpeed;
                    __instance.AddStateSpeedLimit(Math.Max(totalSpeed, 0.15f), Player.ESpeedLimit.Aiming);

                    return false;
                }
                __instance.RemoveStateSpeedLimit(Player.ESpeedLimit.Aiming);
                return false;
            }
            return true;
        }
    }

    public class SprintAccelerationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(MovementContext).GetMethod("SprintAcceleration", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool Prefix(MovementContext __instance, float deltaTime)
        {
            Player player = (Player)AccessTools.Field(typeof(MovementContext), "player_0").GetValue(__instance);

            if (player.IsYourPlayer == true)
            {
                ValueHandler rotationFrameSpan = (ValueHandler)AccessTools.Field(typeof(MovementContext), "gclass765_0").GetValue(__instance);
                float stanceAccelBonus = StanceController.IsShortStock ? 0.9f : StanceController.IsLowReady ? 1.3f : StanceController.IsHighReady && Plugin.EnableTacSprint.Value ? 1.7f : StanceController.IsHighReady ? 1.3f : 1f;
                float stanceSpeedBonus = StanceController.IsPatrolStance ? 1.5f : StanceController.IsHighReady && Plugin.EnableTacSprint.Value ? 1.15f : 1f;

                float sprintAccel = player.Physical.SprintAcceleration * deltaTime * stanceAccelBonus;
                float speed = (player.Physical.SprintSpeed * __instance.SprintingSpeed + 1f) * __instance.StateSprintSpeedLimit * stanceSpeedBonus;
                float sprintInertia = Mathf.Max(EFTHardSettings.Instance.sprintSpeedInertiaCurve.Evaluate(Mathf.Abs((float)rotationFrameSpan.Average)), EFTHardSettings.Instance.sprintSpeedInertiaCurve.Evaluate(2.1474836E+09f) * (2f - player.Physical.Inertia));
                speed = Mathf.Clamp(speed * sprintInertia, 0.1f, speed);
                __instance.SprintSpeed = Mathf.Clamp(__instance.SprintSpeed + sprintAccel * Mathf.Sign(speed - __instance.SprintSpeed), 0.01f, speed);

                return false;
            }
            return true;
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
            if (Utils.CheckIsReady() == true && __instance.IsYourPlayer == true)
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

                __instance.ProceduralWeaponAnimation.Breath.Intensity = Plugin.BreathIntensity * mountingSwayBonus; //default if no recoil standalone, otherwise 
                __instance.ProceduralWeaponAnimation.HandsContainer.HandsRotation.InputIntensity = Plugin.HandsIntensity * mountingSwayBonus; //default if no recoil standalone, otherwise 
                __instance.ProceduralWeaponAnimation.Shootingg.Intensity = Plugin.RecoilIntensity * mountingRecoilBonus;    

                if (StanceController.IsFiring)
                {
                    StanceController.IsPatrolStance = false;
                    __instance.HandsController.FirearmsAnimator.SetPatrol(false);
                    __instance.ProceduralWeaponAnimation.HandsContainer.HandsPosition.Damping = Plugin.HandsDamping; //default if no recoil standalone, otherwise its value
                    __instance.ProceduralWeaponAnimation.HandsContainer.Recoil.ReturnSpeed = Plugin.Convergence; //default if no recoil standalone, otherwise its value
                }
                else
                {
                    __instance.HandsController.FirearmsAnimator.SetPatrol(StanceController.IsPatrolStance);

                    if (StanceController.CanResetDamping)
                    {
                        __instance.ProceduralWeaponAnimation.HandsContainer.HandsPosition.Damping = Mathf.Lerp(__instance.ProceduralWeaponAnimation.HandsContainer.HandsPosition.Damping, 0.45f, 0.04f); //default if no recoil standalone, otherwise its value
                    }
                    else
                    {
                        __instance.ProceduralWeaponAnimation.HandsContainer.HandsPosition.Damping = 0.75f;
                        __instance.ProceduralWeaponAnimation.Shootingg.ShotVals[3].Intensity = 0;
                        __instance.ProceduralWeaponAnimation.Shootingg.ShotVals[4].Intensity = 0;
                    }
                    __instance.ProceduralWeaponAnimation.HandsContainer.Recoil.ReturnSpeed = 10f * StanceController.WiggleReturnSpeed;
                }
            }
        }
    }
}
