using JetBrains.Annotations;
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace Ultraleap.Tracking.OpenXR
{
    /// <summary>
    /// Enables OpenXR hand-tracking support via the <see href="https://www.khronos.org/registry/OpenXR/specs/1.0/html/xrspec.html#XR_EXT_hand_tracking">XR_EXT_hand_tracking</see> OpenXR extension.
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(FeatureId = FeatureId,
        Version = "1.0.0",
        UiName = "Ultraleap Hand Tracking",
        Company = "Ultraleap",
        Desc = "Articulated hands using XR_EXT_hand_tracking",
        Category = FeatureCategory.Feature,
        Required = false,
        OpenxrExtensionStrings = "XR_EXT_hand_tracking",
        BuildTargetGroups = new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android }
    )]
#endif
    public class HandTrackingFeature : OpenXRFeature
    {
        [PublicAPI] public const string FeatureId = "com.ultraleap.tracking.openxr.feature.handtracking";

        private static class Native
        {
            private const string NativeDLL = "UltraleapOpenXRUnity";
            private const string NativePrefix = "Unity_HandTrackingFeature_";

            [StructLayout(LayoutKind.Sequential, Pack = 8)]
            internal readonly struct Result
            {
                public bool Succeeded => _result >= 0;
                public bool Failed => _result < 0;
                [CanBeNull] public string Message => _message;

                private readonly int _result;
                [MarshalAs(UnmanagedType.LPStr)] private readonly string _message;
            }

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "HookGetInstanceProcAddr", ExactSpelling = true)]
            internal static extern IntPtr HookGetInstanceProcAddr(IntPtr func);

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "OnInstanceCreate", ExactSpelling = true)]
            internal static extern bool OnInstanceCreate(ulong xrInstance);

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "OnInstanceDestroy", ExactSpelling = true)]
            internal static extern void OnInstanceDestroy(ulong xrInstance);

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "OnSystemChange", ExactSpelling = true)]
            internal static extern void OnSystemChange(ulong xrSystemId);

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "OnSessionCreate", ExactSpelling = true)]
            internal static extern void OnSessionCreate(ulong xrInstance);

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "OnSessionDestroy", ExactSpelling = true)]
            internal static extern void OnSessionDestroy(ulong xrInstance);

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "OnAppSpaceChange", ExactSpelling = true)]
            internal static extern void OnAppSpaceChange(ulong xrSpace);

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "CreateHandTrackers", ExactSpelling = true)]
            internal static extern Result CreateHandTrackers();

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "DestroyHandTrackers", ExactSpelling = true)]
            internal static extern Result DestroyHandTrackers();

            [DllImport(NativeDLL, EntryPoint = NativePrefix + "LocateHandJoints", ExactSpelling = true)]
            internal static extern Result LocateHandJoints(
                Handedness chirality,
                FrameTime frameTime,
                out uint isActive,
                [Out, NotNull, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)]
                HandJointLocation[] joints,
                uint jointCount);
        }

        protected override IntPtr HookGetInstanceProcAddr(IntPtr func) => Native.HookGetInstanceProcAddr(func);
        protected override void OnInstanceDestroy(ulong xrInstance) => Native.OnInstanceDestroy(xrInstance);
        protected override void OnSessionCreate(ulong xrSession) => Native.OnSessionCreate(xrSession);
        protected override void OnSessionDestroy(ulong xrSession) => Native.OnSessionDestroy(xrSession);
        protected override void OnSystemChange(ulong xrSystemId) => Native.OnSystemChange(xrSystemId);
        protected override void OnAppSpaceChange(ulong xrSpace) => Native.OnAppSpaceChange(xrSpace);

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            if (!OpenXRRuntime.IsExtensionEnabled("XR_EXT_hand_tracking"))
            {
                Debug.LogWarning("XR_EXT_hand_tracking is not enabled, disabling Hand Tracking");
                return false;
            }

            if (OpenXRRuntime.GetExtensionVersion("XR_EXT_hand_tracking") < 4)
            {
                Debug.LogWarning("XR_EXT_hand_tracking is not at least version 4, disabling Hand Tracking");
                return false;
            }

            return Native.OnInstanceCreate(xrInstance);
        }

        protected override void OnSubsystemStart()
        {
            Native.Result result = Native.CreateHandTrackers();
            if (result.Failed)
            {
                Debug.LogError(result.Message);
            }
        }

        protected override void OnSubsystemStop()
        {
            Native.Result result = Native.DestroyHandTrackers();
            if (result.Failed)
            {
                Debug.LogError(result.Message);
            }
        }

        internal bool LocateHandJoints(Handedness handedness, FrameTime frameTime, HandJointLocation[] handJointLocations)
        {
            Native.Result result = Native.LocateHandJoints(handedness, frameTime, out uint isActive, handJointLocations, (uint)handJointLocations.Length);
            if (result.Failed)
            {
                Debug.LogError(result.Message);
            }

            return result.Succeeded && Convert.ToBoolean(isActive);
        }

#if UNITY_EDITOR
        protected override void GetValidationChecks(List<ValidationRule> rules, BuildTargetGroup targetGroup)
        {
            // Check the active input handling supports New (for OpenXR) and Legacy (for Ultraleap Plugin support).
            rules.Add(new ValidationRule(this)
            {
                message = "Active Input Handling is not set to Both. While New is required for OpenXR, Both is recommended as the Ultraleap Unity Plugin does not fully support the New Input System.",
                error = false,
#if !ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
                checkPredicate = () => false,
#else
                checkPredicate = () => true,
#endif
                fixItAutomatic = false,
                fixItMessage = "Enable the Legacy Input Manager and replacement Input System together (Both)",
                fixIt = () => SettingsService.OpenProjectSettings("Project/Player"),
            });

            // Check that the Main camera has a suitable near clipping plane for hand-tracking.
            rules.Add(new ValidationRule(this)
            {
                message = "Main camera near clipping plane is further than recommend and tracked hands may show visual clipping artifacts.",
                error = false,
                checkPredicate = () => Camera.main == null || Camera.main.nearClipPlane <= 0.01,
                fixItAutomatic = true,
                fixItMessage = "Set main camera clipping plane to 0.01",
                fixIt = () =>
                {
                    if (Camera.main != null)
                    {
                        Camera.main.nearClipPlane = 0.01f;
                    }
                },
            });
        }
#endif
    }
}