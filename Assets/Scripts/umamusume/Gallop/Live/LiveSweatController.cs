using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Port of the client's sweat drops of a character's head:
    //   Sweat.SweatLocator (Plugins.dll)            -> LiveSweatLocator
    //   Gallop.SweatController / GallopSweatController, CharacterObject.UpdateSweatLocator(Info)
    //                                               -> LiveSweatController
    // The client hangs one `pfb_chrXXXX_YY_sweatlocator` prefab (children sweat_01..sweat_05) under the
    // head bone and puts one `pfb_sweat_0000` drop model under each child. The sweatLocatorList track
    // shows / hides / moves / fades those models (see LiveTimelineControl.AlterUpdate_SweatLocator).

    // Plugins struct Sweat.SweatLocator.SweatLocatorInfo (size 0x50): per drop.
    public struct LiveSweatLocatorInfo
    {
        public Color color;
        public float colorPower;
        public float strength;
        public float threshHold;
        public Renderer renderer;
        public Material[] MaterialArray;
        public GameObject gameObject;     // drop model instance (the object the timeline shows / hides)
        public Transform transform;       // drop model transform (the timeline moves / rotates it)
        public Transform parentTransform; // the sweat_0N node (origin of the World offsets)
        public int ResourceId;

        // Client ResetParameter (JP 0x204D790): white, power 1, strength 1, threshold 0.
        public void ResetParameter()
        {
            color = Color.white;
            colorPower = 1f;
            strength = 1f;
            threshHold = 0f;
        }
    }

    // Sweat.SweatLocator
    public class LiveSweatLocator
    {
        // Shader property names (client SweatMaterialProperty*ID, resolved from the drop shader
        // Gallop/3D/Chara/AttachableEffectFresnel).
        private static readonly int ColorID = Shader.PropertyToID("_Color");
        private static readonly int ColorPowerID = Shader.PropertyToID("_ColorPower");
        private static readonly int StrengthID = Shader.PropertyToID("_Strength");
        private static readonly int ThreshHoldID = Shader.PropertyToID("_Threshold");

        public int LocatorNum;
        public GameObject Root;
        public LiveSweatLocatorInfo[] InfoArray;
        public float Alpha = 1f;

        // Initialize(locatorPrefab, sweatModelPrefab, shader, resourceId): instantiates the locator prefab and one
        // drop model under each child; every drop starts with the ResetParameter values.
        public void Initialize(GameObject locatorPrefab, GameObject sweatModelPrefab, Shader shader, int resourceId)
        {
            Root = Object.Instantiate(locatorPrefab);
            Transform rootTransform = Root.transform;
            LocatorNum = rootTransform.childCount;
            InfoArray = new LiveSweatLocatorInfo[LocatorNum];
            for (int i = 0; i < LocatorNum; i++)
                SetSweatModel(i, sweatModelPrefab, shader, resourceId);
        }

        // SetSweatModel(index, model, shader, resourceId)
        public void SetSweatModel(int index, GameObject sweatModelPrefab, Shader shader, int resourceId)
        {
            Transform parent = Root.transform.GetChild(index);
            if (InfoArray[index].gameObject != null)
            {
                DestroyMaterial(ref InfoArray[index].MaterialArray);
                Object.Destroy(InfoArray[index].gameObject);
            }

            GameObject model = Object.Instantiate(sweatModelPrefab, parent, false);
            Renderer renderer = model.GetComponent<Renderer>();
            Material[] materials = renderer != null ? renderer.materials : new Material[0];
            if (shader != null)
                SetShader(shader, materials);

            LiveSweatLocatorInfo info = default(LiveSweatLocatorInfo);
            info.ResetParameter();
            info.renderer = renderer;
            info.MaterialArray = materials;
            info.gameObject = model;
            info.transform = model.transform;
            info.parentTransform = parent;
            info.ResourceId = resourceId;
            InfoArray[index] = info;
        }

        public static void SetShader(Shader shader, Material[] materials)
        {
            if (materials == null)
                return;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null)
                    materials[i].shader = shader;
        }

        public static void DestroyMaterial(ref Material[] materials)
        {
            if (materials == null)
                return;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null)
                    Object.Destroy(materials[i]);
            materials = null;
        }

        // AttachTo(parent): SetParent(parent, worldPositionStays = false).
        public void AttachTo(Transform parent)
        {
            if (Root != null)
                Root.transform.SetParent(parent, false);
        }

        public void Enable()
        {
            if (Root != null)
                Root.SetActive(true);
        }

        public void Disable()
        {
            if (Root != null)
                Root.SetActive(false);
        }

        public void SetSweatAlpha(float alpha)
        {
            Alpha = alpha;
        }

        // UpdateMaterial (JP 0x204EAE0): per drop, per material: _Color, _ColorPower (colorPower * alpha),
        // _Strength, _Threshold.
        public void UpdateMaterial()
        {
            if (InfoArray == null)
                return;
            for (int i = 0; i < InfoArray.Length; i++)
            {
                Material[] materials = InfoArray[i].MaterialArray;
                if (materials == null)
                    continue;
                float colorPower = InfoArray[i].colorPower * Alpha;
                for (int j = 0; j < materials.Length; j++)
                {
                    Material material = materials[j];
                    if (material == null)
                        continue;
                    material.SetColor(ColorID, InfoArray[i].color);
                    material.SetFloat(ColorPowerID, colorPower);
                    material.SetFloat(StrengthID, InfoArray[i].strength);
                    material.SetFloat(ThreshHoldID, InfoArray[i].threshHold);
                }
            }
        }

        public void Release()
        {
            if (InfoArray != null)
                for (int i = 0; i < InfoArray.Length; i++)
                    DestroyMaterial(ref InfoArray[i].MaterialArray);
            if (Root != null)
                Object.Destroy(Root);
            Root = null;
            InfoArray = null;
        }
    }

    // Frees the drop materials with the locator (the client releases them in OnDestroy).
    internal sealed class LiveSweatLocatorOwner : MonoBehaviour
    {
        public LiveSweatLocator Locator;

        private void OnDestroy()
        {
            if (Locator == null || Locator.InfoArray == null)
                return;
            for (int i = 0; i < Locator.InfoArray.Length; i++)
                LiveSweatLocator.DestroyMaterial(ref Locator.InfoArray[i].MaterialArray);
        }
    }

    // GallopSweatController + SweatController: one per character.
    public class LiveSweatController
    {
        // ResourcePath.GetSweatModelResourcePath(resourceId)
        public static string GetSweatModelPath(int resourceId)
        {
            return $"3d/effect/sweat/pfb_sweat_{resourceId:0000}";
        }

        // ResourcePath.GetSweatLocatorResourcePath: non-mob = (charaId, headModelSubId); mob adds the face kind.
        public static string GetSweatLocatorPath(UmaContainerCharacter container)
        {
            if (container == null || container.SweatHeadCharaId <= 0)
                return null;
            string chara = container.SweatHeadCharaId.ToString("0000");
            string sub = string.IsNullOrEmpty(container.SweatHeadSubId) ? "00" : container.SweatHeadSubId;
            if (container.IsMob)
            {
                if (string.IsNullOrEmpty(container.SweatMobFaceId))
                    return null;
                string face = container.SweatMobFaceId.PadLeft(3, '0');
                return $"3d/chara/sweat/chr{chara}_{sub}/pfb_chr{chara}_{sub}_face{face}_sweatlocator";
            }
            return $"3d/chara/sweat/chr{chara}_{sub}/pfb_chr{chara}_{sub}_sweatlocator";
        }

        public LiveSweatLocator Locator { get; private set; }

        // Builds the controller of one character (Gallop.ModelLoader.AttachSweatToModelController +
        // GallopSweatController.OnInitialize): locator under the Head bone, every drop hidden, drops on the
        // character layer, materials written once. Returns null when the head has no sweatlocator prefab.
        public static LiveSweatController Create(UmaContainerCharacter container)
        {
            string locatorPath = GetSweatLocatorPath(container);
            if (locatorPath == null)
                return null;

            // ModelLoader.AttachSweatToModelController: nothing is attached when the head model omits the sweat.
            if (!container.IsMob)
            {
                string omitPath = CharaHeadModelOmitSetting.GetPath(container.SweatHeadCharaId, container.SweatHeadSubId);
                if (UmaViewerMain.Instance != null && UmaViewerMain.Instance.AbList != null
                    && UmaViewerMain.Instance.AbList.ContainsKey(omitPath))
                {
                    var omit = LiveFlashResourceUtility.LoadOnView<CharaHeadModelOmitSetting>(omitPath);
                    if (omit != null && omit.IsSweat)
                        return null;
                }
            }

            GameObject locatorPrefab = LiveFlashResourceUtility.LoadOnView<GameObject>(locatorPath);
            GameObject modelPrefab = LiveFlashResourceUtility.LoadOnView<GameObject>(GetSweatModelPath(0));
            if (locatorPrefab == null || modelPrefab == null)
            {
                Debug.LogWarning("[LiveSweatController] Sweat prefab not found: " +
                    (locatorPrefab == null ? locatorPath : GetSweatModelPath(0)));
                return null;
            }

            Transform head = container.HeadBone != null ? container.HeadBone.transform : null;
            if (head == null)
            {
                foreach (Transform t in container.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Head") { head = t; break; }
            }
            if (head == null)
                return null;

            var controller = new LiveSweatController { Locator = new LiveSweatLocator() };
            // The locator prefab is instantiated first, then every drop model (shader = null: the drop keeps the
            // material of its prefab).
            controller.Locator.Initialize(locatorPrefab, modelPrefab, null, 0);
            controller.Locator.Root.AddComponent<LiveSweatLocatorOwner>().Locator = controller.Locator;
            controller.Locator.AttachTo(head);
            controller.SetLayer(ResolveCharacterLayer(container));
            controller.SetShowLocatorAll(false);
            // AttachSweatToModelController: IsVisible = visibility of the model.
            controller.Locator.Root.SetActive(true);
            controller.UpdateMaterial();
            container.InvalidateMirrorLayerCache();
            return controller;
        }

        // Layer of the character's own renderers (the sweat drops follow the model layer in the client:
        // SetLayer after every model layer change).
        private static int ResolveCharacterLayer(UmaContainerCharacter container)
        {
            int charaLayers = GraphicSettings.GetCullingLayer(GraphicSettings.LayerIndex.LayerCHAR)
                | GraphicSettings.GetCullingLayer(GraphicSettings.LayerIndex.LayerCharacter3D_0)
                | GraphicSettings.GetCullingLayer(GraphicSettings.LayerIndex.LayerCharacter3D_1);
            foreach (Renderer r in container.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || (charaLayers & (1 << r.gameObject.layer)) == 0)
                    continue;
                return r.gameObject.layer;
            }
            return GraphicSettings.GetLayer(GraphicSettings.LayerIndex.LayerCHAR);
        }

        // GallopSweatController.SetLayer: SetLayerRecursively on every drop model.
        public void SetLayer(int layer)
        {
            if (Locator == null || Locator.InfoArray == null || layer < 0)
                return;
            for (int i = 0; i < Locator.InfoArray.Length; i++)
            {
                GameObject go = Locator.InfoArray[i].gameObject;
                if (go == null)
                    continue;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = layer;
            }
        }

        // GallopSweatController.SetShowLocatorAll
        public void SetShowLocatorAll(bool show)
        {
            if (Locator == null || Locator.InfoArray == null)
                return;
            for (int i = 0; i < Locator.InfoArray.Length; i++)
                if (Locator.InfoArray[i].gameObject != null)
                    Locator.InfoArray[i].gameObject.SetActive(show);
        }

        // GallopSweatController.Update: alpha (1 unless the character fade drives it) + UpdateSweatMaterial.
        public void UpdateMaterial()
        {
            if (Locator == null)
                return;
            Locator.SetSweatAlpha(1f);
            Locator.UpdateMaterial();
        }

        // CharacterObject.UpdateSweatLocator: every drop gets its LocatorInfo of the key, then the controller
        // updates its materials.
        public void UpdateSweatLocator(SweatLocatorUpdateInfo updateInfo)
        {
            if (Locator == null || Locator.InfoArray == null)
                return;
            int count = Mathf.Min(Locator.LocatorNum, updateInfo.locatorInfo.Length);
            for (int i = 0; i < count; i++)
                UpdateSweatLocatorInfo(ref Locator.InfoArray[i], ref updateInfo.locatorInfo[i], updateInfo.alpha);
            UpdateMaterial();
        }

        // CharacterObject.UpdateSweatLocatorInfo (JP 0x7ffd5922f520)
        private static void UpdateSweatLocatorInfo(ref LiveSweatLocatorInfo info,
            ref LiveTimelineKeySweatLocatorData.LocatorInfo locatorInfo, float alpha)
        {
            GameObject go = info.gameObject;
            if (go == null)
                return;
            if (go.activeSelf != locatorInfo.isVisible)
                go.SetActive(locatorInfo.isVisible);
            if (go.activeSelf)
            {
                if (locatorInfo.offsetType == LiveTimelineKeySweatLocatorData.LocatorType.Local)
                {
                    info.transform.localPosition = locatorInfo.offset;
                }
                else if (locatorInfo.offsetType == LiveTimelineKeySweatLocatorData.LocatorType.World)
                {
                    if (info.parentTransform != null)
                        info.transform.position = info.parentTransform.position + locatorInfo.offset;
                }
                info.transform.localRotation = Quaternion.Euler(locatorInfo.offsetAngle);
            }
            info.colorPower = alpha;
        }
    }
}
