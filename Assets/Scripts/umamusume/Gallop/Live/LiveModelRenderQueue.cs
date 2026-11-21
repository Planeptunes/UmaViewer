using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    /// <summary>
    /// Port of the game's per-frame character render-queue assignment.
    ///
    ///   Director.UpdateModelRenderQueue (JP 0x7ffd59205fc0): sorts the live characters by
    ///   ModelController.GetDepthOrder (JP 0x7ffd59671f50: (camera.worldToCameraMatrix *
    ///   headTransform.position).z, i.e. the head's view-space z) ASCENDING - rank 0 is the FARTHEST
    ///   character - and calls ModelController.UpdateRenderQueue (JP 0x7ffd59685790) with the rank.
    ///
    ///   ModelController.UpdateRenderQueue assigns Material.renderQueue per part
    ///   (CharaPartsHolder.PartsData material sets):
    ///     off = (19 - rank) * 4                       [JP: ~rank * 4 + 0x50]
    ///     GetFaceRenderQueue / GetHeadObjRenderQueue (JP 0x7ffd596723f0)             = 1996 - off
    ///       (face skin material, face prop materials, head-obj materials)
    ///     GetFaceFrontRenderQueue (JP 0x7ffd5795e990)                                = 1997 - off
    ///       (MangameController - the glasses)
    ///     GetHairRenderQueue / GetHeadOnlyRenderQueue (JP 0x7ffd5795e910)            = 1998 - off
    ///       (hair material + hair props; and ALL head/face materials when the model has no hair)
    ///     GetEyeRenderQueue / GetMayuRenderQueue / GetJawRenderQueue (JP 0x7ffd5795e900) = 1999 - off
    ///       (eye / mayu / jaw / mouth materials, and the dedicated MayuMaterial/JawMaterial which are
    ///        re-assigned after the head loop and therefore always keep 1999 - off)
    ///     GetBodyRenderQueue / GetTailRenderQueue / GetAttachmentRenderQueue /
    ///     GetBodyBillboardRenderQueue / GetHairBillboardRenderQueue (JP 0x7ffd59670ad0) = 2000 (constant)
    ///     GetAlphaMeshRenderQueue (JP 0x7ffd59670aa0) = RenderQueueAlpha (2300) + rank * 4
    ///       (AlphaMeshInfo.Material; AlphaMeshInfo.RenderQueueOffset is added per mesh and
    ///        AlphaMeshInfo.SecondMaterial takes the group's opaque queue instead)
    ///
    ///   The cheek (CheekController.SetRenderQueue) and tear (TearController) materials are NOT part of
    ///   UpdateRenderQueue and are left untouched here too.
    ///
    /// VIEWER ADAPTATIONS (reported):
    /// - The viewer has no CharaPartsHolder.PartsData: each material is classified like the game's model
    ///   load does (renderer/material name + shader family, see Classify) and the game's queue formula is
    ///   applied to the resulting bucket.
    /// - AlphaMeshInfo.RenderQueueOffset (per-mesh queue offset) is not modeled: the viewer has no
    ///   AlphaMeshInfo grouping, so every alpha material of a character shares the band base. The old
    ///   viewer formula (2300 + (19 - order) * 16, order = closest-first) misread the JP argument and is
    ///   replaced by the game's exact 2300 + rank * 4.
    /// - The viewer no longer forces _ZWrite on alpha materials (the game does not write _ZWrite here): the old
    ///   viewer-only tweak made translucent parts write depth, so the stage sky / effects (queue 3000) failed
    ///   the depth test behind them and the camera clear colour (green) showed through (live 1195 f5041).
    /// - Director.UpdateModelRenderQueue also calls one no-argument virtual per model before
    ///   UpdateRenderQueue (vtable +0x538, likely ModelController.UpdateSortingOrder @0x7ffd596862f0
    ///   which zeroes Renderer.sortingOrder). Not ported: the viewer never sets sortingOrder on the
    ///   character renderers.
    /// </summary>
    public static class LiveModelRenderQueue
    {
        private const int RENDER_QUEUE_ALPHA = 2300; // ModelController.get_RenderQueueAlpha
        private const int MAX_ORDER = 19;            // UpdateRenderQueue: off = (19 - rank) * 4

        // ModelController.Get*RenderQueue band bases (the JP formulas subtract `off` from these).
        private const int QUEUE_FACE = 1996;       // GetFaceRenderQueue / GetHeadObjRenderQueue
        private const int QUEUE_FACE_FRONT = 1997; // GetFaceFrontRenderQueue (glasses)
        private const int QUEUE_HAIR = 1998;       // GetHairRenderQueue / GetHeadOnlyRenderQueue
        private const int QUEUE_HEAD_OTHER = 1999; // GetEyeRenderQueue / GetMayuRenderQueue / GetJawRenderQueue
        private const int QUEUE_BODY = 2000;       // GetBody/GetTail/GetAttachment/Get*BillboardRenderQueue

        // The game's part buckets of UpdateRenderQueue.
        private enum PartKind
        {
            Skip,        // not managed by UpdateRenderQueue (cheek, tear, effects, unknown)
            Face,        // face skin + face props + head obj   -> 1996 - off
            FaceFront,   // glasses (MangameController)         -> 1997 - off
            Hair,        // hair + hair props                   -> 1998 - off
            HeadOther,   // eye / mayu / jaw / mouth            -> 1999 - off
            Body,        // body / tail / attachment / billboards -> 2000
            Alpha,       // alpha meshes                        -> 2300 + rank * 4
        }

        private sealed class CharaQueues
        {
            public readonly List<Material> Face = new List<Material>();
            public readonly List<Material> Hair = new List<Material>();
            public readonly List<Material> HeadOther = new List<Material>();
            public readonly List<Material> Body = new List<Material>();
            public readonly List<Material> Alpha = new List<Material>();
        }

        private static readonly Dictionary<UmaContainerCharacter, CharaQueues> _byChara =
            new Dictionary<UmaContainerCharacter, CharaQueues>();
        private static readonly List<KeyValuePair<UmaContainerCharacter, float>> _sorted =
            new List<KeyValuePair<UmaContainerCharacter, float>>();
        private static float _nextScan;
        private static Director _director;   // al cambiar de live se vacian las caches

        private static bool IsCharaShader(Shader s)
        {
            return s != null && s.name.StartsWith("Gallop/3D/Chara/");
        }

        private static PartKind Classify(Renderer renderer, Material material, bool hasHair)
        {
            if (material == null)
                return PartKind.Skip;
            Shader shader = material.shader;
            if (!IsCharaShader(shader))
                return PartKind.Skip;

            string shaderName = shader.name;
            string matName = material.name;
            string rName = renderer != null ? renderer.name : "";

            // Cheek (CheekController) and tear (TearController) materials are not touched by the game's
            // UpdateRenderQueue.
            if (rName.Contains("Cheek") || matName.Contains("Cheek") || rName.Contains("tearmesh"))
                return PartKind.Skip;

            // Alpha character parts (existing alpha-mesh path): the Alpha* shader families.
            if (shaderName.Contains("Alpha"))
                return PartKind.Alpha;

            // Mayu and jaw keep 1999 - off even on hairless models (the game re-assigns the dedicated
            // MayuMaterial/JawMaterial after the head loop). Head pieces are identified by name like the
            // viewer's head loader already does.
            bool mayuOrJaw = matName.StartsWith("M_Mayu") || matName.Contains("mayu") ||
                             matName.StartsWith("M_Jaw") || matName.Contains("jaw");
            bool eyeOrMouthName = matName.StartsWith("M_Eye") || matName.Contains("eye") ||
                                  matName.StartsWith("M_Mouth") || matName.Contains("mouth");

            // Shader families (the segment after "Gallop/3D/Chara/"): ToonFace*/NolineToonFace*/... are
            // the face shaders, ToonHair*/... the hair shaders, ToonEye*/ToonMayu*/... the eye/mayu
            // shaders and Toon(/ToonColor/NolineToon/DitherToon/HologramToon/ScreenMappingToon) the
            // body/tail/attachment shaders (tools 09-shaders INDEX).
            string family = shaderName.Substring("Gallop/3D/Chara/".Length);
            int slash = family.IndexOf('/');
            if (slash >= 0) family = family.Substring(0, slash);

            bool faceShader = family.EndsWith("ToonFace");
            bool hairShader = family.EndsWith("ToonHair") || family.EndsWith("ToonBehindHair");
            bool eyeOrMayuShader = family.EndsWith("ToonEye") || family.EndsWith("ToonMayu");

            // Game UpdateRenderQueue: without hair, everything reached through HeadMaterialArray /
            // FacePropMaterialArray (eye, mouth, face skin) takes the GetHeadOnlyRenderQueue band
            // (1998 - off, the same bucket as hair); only the dedicated Mayu/Jaw materials keep
            // 1999 - off. The part identity (name) wins over the shader family, as in the game.
            if (mayuOrJaw)
                return PartKind.HeadOther;
            if (eyeOrMouthName)
                return hasHair ? PartKind.HeadOther : PartKind.Hair;
            if (hairShader)
                return PartKind.Hair;
            if (faceShader)
                return hasHair ? PartKind.Face : PartKind.Hair;
            if (eyeOrMayuShader)
                return hasHair ? PartKind.HeadOther : PartKind.Hair;
            // Body / tail / attachment shader families -> GetBodyRenderQueue & co (constant 2000).
            if (family.StartsWith("Toon") || family.StartsWith("NolineToon") ||
                family.StartsWith("DitherToon") || family.StartsWith("DitherNolineToon") ||
                family.StartsWith("HologramToon") || family.StartsWith("ScreenMappingToon"))
                return PartKind.Body;
            return PartKind.Skip;
        }

        private static void Scan(Director director)
        {
            _byChara.Clear();
            foreach (var chara in director.CharaContainerScript)
            {
                if (chara == null) continue;
                var queues = new CharaQueues();
                // Game PartsData.IsHair: the model has a hair object.
                bool hasHair = chara.Hair != null;
                foreach (var r in chara.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var materials = r.sharedMaterials;
                    if (materials == null) continue;
                    foreach (var m in materials)
                    {
                        PartKind kind = Classify(r, m, hasHair);
                        switch (kind)
                        {
                            case PartKind.Face:
                                if (!queues.Face.Contains(m)) queues.Face.Add(m);
                                break;
                            case PartKind.Hair:
                                if (!queues.Hair.Contains(m)) queues.Hair.Add(m);
                                break;
                            case PartKind.HeadOther:
                                if (!queues.HeadOther.Contains(m)) queues.HeadOther.Add(m);
                                break;
                            case PartKind.Body:
                                if (!queues.Body.Contains(m)) queues.Body.Add(m);
                                break;
                            case PartKind.Alpha:
                                if (!queues.Alpha.Contains(m)) queues.Alpha.Add(m);
                                break;
                        }
                    }
                }
                if (queues.Face.Count > 0 || queues.Hair.Count > 0 || queues.HeadOther.Count > 0 ||
                    queues.Body.Count > 0 || queues.Alpha.Count > 0)
                    _byChara[chara] = queues;
            }
        }

        /// <summary>Lo llama Director.LateUpdate en cada frame del live.</summary>
        public static void Update(Director director)
        {
            if (director == null || director.CharaContainerScript == null) return;

            if (director != _director)
            {
                _director = director;
                _byChara.Clear();
                _nextScan = 0f;
            }

            if (Time.realtimeSinceStartup >= _nextScan)
            {
                _nextScan = Time.realtimeSinceStartup + 0.5f;
                Scan(director);
            }
            if (_byChara.Count == 0) return;

            Camera cam = director.MainRenderCamera != null ? director.MainRenderCamera : Camera.main;
            if (cam == null) return;

            // Director.UpdateModelRenderQueue (JP 0x7ffd59205fc0) + ModelController.GetDepthOrder
            // (JP 0x7ffd59671f50): the head transform's view-space z, sorted ASCENDING (rank 0 = farthest).
            Matrix4x4 worldToCamera = cam.worldToCameraMatrix;
            _sorted.Clear();
            foreach (var kv in _byChara)
            {
                if (kv.Key == null) continue;
                Transform head = kv.Key.Head != null ? kv.Key.Head.transform : kv.Key.transform;
                float depth = worldToCamera.MultiplyPoint(head.position).z;
                _sorted.Add(new KeyValuePair<UmaContainerCharacter, float>(kv.Key, depth));
            }
            _sorted.Sort((a, b) => a.Value.CompareTo(b.Value));

            for (int rank = 0; rank < _sorted.Count; rank++)
            {
                int off = (MAX_ORDER - rank) * 4;                 // UpdateRenderQueue: ~rank * 4 + 0x50
                CharaQueues queues = _byChara[_sorted[rank].Key];
                Apply(queues.Face, QUEUE_FACE - off);
                Apply(queues.Hair, QUEUE_HAIR - off);
                Apply(queues.HeadOther, QUEUE_HEAD_OTHER - off);
                Apply(queues.Body, QUEUE_BODY);
                // GetAlphaMeshRenderQueue(rank) = RenderQueueAlpha + rank * 4 (the per-mesh
                // AlphaMeshInfo.RenderQueueOffset of the game is not modeled, see the class comment).
                ApplyAlpha(queues.Alpha, RENDER_QUEUE_ALPHA + rank * 4);
            }
        }

        private static void Apply(List<Material> materials, int queue)
        {
            for (int i = 0; i < materials.Count; i++)
            {
                Material m = materials[i];
                if (m == null) continue;
                if (m.renderQueue != queue) m.renderQueue = queue;
            }
        }

        private static void ApplyAlpha(List<Material> materials, int queue)
        {
            for (int i = 0; i < materials.Count; i++)
            {
                Material m = materials[i];
                if (m == null) continue;
                if (m.renderQueue != queue) m.renderQueue = queue;
            }
        }
    }
}
