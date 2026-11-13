using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.View.Art;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// The owner's field report (2026-11-12): every character and the Kaiju walked backwards, face on the trailing side.
    /// Each model is checked on its own (never assumed per family): posed on its Idle clip, its painted face (decal
    /// vertices relative to the head bone) and its toes (foot to toes bone) must point along the root's +Z once
    /// ModelSpec.ArtYawDeg is added, and the full view rotation for sim facing 0 must put the face toward Unity +X.
    /// </summary>
    public sealed class ModelFacingTests
    {
        const float Tolerance = 45f;

        static IEnumerable<string> Keys() => new[]
        {
            ArtMapping.Warrior, ArtMapping.Miner, ArtMapping.Farmer, ArtMapping.KaijuHatchling, ArtMapping.KaijuAdult,
        };

        static ModelFacing.Reading Read(string key, bool pose)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            Assert.IsNotNull(model, spec.AssetPath + " is not imported");
            GameObject inst = Object.Instantiate(model);
            Texture2D decal = null;
            try
            {
                System.Func<Vector2, float> alpha = null;
                if (spec.FaceTexture != null && spec.FaceSlot < 0)
                {
                    decal = new Texture2D(2, 2);
                    Assert.IsTrue(decal.LoadImage(File.ReadAllBytes(spec.FaceTexture)), spec.FaceTexture);
                    Texture2D d = decal;
                    alpha = uv => d.GetPixelBilinear(uv.x, uv.y).a;
                }
                if (pose)
                {
                    ArtEntry e = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath)?.Find(key);
                    AnimationClip idle = e?.Clip(ArtMapping.Idle);
                    if (idle != null) idle.SampleAnimation(inst, 0f);
                }
                return ModelFacing.Measure(inst, spec, alpha);
            }
            finally
            {
                Object.DestroyImmediate(inst);
                if (decal != null) Object.DestroyImmediate(decal);
            }
        }

        [TestCaseSource(nameof(Keys))]
        public void TheFaceLeadsAlongTheActorsFacing(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            ModelFacing.Reading bind = Read(key, false), idle = Read(key, true);
            string where = key + ": bind " + bind + "; idle " + idle + "; ArtYawDeg " + spec.ArtYawDeg;
            Debug.Log("[ModelFacing] " + where);
            Assert.IsTrue(idle.HasFace || idle.HasToes, where + ": no face decal and no toes to measure");
            if (idle.HasFace)
                Assert.Less(Mathf.Abs(ModelFacing.Wrap(idle.FaceYaw + spec.ArtYawDeg)), Tolerance, where + ": the face points away from the facing");
            if (idle.HasToes)
                Assert.Less(Mathf.Abs(ModelFacing.Wrap(idle.ToesYaw + spec.ArtYawDeg)), Tolerance, where + ": the toes point away from the facing");

            // The whole chain for sim facing 0 (+X): view rotation, then the art yaw, must send the face to Unity +X.
            float yaw = idle.HasFace ? idle.FaceYaw : idle.ToesYaw;
            Vector3 faceInModel = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Quaternion view = Quaternion.Euler(0f, SimSpace.YawDeg(0.0), 0f) * Quaternion.Euler(0f, spec.ArtYawDeg, 0f);
            Assert.Greater(Vector3.Dot(view * faceInModel, Vector3.right), 0.7f, where + ": sim facing +X does not put the face toward +X");
        }
    }
}
