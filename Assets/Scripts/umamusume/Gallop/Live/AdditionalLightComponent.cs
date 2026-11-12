using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Real stage lights driven by the AdditionalLight timeline track (client's
    // Gallop.Live.AdditionalLightComponent). Serialized identity and fields match the stage
    // prefabs: _addLightList holds the authored "SceneLight" objects and UpdateAdditionalLightCount
    // grows/shrinks the list at runtime with lights created under _root.
    public class AdditionalLightComponent : MonoBehaviour
    {
        [SerializeField] private List<Light> _addLightList; // 0x10
        [SerializeField] private Transform _root; // 0x18
        [SerializeField] private int _lightMask; // 0x20

        private const string CreatedLightName = "SceneLight";

        // True while any real stage light is switched on by its track. The game only lets the characters cast real
        // shadows while the AdditionalLight is on (probe, Live 1093: bodies/faces/hair/tails of the characters with
        // FormationOffset IsCastShadow cast at frames 3885-4185 where the light is enabled, and none at 3189 / 5382
        // where it is disabled).
        public bool IsAnyLightEnabled
        {
            get
            {
                if (_addLightList == null)
                    return false;
                for (int i = 0; i < _addLightList.Count; i++)
                {
                    Light light = _addLightList[i];
                    if (light != null && light.enabled)
                        return true;
                }
                return false;
            }
        }

        public IReadOnlyList<Light> LightList
        {
            get { return _addLightList; }
        }

        public void Initialize(Transform root, int lightMask)
        {
            _root = root;
            _lightMask = lightMask;
            if (_addLightList == null)
                _addLightList = new List<Light>();
        }

        // Client's UpdateAdditionalLight: enables and places one light per key. IsEnable is taken
        // from the current key only; the rest of the parameters arrive already interpolated.
        public void UpdateAdditionalLight(ref AdditionalLightUpdateInfo info)
        {
            if (_addLightList == null)
                return;
            if (info.Index < 0 || info.Index >= _addLightList.Count)
                return;

            Light light = _addLightList[info.Index];
            if (light == null)
                return;

            light.enabled = info.IsEnable;
            if (!light.enabled)
                return;

            light.transform.SetPositionAndRotation(info.Position, Quaternion.Euler(info.Rotate));
            light.type = info.Type;
            light.range = info.Range;
            light.spotAngle = info.SpotAngle;
            light.bounceIntensity = info.IndirectMultiplier;
            light.shadows = info.ShadowType;
            light.shadowStrength = info.Strength;
            light.shadowBias = info.Bias;
            light.shadowNormalBias = info.NormalBias;
            light.shadowNearPlane = info.NearPlane;
        }

        // Client's UpdateAdditionalLightCount: keeps _addLightList.Count == count (the number of
        // AdditionalLightList groups) and raises QualitySettings.pixelLightCount accordingly.
        // Extra lights are created disabled under _root with cullingMask = _lightMask.
        public void UpdateAdditionalLightCount(int count)
        {
            if (_addLightList == null)
                _addLightList = new List<Light>();

            int diff = _addLightList.Count - count;
            if (diff == 0)
                return;

            QualitySettings.pixelLightCount = count;

            if (diff < 0)
            {
                for (int i = 0; i < -diff; i++)
                {
                    GameObject go = new GameObject(CreatedLightName);
                    if (_root != null)
                        go.transform.SetParent(_root, true);
                    Light light = go.AddComponent<Light>();
                    light.enabled = false;
                    light.cullingMask = _lightMask;
                    _addLightList.Add(light);
                }
            }
            else
            {
                for (int i = 0; i < diff; i++)
                {
                    Light light = _addLightList[_addLightList.Count - 1 - i];
                    if (light != null)
                        Destroy(light.gameObject, 0f);
                }
                _addLightList.RemoveRange(_addLightList.Count - diff, diff);
            }
        }
    }
}
