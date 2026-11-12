using System;
using UnityEngine;

namespace Gallop.Live
{
    // Colliders that make a character hide the stage lens flares behind it. The client attaches them to the bones
    // of the loaded model (Director.LoadCharaModel: body, head and tail parameter assets, in that order) and
    // LensFlareManager casts one ray per flare from the camera: a ray that hits any collider fades the flare out.
    //
    // Asset layout (JP client): ast_bdyXXXX_YY[_ZZ_H_S_B]_flare / ast_chrXXXX_YY[_hairNNN]_flare /
    // ast_tailXXXX_YY_flare, class Gallop.Live.CharacterFlareCollisionParameter (ScriptableObject).
    public class CharacterFlareCollisionParameter : ScriptableObject
    {
        [Serializable]
        public class BoneCollisionParameter
        {
            [Serializable]
            public class CollisionParameter
            {
                public enum Type { Sphere = 0, Box = 1, Capsule = 2 }
                public enum Direction { X_Axis = 0, Y_Axis = 1, Z_Axis = 2 }

                public Type _type;
                public float _radius;
                public float _height;
                public Vector3 _center;
                public Vector3 _size;
                public Direction _direction;
            }

            // Path of the bone relative to the model root or to any of its descendants
            // (resolved with Transform.Find, see CharacterFlareCollision.FindChild).
            public string _targetBoneName;
            public CollisionParameter[] _collisions;
        }

        public BoneCollisionParameter[] _boneCollisionParameters;
    }

    // Port of Gallop.Live.CharacterFlareCollision (JP 0x4EA8A70 AddCollisionToObject,
    // 0x4EA8D80 AddComponentFromCollisionParameter) and Gallop.GameObjectUtil.FindChild (0x4C698E0).
    public static class CharacterFlareCollision
    {
        // Returns false when there is nothing to add (null parameter asset).
        public static bool AddCollisionToObject(GameObject model, CharacterFlareCollisionParameter parameter)
        {
            if (model == null || parameter == null || parameter._boneCollisionParameters == null)
                return false;

            Transform root = model.transform;
            for (int i = 0; i < parameter._boneCollisionParameters.Length; i++)
            {
                var bone = parameter._boneCollisionParameters[i];
                if (bone == null || bone._collisions == null)
                    continue;
                Transform target = FindChild(bone._targetBoneName, root);
                if (target == null)
                    continue;
                for (int c = 0; c < bone._collisions.Length; c++)
                    AddComponentFromCollisionParameter(target.gameObject, bone._collisions[c]);
            }
            return true;
        }

        // GameObjectUtil.FindChild: Transform.Find(path) from the parent, then depth-first through the children.
        public static Transform FindChild(string path, Transform parent)
        {
            if (parent == null || string.IsNullOrEmpty(path))
                return null;
            Transform found = parent.Find(path);
            if (found != null)
                return found;
            for (int i = 0; i < parent.childCount; i++)
            {
                found = FindChild(path, parent.GetChild(i));
                if (found != null)
                    return found;
            }
            return null;
        }

        public static void AddComponentFromCollisionParameter(GameObject target,
            CharacterFlareCollisionParameter.BoneCollisionParameter.CollisionParameter parameter)
        {
            if (target == null || parameter == null)
                return;
            switch (parameter._type)
            {
                case CharacterFlareCollisionParameter.BoneCollisionParameter.CollisionParameter.Type.Sphere:
                {
                    var collider = target.AddComponent<SphereCollider>();
                    collider.center = parameter._center;
                    collider.radius = parameter._radius;
                    break;
                }
                case CharacterFlareCollisionParameter.BoneCollisionParameter.CollisionParameter.Type.Box:
                {
                    var collider = target.AddComponent<BoxCollider>();
                    collider.center = parameter._center;
                    collider.size = parameter._size;
                    break;
                }
                case CharacterFlareCollisionParameter.BoneCollisionParameter.CollisionParameter.Type.Capsule:
                {
                    var collider = target.AddComponent<CapsuleCollider>();
                    collider.center = parameter._center;
                    collider.radius = parameter._radius;
                    collider.height = parameter._height;
                    collider.direction = (int)parameter._direction;
                    break;
                }
            }
        }

        // Live.Director.LoadCharaModel: body, head and tail parameters of one character.
        public static void Attach(UmaContainerCharacter container)
        {
            if (container == null)
                return;
            AttachAsset(container, container.FlareBodyPath);
            AttachAsset(container, container.FlareHeadPath);
            AttachAsset(container, container.FlareTailPath);
        }

        private static void AttachAsset(UmaContainerCharacter container, string path)
        {
            if (string.IsNullOrEmpty(path) || UmaViewerMain.Instance == null || UmaViewerMain.Instance.AbList == null
                || !UmaViewerMain.Instance.AbList.ContainsKey(path))
                return;
            var parameter = LiveFlashResourceUtility.LoadOnView<CharacterFlareCollisionParameter>(path);
            AddCollisionToObject(container.gameObject, parameter);
        }
    }
}
