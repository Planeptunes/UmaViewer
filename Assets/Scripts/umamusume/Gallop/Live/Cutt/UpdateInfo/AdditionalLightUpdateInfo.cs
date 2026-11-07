using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Per-frame state of one additional stage light (AdditionalLightList track).
    // Layout mirrors the client's AdditionalLightUpdateInfo (header 0x10).
    public struct AdditionalLightUpdateInfo
    {
        // Fields
        public int Index; // 0x10 index of the light inside AdditionalLightComponent._addLightList
        public Vector3 Position; // 0x14
        public Vector3 Rotate; // 0x20 Euler angles
        public bool IsEnable; // 0x2C taken from the current key (never interpolated)
        public LightType Type; // 0x30
        public float Range; // 0x34
        public int SpotAngle; // 0x38
        public int IndirectMultiplier; // 0x3C Light.bounceIntensity
        public LightShadows ShadowType; // 0x40
        public float Strength; // 0x44 Light.shadowStrength
        public float Bias; // 0x48 Light.shadowBias
        public float NormalBias; // 0x4C Light.shadowNormalBias
        public float NearPlane; // 0x50 Light.shadowNearPlane
    }

    public delegate void AdditionalLightUpdateInfoDelegate(ref AdditionalLightUpdateInfo updateInfo);

    public delegate void AdditionalLightCountUpdateInfoDelegate(int count);
}
