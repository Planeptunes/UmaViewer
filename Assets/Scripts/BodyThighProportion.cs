using System;
using UnityEngine;

/// <summary>
/// Adapts the thighs of the body of character B (unique costume) to the head character A ("Other Characters"/Favorites).
/// Thigh_L/R is scaled only across its own axis (the Thigh->Knee direction keeps its length, so the leg does not
/// change) and the Knee is scaled back by the inverse, so the lower leg and the foot keep their size. With the hip sizes
/// of both characters the factor is (hip A / hip B) / height ratio (the whole model is already scaled by the height
/// ratio); without them it falls back to the DB `shape` index step.
/// </summary>
public static class BodyThighProportion
{
    private const float ShapePerStep = 0.11f;   //index fallback: linear size change per `shape` index step
    private const float MinFactor = 0.6f, MaxFactor = 1.4f;

    public static void Apply(UmaContainerCharacter c, string bodyShape, string headShape,
        CharaEntry bodyChara, CharaEntry headChara, float heightRatio)
    {
        if (c == null) return;

        float g;
        string source;
        if (heightRatio > 0.01f && StudentThreeSizes.TryGet(bodyChara, out var sBody) && StudentThreeSizes.TryGet(headChara, out var sHead))
        {
            g = Mathf.Clamp(sHead.H / sBody.H / heightRatio, MinFactor, MaxFactor);
            source = $"hips {sBody.H}->{sHead.H}, height x{heightRatio:F3}";
        }
        else
        {
            if (!int.TryParse(bodyShape, out int bs) || !int.TryParse(headShape, out int hs)) return;
            g = Mathf.Clamp(1f + ShapePerStep * (hs - bs), MinFactor, MaxFactor);
            source = "DB shape index (no three sizes for one of the characters)";
        }
        if (Mathf.Abs(g - 1f) < 1e-4f) return;

        try
        {
            Transform[] all = c.GetComponentsInChildren<Transform>(true);
            int legs = 0;
            if (ScaleLeg(c, all, "Thigh_L", "Knee_L", "Ankle_L", g)) legs++;
            if (ScaleLeg(c, all, "Thigh_R", "Knee_R", "Ankle_R", g)) legs++;
            Debug.Log($"[BodyThighProportion] thighs x{g:F2} ({legs} legs). Source: {source}.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BodyThighProportion] Failed, the body keeps its own thighs: {ex}");
        }
    }

    //The Animator rewrites the local scale of Thigh/Knee every frame (measured: a scale set by hand is back to 1 one frame
    //later), so the scale is re-applied after the animation in LateUpdate, before the physics reads the bones.
    [DefaultExecutionOrder(-100)]
    private class LegScaleDriver : MonoBehaviour
    {
        public Transform Thigh, Knee;
        public Vector3 ThighBase, KneeBase, ThighFactor, KneeFactor;

        private void LateUpdate()
        {
            Thigh.localScale = Vector3.Scale(ThighBase, ThighFactor);
            Knee.localScale = Vector3.Scale(KneeBase, KneeFactor);
        }
    }

    private static bool ScaleLeg(UmaContainerCharacter c, Transform[] all, string thighName, string kneeName, string ankleName, float g)
    {
        Transform thigh = Find(all, thighName), knee = Find(all, kneeName), ankle = Find(all, ankleName);
        if (thigh == null || knee == null || ankle == null) return false;

        var driver = c.gameObject.AddComponent<LegScaleDriver>();
        driver.Thigh = thigh; driver.Knee = knee;
        driver.ThighBase = thigh.localScale; driver.KneeBase = knee.localScale;
        driver.ThighFactor = AcrossAxis(thigh.InverseTransformPoint(knee.position), g);
        driver.KneeFactor = AcrossAxis(knee.InverseTransformPoint(ankle.position), 1f / g);
        return true;
    }

    //Scale vector that is `s` on the two axes across a bone and 1 along it. The bone's own axis is the local axis
    //that points to its child (the dominant component of the child's local position).
    private static Vector3 AcrossAxis(Vector3 toChildLocal, float s)
    {
        float ax = Mathf.Abs(toChildLocal.x), ay = Mathf.Abs(toChildLocal.y), az = Mathf.Abs(toChildLocal.z);
        Vector3 v = new Vector3(s, s, s);
        if (ax >= ay && ax >= az) v.x = 1f;
        else if (ay >= az) v.y = 1f;
        else v.z = 1f;
        return v;
    }

    private static Transform Find(Transform[] all, string name)
    {
        foreach (Transform t in all) if (t != null && t.name == name) return t;
        return null;
    }
}
