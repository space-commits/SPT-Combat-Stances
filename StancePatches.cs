﻿using Aki.Reflection.Patching;
using BepInEx.Logging;
using CombatStances;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using EFT.Animations;
using static EFT.Player;
using LightStruct = GStruct155;
using PlayerInterface = GInterface113;
using CastingClass = GClass570;

namespace CombatStances
{
    public class OnWeaponDrawPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(SkillManager).GetMethod("OnWeaponDraw", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(SkillManager __instance, Item item)
        {
            if (item?.Owner?.ID != null && (item.Owner.ID.StartsWith("pmc") || item.Owner.ID.StartsWith("scav")))
            {
                Plugin.DidWeaponSwap = true;
            }
        }
    }

    public class SetFireModePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(FirearmsAnimator).GetMethod("SetFireMode", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool Prefix(FirearmsAnimator __instance, Weapon.EFireMode fireMode, bool skipAnimation = false)
        {

            __instance.ResetLeftHand();
            skipAnimation = StanceController.IsHighReady && Plugin.IsSprinting ? true : skipAnimation;
            WeaponAnimationSpeedControllerClass.SetFireMode(__instance.Animator, (float)fireMode);
            if (!skipAnimation)
            {
                WeaponAnimationSpeedControllerClass.TriggerFiremodeSwitch(__instance.Animator);
            }
            return false;
        }
    }

    public class WeaponLengthPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player.FirearmController).GetMethod("method_7", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void PatchPostfix(Player.FirearmController __instance)
        {
            Player player = (Player)AccessTools.Field(typeof(EFT.Player.FirearmController), "_player").GetValue(__instance);
            float length = (float)AccessTools.Field(typeof(EFT.Player.FirearmController), "WeaponLn").GetValue(__instance);
            if (player.IsYourPlayer == true)
            {
                Plugin.BaseWeaponLength = length;
                Plugin.NewWeaponLength = length >= 0.9f ? length * 1.15f : length;
            }
        }
    }

    public class OperateStationaryWeaponPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetMethod("OperateStationaryWeapon", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            if (__instance.IsYourPlayer)
            {
                StanceController.CancelAllStances();
                StanceController.StanceBlender.Target = 0f;
                StanceController.StanceTargetPosition = Vector3.zero;

            }
        }
    }

    public class CollisionPatch : ModulePatch
    {
        private static FieldInfo playerField;
        private static FieldInfo hitIgnoreField;
        private static Player player;

        private static int timer = 0;

        private static void setMountingStatus(bool isTop, bool isRightide)
        {
            if (isTop)
            {
                StanceController.IsBracingTop = true;
                StanceController.IsBracingSide = false;
                StanceController.IsBracingRightSide = false;
                StanceController.IsBracingLeftSide = false;
            }
            else
            {
                StanceController.IsBracingSide = true;
                StanceController.IsBracingTop = false;

                if (isRightide)
                {
                    StanceController.IsBracingRightSide = true;
                    StanceController.IsBracingLeftSide = false;
                }
                else
                {
                    StanceController.IsBracingRightSide = false;
                    StanceController.IsBracingLeftSide = true;
                }
            }
        }

        protected override MethodBase GetTargetMethod()
        {
            playerField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_player");
            hitIgnoreField = AccessTools.Field(typeof(EFT.Player.FirearmController), "func_1");

            return typeof(Player.FirearmController).GetMethod("method_8", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static void doStability(bool isTop, bool isRightide, string weapClass)
        {
            if (!StanceController.IsMounting)
            {
                setMountingStatus(isTop, isRightide);
            }

            StanceController.IsBracing = true;

            float mountOrientationBonus = StanceController.IsBracingTop ? 0.8f : 1f;
            float mountingRecoilBase = weapClass == "pistol" ? 0.1f : 0.3f;
            if (Plugin.RecoilStandaloneIsPresent) 
            {
                mountingRecoilBase = weapClass == "pistol" ? 0.5f : 0.65f;
            }

            StanceController.BracingSwayBonus = Mathf.Lerp(StanceController.BracingSwayBonus, 0.8f * mountOrientationBonus, 0.25f);
            StanceController.BracingRecoilBonus = Mathf.Lerp(StanceController.BracingRecoilBonus, 0.85f * mountOrientationBonus, 0.25f);
            StanceController.MountingSwayBonus = Mathf.Clamp(0.55f * mountOrientationBonus, 0.4f, 1f);
            StanceController.MountingRecoilBonus = Mathf.Clamp(mountingRecoilBase * mountOrientationBonus, 0.1f, 1f);
        }

        [PatchPrefix]
        private static void PatchPrefix(Player.FirearmController __instance, Vector3 origin, float ln, Vector3? weaponUp = null)
        {
            player = (Player)playerField.GetValue(__instance);
            if (player.IsYourPlayer)
            {
                timer += 1;
                if (timer >= 60)
                {
                    timer = 0;

                    RaycastHit[] raycastArr = AccessTools.StaticFieldRefAccess<EFT.Player.FirearmController, RaycastHit[]>("raycastHit_0");
                    Func<RaycastHit, bool> func_1 = (Func<RaycastHit, bool>)AccessTools.Field(typeof(EFT.Player.FirearmController), "func_1").GetValue(__instance);

                    string weapClass = __instance.Item.WeapClass;

                    float wiggleAmount = 6f;
                    float moveToCoverOffset = 0.01f;

                    Transform weapTransform = player.ProceduralWeaponAnimation.HandsContainer.WeaponRootAnim;
                    Vector3 linecastDirection = weapTransform.TransformDirection(Vector3.up);

                    Vector3 startDown = weapTransform.position + weapTransform.TransformDirection(new Vector3(0f, 0f, -0.12f));
                    Vector3 startLeft = weapTransform.position + weapTransform.TransformDirection(new Vector3(0.1f, 0f, 0f));
                    Vector3 startRight = weapTransform.position + weapTransform.TransformDirection(new Vector3(-0.1f, 0f, 0f));

                    Vector3 forwardDirection = startDown - linecastDirection * ln;
                    Vector3 leftDirection = startLeft - linecastDirection * ln;
                    Vector3 rightDirection = startRight - linecastDirection * ln;
                    /*
                                    DebugGizmos.SingleObjects.Line(startDown, forwardDirection, Color.red, 0.02f, true, 0.3f, true);
                                    DebugGizmos.SingleObjects.Line(startLeft, leftDirection, Color.green, 0.02f, true, 0.3f, true);
                                    DebugGizmos.SingleObjects.Line(startRight, rightDirection, Color.yellow, 0.02f, true, 0.3f, true);*/

                    RaycastHit raycastHit;
                    if (CastingClass.Linecast(startDown, forwardDirection, out raycastHit, EFTHardSettings.Instance.WEAPON_OCCLUSION_LAYERS, false, raycastArr, func_1))
                    {
                        doStability(true, false, weapClass);
                        StanceController.CoverWiggleDirection = new Vector3(wiggleAmount, 0f, 0f);
                        StanceController.CoverDirection = new Vector3(0f, -moveToCoverOffset, 0f);
                        return;
                    }
                    if (CastingClass.Linecast(startLeft, leftDirection, out raycastHit, EFTHardSettings.Instance.WEAPON_OCCLUSION_LAYERS, false, raycastArr, func_1))
                    {
                        doStability(false, false, weapClass);
                        StanceController.CoverWiggleDirection = new Vector3(0f, wiggleAmount, 0f);
                        StanceController.CoverDirection = new Vector3(moveToCoverOffset, 0f, 0f);
                        return;
                    }
                    if (CastingClass.Linecast(startRight, rightDirection, out raycastHit, EFTHardSettings.Instance.WEAPON_OCCLUSION_LAYERS, false, raycastArr, func_1))
                    {
                        doStability(false, true, weapClass);
                        StanceController.CoverWiggleDirection = new Vector3(0f, -wiggleAmount, 0f);
                        StanceController.CoverDirection = new Vector3(-moveToCoverOffset, 0f, 0f);
                        return;
                    }

                    StanceController.BracingSwayBonus = Mathf.Lerp(StanceController.BracingSwayBonus, 1f, 0.5f);
                    StanceController.BracingRecoilBonus = Mathf.Lerp(StanceController.BracingRecoilBonus, 1f, 0.5f);
                    StanceController.IsBracing = false;
                }
            }
        }
    }

    public class WeaponOverlapViewPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player.FirearmController).GetMethod("WeaponOverlapView", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool PatchPrefix(Player.FirearmController __instance)
        {
            Player player = (Player)AccessTools.Field(typeof(EFT.Player.FirearmController), "_player").GetValue(__instance);

            if (player.IsYourPlayer && StanceController.IsMounting)
            {
                return false;
            }
            return true;
        }
    }

    public class WeaponOverlappingPatch : ModulePatch
    {
        private static FieldInfo weaponLnField;
        private static FieldInfo playerField;

        protected override MethodBase GetTargetMethod()
        {
            playerField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_player");
            weaponLnField = AccessTools.Field(typeof(EFT.Player.FirearmController), "WeaponLn");
            return typeof(Player.FirearmController).GetMethod("WeaponOverlapping", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance)
        {
            Player player = (Player)playerField.GetValue(__instance);

            if (player.IsYourPlayer == true)
            {
                if (__instance.Item.WeapClass == "pistol")
                {
                    if (StanceController.PistolIsCompressed == true)
                    {
                        weaponLnField.SetValue(__instance, Plugin.NewWeaponLength * 0.75f);
                    }
                    else
                    {
                        weaponLnField.SetValue(__instance, Plugin.NewWeaponLength * 0.85f);
                    }
                    return;
                }
                else 
                {
                    if (StanceController.IsHighReady == true || StanceController.IsLowReady == true || StanceController.IsShortStock == true)
                    {
                        weaponLnField.SetValue(__instance, Plugin.NewWeaponLength * 0.8f);
                        return;
                    }
                    if (StanceController.WasShortStock == true && Plugin.IsAiming)
                    {
                        weaponLnField.SetValue(__instance, Plugin.NewWeaponLength * 0.7f);
                        return;
                    }
                }
                weaponLnField.SetValue(__instance, Plugin.NewWeaponLength);
                return;
            }
        }
    }

    public class InitTransformsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("InitTransforms", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.Animations.ProceduralWeaponAnimation __instance)
        {

            PlayerInterface playerInterface = (PlayerInterface)AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_firearmAnimationData").GetValue(__instance);

            if (playerInterface != null && playerInterface.Weapon != null)
            {
                Weapon weapon = playerInterface.Weapon;
                Player player = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(weapon.Owner.ID);
                if (player != null && player.MovementContext.CurrentState.Name != EPlayerState.Stationary && player.IsYourPlayer)
                {
                    Plugin.WeaponOffsetPosition = __instance.HandsContainer.WeaponRoot.localPosition += new Vector3(Plugin.WeapOffsetX.Value, Plugin.WeapOffsetY.Value, Plugin.WeapOffsetZ.Value);
                    __instance.HandsContainer.WeaponRoot.localPosition += new Vector3(Plugin.WeapOffsetX.Value, Plugin.WeapOffsetY.Value, Plugin.WeapOffsetZ.Value);
                    Plugin.TransformBaseStartPosition = new Vector3(0.0f, 0.0f, 0.0f);
                }
            }
        }
    }


    public class ZeroAdjustmentsPatch : ModulePatch
    {
        private static FieldInfo blindFireStrengthField;
        private static FieldInfo blindfireRotationField;
        private static PropertyInfo overlappingBlindfireField;
        private static FieldInfo blindfirePositionField;

        private static Vector3 targetPosition = Vector3.zero;

        protected override MethodBase GetTargetMethod()
        {
            blindFireStrengthField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_blindfireStrength");
            blindfireRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_blindFireRotation");
            overlappingBlindfireField = AccessTools.Property(typeof(EFT.Animations.ProceduralWeaponAnimation), "Single_3");
            blindfirePositionField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_blindFirePosition");

            return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("ZeroAdjustments", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool PatchPrefix(EFT.Animations.ProceduralWeaponAnimation __instance)
        {
            PlayerInterface playerInterface = (PlayerInterface)AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_firearmAnimationData").GetValue(__instance);

            if (playerInterface != null && playerInterface.Weapon != null)
            {
                Weapon weapon = playerInterface.Weapon;
                Player player = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(weapon.Owner.ID);
                if (player != null && player.IsYourPlayer == true)
                {
                    float collidingModifier = (float)overlappingBlindfireField.GetValue(__instance);
                    Vector3 blindfirePosition = (Vector3)blindfirePositionField.GetValue(__instance);

                    Vector3 highReadyTargetPosition = new Vector3(Plugin.HighReadyOffsetX.Value, Plugin.HighReadyOffsetY.Value, Plugin.HighReadyOffsetZ.Value);

                    __instance.PositionZeroSum.y = (__instance._shouldMoveWeaponCloser ? 0.05f : 0f);
                    __instance.RotationZeroSum.y = __instance.SmoothedTilt * __instance.PossibleTilt;

                    float stanceBlendValue = StanceController.StanceBlender.Value;
                    float stanceAbs = Mathf.Abs(stanceBlendValue);

                    float blindFireBlendValue = __instance.BlindfireBlender.Value;
                    float blindFireAbs = Mathf.Abs(blindFireBlendValue);

                    if (blindFireAbs > 0f)
                    {
                        Plugin.IsBlindFiring = true;
                        float pitch = ((Mathf.Abs(__instance.Pitch) < 45f) ? 1f : ((90f - Mathf.Abs(__instance.Pitch)) / 45f));
                        blindFireStrengthField.SetValue(__instance, pitch);
                        blindfireRotationField.SetValue(__instance, ((blindFireBlendValue > 0f) ? (__instance.BlindFireRotation * blindFireAbs) : (__instance.SideFireRotation * blindFireAbs)));
                        targetPosition = ((blindFireBlendValue > 0f) ? (__instance.BlindFireOffset * blindFireAbs) : (__instance.SideFireOffset * blindFireAbs));
                        targetPosition += StanceController.StanceTargetPosition;
                        __instance.BlindFireEndPosition = ((blindFireBlendValue > 0f) ? __instance.BlindFireOffset : __instance.SideFireOffset);
                        __instance.BlindFireEndPosition *= pitch;
                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * collidingModifier * targetPosition;
                        __instance.HandsContainer.HandsRotation.Zero = __instance.RotationZeroSum;
                        return false;
                    }

                    Plugin.IsBlindFiring = false;

                    if (stanceAbs > 0f)
                    {
                        float pitch = ((Mathf.Abs(__instance.Pitch) < 45f) ? 1f : ((90f - Mathf.Abs(__instance.Pitch)) / 45f));
                        blindFireStrengthField.SetValue(__instance, pitch);
                        targetPosition = StanceController.StanceTargetPosition * stanceAbs;
                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * targetPosition;
                        __instance.HandsContainer.HandsRotation.Zero = __instance.RotationZeroSum;
                        return false;
                    }
                    else
                    {
                        targetPosition = Vector3.zero;
                    }

                    __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + collidingModifier * targetPosition;
                    __instance.HandsContainer.HandsRotation.Zero = __instance.RotationZeroSum;
                    return false;
                }
            }
            return true;
        }
    }

    public class RotatePatch : ModulePatch
    {
        private static FieldInfo movementContextField;
        private static FieldInfo playerField;
        private static Vector2 initialRotation = Vector3.zero;

        protected override MethodBase GetTargetMethod()
        {
            movementContextField = AccessTools.Field(typeof(MovementState), "MovementContext");
            playerField = AccessTools.Field(typeof(MovementContext), "_player");
            return typeof(MovementState).GetMethod("Rotate", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool Prefix(MovementState __instance, ref Vector2 deltaRotation, bool ignoreClamp)
        {
            MovementContext movementContext = (MovementContext)movementContextField.GetValue(__instance);
            Player player = (Player)playerField.GetValue(movementContext);

            if (player.IsYourPlayer)
            {

                if (!StanceController.IsMounting)
                {
                    initialRotation = movementContext.Rotation;
                }

                if (StanceController.IsMounting && !ignoreClamp)
                {
                    FirearmController fc = player.HandsController as FirearmController;

                    Vector2 currentRotation = movementContext.Rotation;

                    deltaRotation *= (fc.AimingSensitivity * 0.9f);

                    float lowerClampXLimit = StanceController.IsBracingTop ? -17f : StanceController.IsBracingRightSide ? -4f : -15f;
                    float upperClampXLimit = StanceController.IsBracingTop ? 17f : StanceController.IsBracingRightSide ? 15f : 1f;

                    float lowerClampYLimit = StanceController.IsBracingTop ? -10f : -8f;
                    float upperClampYLimit = StanceController.IsBracingTop ? 10f : 15f;

                    float relativeLowerXLimit = initialRotation.x + lowerClampXLimit;
                    float relativeUpperXLimit = initialRotation.x + upperClampXLimit;
                    float relativeLowerYLimit = initialRotation.y + lowerClampYLimit;
                    float relativeUpperYLimit = initialRotation.y + upperClampYLimit;

                    float clampedX = Mathf.Clamp(currentRotation.x + deltaRotation.x, relativeLowerXLimit, relativeUpperXLimit);
                    float clampedY = Mathf.Clamp(currentRotation.y + deltaRotation.y, relativeLowerYLimit, relativeUpperYLimit);

                    deltaRotation = new Vector2(clampedX - currentRotation.x, clampedY - currentRotation.y);

                    deltaRotation = movementContext.ClampRotation(deltaRotation);

                    movementContext.Rotation += deltaRotation;

                    return false;
                }
            }
            return true;
        }
    }

    public class SetTiltPatch : ModulePatch
    {
        private static FieldInfo movementContextField;
        private static FieldInfo playerField;

        protected override MethodBase GetTargetMethod()
        {
            movementContextField = AccessTools.Field(typeof(MovementState), "MovementContext");
            playerField = AccessTools.Field(typeof(MovementContext), "_player");
            return typeof(MovementState).GetMethod("SetTilt", BindingFlags.Instance | BindingFlags.Public);
        }

        public static float currentTilt = 0f;
        public static float currentPoseLevel = 0f;
        public static bool wasProne = false;

        [PatchPrefix]
        private static void Prefix(MovementState __instance, float tilt)
        {
            MovementContext movementContext = (MovementContext)movementContextField.GetValue(__instance);
            Player player = (Player)playerField.GetValue(movementContext);

            if (player.IsYourPlayer)
            {
                if (!StanceController.IsMounting)
                {
                    currentTilt = tilt;
                    currentPoseLevel = movementContext.PoseLevel;
                    wasProne = movementContext.IsInPronePose;
                }

                if (currentTilt != tilt || currentPoseLevel != movementContext.PoseLevel || !movementContext.IsGrounded || wasProne != movementContext.IsInPronePose)
                {
                    StanceController.IsMounting = false;
                }
            }
        }
    }


    public class ApplySimpleRotationPatch : ModulePatch
    {
        private static FieldInfo aimSpeedField;
        private static FieldInfo pitchField;
        private static FieldInfo blindfireRotationField;
        private static FieldInfo aimingQuatField;
        private static FieldInfo weapRotationField;
        private static FieldInfo isAimingField;
        private static FieldInfo weaponPositionField;
        private static PropertyInfo overlappingBlindfireField;
        private static FieldInfo currentRotationField;

        private static bool hasResetActiveAim = true;
        private static bool hasResetLowReady = true;
        private static bool hasResetHighReady = true;
        private static bool hasResetShortStock = true;
        private static bool hasResetPistolPos = true;

        private static bool isResettingActiveAim = false;
        private static bool isResettingLowReady = false;
        private static bool isResettingHighReady = false;
        private static bool isResettingShortStock = false;
        private static bool isResettingPistol = false;

        private static Quaternion currentRotation = Quaternion.identity;
        private static Quaternion stanceRotation = Quaternion.identity;
        private static Vector3 mountWeapPosition = Vector3.zero;

        private static float stanceSpeed = 1f;

        protected override MethodBase GetTargetMethod()
        {
            aimSpeedField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_aimingSpeed");
            pitchField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_blindfireStrength");
            blindfireRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_blindFireRotation");
            weaponPositionField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_temporaryPosition");
            aimingQuatField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_targetScopeRotation");
            weapRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_temporaryRotation");
            isAimingField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_isAiming");
            overlappingBlindfireField = AccessTools.Property(typeof(EFT.Animations.ProceduralWeaponAnimation), "Single_3");
            currentRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_cameraIdenity");

            return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("ApplySimpleRotation", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void Postfix(ref EFT.Animations.ProceduralWeaponAnimation __instance, float dt)
        {
            PlayerInterface playerInterface = (PlayerInterface)AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_firearmAnimationData").GetValue(__instance);

            if (playerInterface != null && playerInterface.Weapon != null)
            {
                Weapon weapon = playerInterface.Weapon;
                Player player = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(weapon.Owner.ID);
                if (player != null)
                {
                    Player.FirearmController firearmController = player.HandsController as Player.FirearmController;

                    float pitch = (float)pitchField.GetValue(__instance);
                    Quaternion aimingQuat = (Quaternion)aimingQuatField.GetValue(__instance);
                    float overlappingBlindfire = (float)overlappingBlindfireField.GetValue(__instance);
                    Vector3 blindfireRotation = (Vector3)blindfireRotationField.GetValue(__instance);
                    Vector3 weaponPosition = (Vector3)weaponPositionField.GetValue(__instance);
                    Quaternion weapRotation = (Quaternion)weapRotationField.GetValue(__instance);

                    if (player.IsYourPlayer)
                    {
                        Plugin.IsInThirdPerson = true;

                        float aimSpeed = (float)aimSpeedField.GetValue(__instance);
                        bool isAiming = (bool)isAimingField.GetValue(__instance);

                        bool isPistol = firearmController.Item.WeapClass == "pistol";
                        bool allStancesReset = hasResetActiveAim && hasResetLowReady && hasResetHighReady && hasResetShortStock && hasResetPistolPos;
                        bool isInStance = StanceController.IsHighReady || StanceController.IsLowReady || StanceController.IsShortStock || StanceController.IsActiveAiming;
                        bool isInShootableStance = StanceController.IsShortStock || StanceController.IsActiveAiming || isPistol;
                        bool cancelBecauseSooting = StanceController.IsFiringStance && !StanceController.IsActiveAiming && !StanceController.IsShortStock && !isPistol;
                        bool doStanceRotation = (isInStance || !allStancesReset || StanceController.PistolIsCompressed) && !cancelBecauseSooting;
                        bool cancelStance = (StanceController.CancelActiveAim && StanceController.IsActiveAiming) || (StanceController.CancelHighReady && StanceController.IsHighReady) || (StanceController.CancelLowReady && StanceController.IsLowReady) || (StanceController.CancelShortStock && StanceController.IsShortStock) || (StanceController.CancelPistolStance && StanceController.PistolIsCompressed);

                        StanceController.DoMounting(Logger, player, __instance, ref weaponPosition, ref mountWeapPosition, dt, __instance.HandsContainer.WeaponRoot.position);
                        weaponPositionField.SetValue(__instance, weaponPosition);

                        currentRotation = Quaternion.Slerp(currentRotation, __instance.IsAiming && allStancesReset ? aimingQuat : doStanceRotation ? stanceRotation : Quaternion.identity, doStanceRotation ? stanceSpeed : __instance.IsAiming ? 8f * aimSpeed * dt : 8f * dt);

                        Quaternion rhs = Quaternion.Euler(pitch * overlappingBlindfire * blindfireRotation);
                        __instance.HandsContainer.WeaponRootAnim.SetPositionAndRotation(weaponPosition, weapRotation * rhs * currentRotation);

                        if (isPistol && Plugin.EnableAltPistol.Value && !StanceController.IsPatrolStance)
                        {
                            if (StanceController.PistolIsCompressed && !Plugin.IsAiming && !isResettingPistol && !Plugin.IsBlindFiring)
                            {
                                StanceController.StanceBlender.Target = 1f;
                            }
                            else
                            {
                                StanceController.StanceBlender.Target = 0f;
                            }

                            if ((!StanceController.PistolIsCompressed && !Plugin.IsAiming && !isResettingPistol) || (Plugin.IsBlindFiring))
                            {
                                StanceController.StanceTargetPosition = Vector3.Lerp(StanceController.StanceTargetPosition, Vector3.zero, 5f * dt);
                            }

                            hasResetActiveAim = true;
                            hasResetHighReady = true;
                            hasResetLowReady = true;
                            hasResetShortStock = true;
                            StanceController.DoPistolStances(true, ref __instance, ref stanceRotation, dt, ref hasResetPistolPos, player, Logger, ref stanceSpeed, ref isResettingPistol, Plugin.ErgoDelta);
                        }
                        else if (!isPistol)
                        {
                            if ((!isInStance && allStancesReset) || (cancelBecauseSooting && !isInShootableStance) || Plugin.IsAiming || cancelStance || Plugin.IsBlindFiring)
                            {
                                StanceController.StanceBlender.Target = 0f;
                            }
                            else if (isInStance)
                            {
                                StanceController.StanceBlender.Target = 1f;
                            }

                            if ((!isInStance && allStancesReset) && !cancelBecauseSooting && !Plugin.IsAiming || (Plugin.IsBlindFiring))
                            {
                                StanceController.StanceTargetPosition = Vector3.Lerp(StanceController.StanceTargetPosition, Vector3.zero, 5f * dt);
                            }

                            hasResetPistolPos = true;
                            StanceController.DoRifleStances(Logger, player, firearmController, true, ref __instance, ref stanceRotation, dt, ref isResettingShortStock, ref hasResetShortStock, ref hasResetLowReady, ref hasResetActiveAim, ref hasResetHighReady, ref isResettingHighReady, ref isResettingLowReady, ref isResettingActiveAim, ref stanceSpeed, Plugin.ErgoDelta);
                        }

                        StanceController.HasResetActiveAim = hasResetActiveAim;
                        StanceController.HasResetHighReady = hasResetHighReady;
                        StanceController.HasResetLowReady = hasResetLowReady;
                        StanceController.HasResetShortStock = hasResetShortStock;
                        StanceController.HasResetPistolPos = hasResetPistolPos;
                    }
                    else if (player.IsAI)
                    {
                        Quaternion targetRotation = Quaternion.identity;
                        Quaternion currentRotation = (Quaternion)currentRotationField.GetValue(__instance);
                        aimSpeedField.SetValue(__instance, 1f);

                        Vector3 lowReadyTargetRotation = new Vector3(18.0f, 5.0f, -1.0f);
                        Quaternion lowReadyTargetQuaternion = Quaternion.Euler(lowReadyTargetRotation);
                        Vector3 lowReadyTargetPostion = new Vector3(0.06f, 0.04f, 0.0f);

                        Vector3 highReadyTargetRotation = new Vector3(-15.0f, 3.0f, 3.0f);
                        Quaternion highReadyTargetQuaternion = Quaternion.Euler(highReadyTargetRotation);
                        Vector3 highReadyTargetPostion = new Vector3(0.05f, 0.1f, -0.12f);

                        Vector3 activeAimTargetRotation = new Vector3(0.0f, -40.0f, 0.0f);
                        Quaternion activeAimTargetQuaternion = Quaternion.Euler(activeAimTargetRotation);
                        Vector3 activeAimTargetPostion = new Vector3(0.0f, 0.0f, 0.0f);

                        Vector3 shortStockTargetRotation = new Vector3(0.0f, -28.0f, 0.0f);
                        Quaternion shortStockTargetQuaternion = Quaternion.Euler(shortStockTargetRotation);
                        Vector3 shortStockTargetPostion = new Vector3(0.05f, 0.18f, -0.2f);

                        Vector3 tacPistolTargetRotation = new Vector3(0.0f, -20.0f, 0.0f);
                        Quaternion tacPistolTargetQuaternion = Quaternion.Euler(tacPistolTargetRotation);
                        Vector3 tacPistolTargetPosition = new Vector3(-0.1f, 0.1f, -0.05f);

                        Vector3 normalPistolTargetRotation = new Vector3(0f, -5.0f, 0.0f);
                        Quaternion normalPistolTargetQuaternion = Quaternion.Euler(normalPistolTargetRotation);
                        Vector3 normalPistolTargetPosition = new Vector3(-0.05f, 0.0f, 0.0f);

                        FaceShieldComponent fsComponent = player.FaceShieldObserver.Component;
                        NightVisionComponent nvgComponent = player.NightVisionObserver.Component;
                        bool nvgIsOn = nvgComponent != null && (nvgComponent.Togglable == null || nvgComponent.Togglable.On);
                        bool fsIsON = fsComponent != null && (fsComponent.Togglable == null || fsComponent.Togglable.On);

                        float lastDistance = player.AIData.BotOwner.AimingData.LastDist2Target;
                        Vector3 distanceVect = player.AIData.BotOwner.AimingData.RealTargetPoint - player.AIData.BotOwner.MyHead.position;
                        float realDistance = distanceVect.magnitude;

                        bool isTacBot = StanceController.botsToUseTacticalStances.Contains(player.AIData.BotOwner.Profile.Info.Settings.Role.ToString());
                        bool isPeace = player.AIData.BotOwner.Memory.IsPeace;
                        bool notShooting = !player.AIData.BotOwner.ShootData.Shooting && Time.time - player.AIData.BotOwner.ShootData.LastTriggerPressd > 15f;
                        bool isInStance = false;
                        float stanceSpeed = 1f;

                        ////peaceful positon//// (player.AIData.BotOwner.Memory.IsPeace == true && !StanceController.botsToUseTacticalStances.Contains(player.AIData.BotOwner.Profile.Info.Settings.Role.ToString()) && !player.IsSprintEnabled && !__instance.IsAiming && !player.AIData.BotOwner.ShootData.Shooting && (Time.time - player.AIData.BotOwner.ShootData.LastTriggerPressd) > 20f)
                        if (player.AIData.BotOwner.GetPlayer.MovementContext.BlindFire == 0)
                        {
                            if (isPeace && !player.IsSprintEnabled && player.MovementContext.StationaryWeapon == null && !__instance.IsAiming && !firearmController.IsInReloadOperation() && !firearmController.IsInventoryOpen() && !firearmController.IsInInteractionStrictCheck() && !firearmController.IsInSpawnOperation() && !firearmController.IsHandsProcessing()) // && player.AIData.BotOwner.WeaponManager.IsWeaponReady &&  player.AIData.BotOwner.WeaponManager.InIdleState()
                            {
                                isInStance = true;
                                player.HandsController.FirearmsAnimator.SetPatrol(true);
                            }
                            else
                            {
                                player.HandsController.FirearmsAnimator.SetPatrol(false);
                                if (firearmController.Item.WeapClass != "pistol")
                                {
                                    ////low ready//// 
                                    if (!isTacBot && !firearmController.IsInReloadOperation() && !player.IsSprintEnabled && !__instance.IsAiming && notShooting && (lastDistance >= 25f || lastDistance == 0f))    // (Time.time - player.AIData.BotOwner.Memory.LastEnemyTimeSeen) > 1f
                                    {
                                        isInStance = true;
                                        stanceSpeed = 4f * dt * 3f;
                                        targetRotation = lowReadyTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * lowReadyTargetPostion;
                                    }

                                    ////high ready////
                                    if (isTacBot && !firearmController.IsInReloadOperation() && !__instance.IsAiming && notShooting && (lastDistance >= 25f || lastDistance == 0f))
                                    {
                                        isInStance = true;
                                        player.BodyAnimatorCommon.SetFloat(PlayerAnimator.WEAPON_SIZE_MODIFIER_PARAM_HASH, 2);
                                        stanceSpeed = 4f * dt * 2.7f;
                                        targetRotation = highReadyTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * highReadyTargetPostion;
                                    }
                                    else
                                    {
                                        player.BodyAnimatorCommon.SetFloat(PlayerAnimator.WEAPON_SIZE_MODIFIER_PARAM_HASH, (float)firearmController.Item.CalculateCellSize().X);
                                    }

                                    ///active aim//// 
                                    if (isTacBot && (((nvgIsOn || fsIsON) && !player.IsSprintEnabled && !firearmController.IsInReloadOperation() && lastDistance < 25f && lastDistance > 2f && lastDistance != 0f) || (__instance.IsAiming && (nvgIsOn && __instance.CurrentScope.IsOptic || fsIsON))))
                                    {
                                        isInStance = true;
                                        stanceSpeed = 4f * dt * 1.5f;
                                        targetRotation = activeAimTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * activeAimTargetPostion;
                                    }

                                    ///short stock//// 
                                    if (isTacBot && !player.IsSprintEnabled && !firearmController.IsInReloadOperation() && lastDistance <= 2f && lastDistance != 0f)
                                    {
                                        isInStance = true;
                                        stanceSpeed = 4f * dt * 3f;
                                        targetRotation = shortStockTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * shortStockTargetPostion;
                                    }
                                }
                                else
                                {
                                    if (!isTacBot && !player.IsSprintEnabled && !__instance.IsAiming && notShooting)
                                    {
                                        isInStance = true;
                                        stanceSpeed = 4f * dt * 1.5f;
                                        targetRotation = normalPistolTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * normalPistolTargetPosition;
                                    }

                                    if (isTacBot && !player.IsSprintEnabled && !__instance.IsAiming && notShooting)
                                    {
                                        isInStance = true;
                                        stanceSpeed = 4f * dt * 1.5f;
                                        targetRotation = tacPistolTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * tacPistolTargetPosition;
                                    }
                                }
                            }

                            Quaternion rhs = Quaternion.Euler(pitch * overlappingBlindfire * blindfireRotation);
                            currentRotation = Quaternion.Slerp(currentRotation, __instance.IsAiming && !isInStance ? aimingQuat : isInStance ? targetRotation : Quaternion.identity, isInStance ? stanceSpeed : 8f * dt);
                            __instance.HandsContainer.WeaponRootAnim.SetPositionAndRotation(weaponPosition, weapRotation * rhs * currentRotation);
                            currentRotationField.SetValue(__instance, currentRotation);
                        }
                    }
                }
            }
        }
    }

    public class ApplyComplexRotationPatch : ModulePatch
    {
        private static FieldInfo aimSpeedField;
        private static FieldInfo fovScaleField;
        private static FieldInfo blindfireStrength;
        private static FieldInfo displacementStrField;
        private static FieldInfo blindfireRotationField;
        private static FieldInfo aimingQuatField;
        private static FieldInfo weapLocalRotationField;
        private static FieldInfo weapRotationField;
        private static FieldInfo isAimingField;
        private static PropertyInfo overlappingBlindfireField;

        private static bool hasResetActiveAim = true;
        private static bool hasResetLowReady = true;
        private static bool hasResetHighReady = true;
        private static bool hasResetShortStock = true;
        private static bool hasResetPistolPos = true;

        private static bool isResettingActiveAim = false;
        private static bool isResettingLowReady = false;
        private static bool isResettingHighReady = false;
        private static bool isResettingShortStock = false;
        private static bool isResettingPistol = false;

        private static Quaternion currentRotation = Quaternion.identity;
        private static Quaternion stanceRotation = Quaternion.identity;
        private static Vector3 mountWeapPosition = Vector3.zero;
        private static Vector3 currentRecoil = Vector3.zero;
        private static Vector3 targetRecoil = Vector3.zero;

        private static float stanceRotationSpeed = 1f;

        protected override MethodBase GetTargetMethod()
        {
            aimSpeedField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_aimingSpeed");
            fovScaleField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_compensatoryScale");
            blindfireStrength = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_blindfireStrength");
            displacementStrField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_displacementStr");
            blindfireRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_blindFireRotation");
            aimingQuatField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_targetScopeRotation");
            weapLocalRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_local");
            weapRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_temporaryRotation");
            isAimingField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_isAiming");
            overlappingBlindfireField = AccessTools.Property(typeof(EFT.Animations.ProceduralWeaponAnimation), "Single_3");

            return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("ApplyComplexRotation", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void Postfix(EFT.Animations.ProceduralWeaponAnimation __instance, float dt)
        {
            PlayerInterface playerInterface = (PlayerInterface)AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_firearmAnimationData").GetValue(__instance);
            if (playerInterface != null && playerInterface.Weapon != null)
            {
                Weapon weapon = playerInterface.Weapon;
                Player player = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(weapon.Owner.ID);
                if (player != null && player.IsYourPlayer)
                {
                    FirearmController fc = player.HandsController as FirearmController;

                    Plugin.IsInThirdPerson = false;

                    float aimSpeed = (float)aimSpeedField.GetValue(__instance);
                    float fovScale = (float)fovScaleField.GetValue(__instance);
                    float pitch = (float)blindfireStrength.GetValue(__instance);
                    float displacementStr = (float)displacementStrField.GetValue(__instance);
                    Vector3 blindFireRotation = (Vector3)blindfireRotationField.GetValue(__instance);
                    Quaternion aimingQuat = (Quaternion)aimingQuatField.GetValue(__instance);
                    Quaternion weapLocalRotation = (Quaternion)weapLocalRotationField.GetValue(__instance);
                    Quaternion weapRotation = (Quaternion)weapRotationField.GetValue(__instance);
                    bool isAiming = (bool)isAimingField.GetValue(__instance);
                    float overlappingBlindfire = (float)overlappingBlindfireField.GetValue(__instance);

                    Vector3 handsRotation = __instance.HandsContainer.HandsRotation.Get();
                    Vector3 sway = __instance.HandsContainer.SwaySpring.Value;
                    handsRotation += displacementStr * (isAiming ? __instance.AimingDisplacementStr : 1f) * new Vector3(sway.x, 0f, sway.z);
                    handsRotation += sway;
                    Vector3 rotationCenter = __instance._shouldMoveWeaponCloser ? __instance.HandsContainer.RotationCenterWoStock : __instance.HandsContainer.RotationCenter;
                    Vector3 weapRootPivot = __instance.HandsContainer.WeaponRootAnim.TransformPoint(rotationCenter);
                    Vector3 weaponWorldPos = __instance.HandsContainer.WeaponRootAnim.position;

                    StanceController.DoMounting(Logger, player, __instance, ref weaponWorldPos, ref mountWeapPosition, dt, __instance.HandsContainer.WeaponRoot.position);

                    __instance.DeferredRotateWithCustomOrder(__instance.HandsContainer.WeaponRootAnim, weapRootPivot, handsRotation);
                    Vector3 recoilVector = __instance.HandsContainer.Recoil.Get();
                    if (recoilVector.magnitude > 1E-45f)
                    {
                        if (fovScale < 1f && __instance.ShotNeedsFovAdjustments)
                        {
                            recoilVector.x = Mathf.Atan(Mathf.Tan(recoilVector.x * 0.017453292f) * fovScale) * 57.29578f;
                            recoilVector.z = Mathf.Atan(Mathf.Tan(recoilVector.z * 0.017453292f) * fovScale) * 57.29578f;
                        }
                        Vector3 recoilPivot = weaponWorldPos + weapRotation * __instance.HandsContainer.RecoilPivot;
                        __instance.DeferredRotate(__instance.HandsContainer.WeaponRootAnim, recoilPivot, weapRotation * recoilVector);
                    }

                    __instance.ApplyAimingAlignment(dt);

                    bool isPistol = weapon.WeapClass == "pistol";
                    bool allStancesReset = hasResetActiveAim && hasResetLowReady && hasResetHighReady && hasResetShortStock && hasResetPistolPos;
                    bool isInStance = StanceController.IsHighReady || StanceController.IsLowReady || StanceController.IsShortStock || StanceController.IsActiveAiming;
                    bool isInShootableStance = StanceController.IsShortStock || StanceController.IsActiveAiming || isPistol;
                    bool cancelBecauseShooting = StanceController.IsFiringStance && !isInShootableStance;
                    bool doStanceRotation = (isInStance || !allStancesReset || StanceController.PistolIsCompressed) && !cancelBecauseShooting;
                    bool cancelStance = (StanceController.CancelActiveAim && StanceController.IsActiveAiming) || (StanceController.CancelHighReady && StanceController.IsHighReady) || (StanceController.CancelLowReady && StanceController.IsLowReady) || (StanceController.CancelShortStock && StanceController.IsShortStock) || (StanceController.CancelPistolStance && StanceController.PistolIsCompressed);

                    currentRotation = Quaternion.Slerp(currentRotation, __instance.IsAiming && allStancesReset ? aimingQuat : doStanceRotation ? stanceRotation : Quaternion.identity, doStanceRotation ? stanceRotationSpeed * Plugin.StanceRotationSpeedMulti.Value : __instance.IsAiming ? 8f * aimSpeed * dt : 8f * dt);
                    Quaternion rhs = Quaternion.Euler(pitch * overlappingBlindfire * blindFireRotation);

                    if (Plugin.RecoilStandaloneIsPresent)
                    {
                        StanceController.DoCantedRecoil(ref targetRecoil, ref currentRecoil, ref weapRotation);
                    }

                    __instance.HandsContainer.WeaponRootAnim.SetPositionAndRotation(weaponWorldPos, weapRotation * rhs * currentRotation);

                    if (isPistol && !StanceController.IsPatrolStance)
                    {
                        if (StanceController.PistolIsCompressed && !Plugin.IsAiming && !isResettingPistol && !Plugin.IsBlindFiring)
                        {
                            StanceController.StanceBlender.Target = 1f;
                        }
                        else
                        {
                            StanceController.StanceBlender.Target = 0f;
                        }

                        if ((!StanceController.PistolIsCompressed && !Plugin.IsAiming && !isResettingPistol) || (Plugin.IsBlindFiring))
                        {
                            StanceController.StanceTargetPosition = Vector3.Lerp(StanceController.StanceTargetPosition, Vector3.zero, 5f * dt);
                        }
                        hasResetActiveAim = true;
                        hasResetHighReady = true;
                        hasResetLowReady = true;
                        hasResetShortStock = true;
                        StanceController.DoPistolStances(false, ref __instance, ref stanceRotation, dt, ref hasResetPistolPos, player, Logger, ref stanceRotationSpeed, ref isResettingPistol, Plugin.ErgoDelta);
                    }
                    else if (!isPistol)
                    {
                        if ((!isInStance && allStancesReset) || (cancelBecauseShooting && !isInShootableStance) || Plugin.IsAiming || cancelStance || Plugin.IsBlindFiring)
                        {
                            StanceController.StanceBlender.Target = 0f;
                        }
                        else if (isInStance)
                        {
                            StanceController.StanceBlender.Target = 1f;
                        }

                        if (((!isInStance && allStancesReset) && !cancelBecauseShooting && !Plugin.IsAiming) || (Plugin.IsBlindFiring))
                        {
                            StanceController.StanceTargetPosition = Vector3.Lerp(StanceController.StanceTargetPosition, Vector3.zero, 5f * dt);
                        }

                        hasResetPistolPos = true;
                        StanceController.DoRifleStances(Logger, player, fc, false, ref __instance, ref stanceRotation, dt, ref isResettingShortStock, ref hasResetShortStock, ref hasResetLowReady, ref hasResetActiveAim, ref hasResetHighReady, ref isResettingHighReady, ref isResettingLowReady, ref isResettingActiveAim, ref stanceRotationSpeed, Plugin.ErgoDelta);
                    }

                    StanceController.HasResetActiveAim = hasResetActiveAim;
                    StanceController.HasResetHighReady = hasResetHighReady;
                    StanceController.HasResetLowReady = hasResetLowReady;
                    StanceController.HasResetShortStock = hasResetShortStock;
                    StanceController.HasResetPistolPos = hasResetPistolPos;
                }
            }
        }
    }

    public class UpdateHipInaccuracyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.Player.FirearmController).GetMethod("UpdateHipInaccuracy", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(ref Player.FirearmController __instance)
        {
            Player player = (Player)AccessTools.Field(typeof(EFT.Player.FirearmController), "_player").GetValue(__instance);
            if (player.IsYourPlayer == true)
            {
                Plugin.BaseHipfireInaccuracy = player.ProceduralWeaponAnimation.Breath.HipPenalty;
            }
        }
    }
}
