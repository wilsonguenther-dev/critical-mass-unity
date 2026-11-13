using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;

namespace CriticalMass.Runtime.Secrets
{
    /// <summary>
    /// The tiny hatchling of the easter eggs, drawn as HUD stickers (VectorShape, Void Navy outline): a mossy egg-shaped
    /// body with a cream belly, two eyes, stubby arms and feet. Poses: Idle (slow breathe), Waddle (rocks side to side;
    /// the caller moves it), Shrug (arms up twice, head tilt), Look (turns to the camera and blinks). Unscaled time, so
    /// it animates on menus and while paused; view only.
    /// </summary>
    public sealed class Hatchling : MonoBehaviour
    {
        public enum Pose { Idle, Waddle, Shrug, Look }

        public Pose Current = Pose.Idle;
        RectTransform _body, _armL, _armR, _eyes, _footL, _footR;
        VectorShape _lidL, _lidR;
        float _t;

        /// <summary>A hatchling in a size x size box at (x, y) of <paramref name="parent"/> (reference px, top-left origin).</summary>
        public static Hatchling Make(RectTransform parent, float x, float y, float size, Pose pose)
        {
            RectTransform root = MenuUi.Rt(parent, "Hatchling");
            MenuUi.Place(root, x, y, size, size);
            Hatchling h = root.gameObject.AddComponent<Hatchling>();
            h.Current = pose;
            h.Build(root, size / 96f);
            return h;
        }

        void Build(RectTransform root, float k)
        {
            Color body = CMTokens.Biomass.Color, cream = CMTokens.WarmCream.Color, navy = CMTokens.VoidNavy.Color, stone = CMTokens.Stone.Color;
            _footL = Part(root, "Foot L", ShapeKind.Circle, 22, 78, 24, 14, stone, k);
            _footR = Part(root, "Foot R", ShapeKind.Circle, 50, 78, 24, 14, stone, k);
            _armL = Part(root, "Arm L", ShapeKind.RoundRect, 8, 46, 12, 26, body, k);
            _armR = Part(root, "Arm R", ShapeKind.RoundRect, 76, 46, 12, 26, body, k);
            _body = Part(root, "Body", ShapeKind.Egg, 16, 8, 64, 76, body, k);
            Part(_body, "Belly", ShapeKind.Egg, 14, 30, 36, 40, cream, k, 64, 76).GetComponent<VectorShape>().RimPx = 0f;
            _eyes = MenuUi.Rt(_body, "Eyes");
            MenuUi.Place(_eyes, 0f, 0f, 64f * k, 76f * k);
            Eye(_eyes, 16, 18, navy, cream, k, out _lidL);
            Eye(_eyes, 36, 18, navy, cream, k, out _lidR);
        }

        static RectTransform Part(Transform parent, string name, ShapeKind kind, float x, float y, float w, float h, Color c, float k,
            float pw = 96f, float ph = 96f)
        {
            VectorShape s = MenuUi.Shape(parent, name, kind, x * k, y * k, w * k, h * k, c, kind == ShapeKind.RoundRect ? 6f * k : -1f);
            s.OutlinePx = 2.5f;
            s.RimPx = 0f;
            return s.rectTransform;
        }

        static void Eye(RectTransform parent, float x, float y, Color navy, Color cream, float k, out VectorShape lid)
        {
            VectorShape e = MenuUi.Shape(parent, "Eye", ShapeKind.Circle, x * k, y * k, 12f * k, 14f * k, navy, -1f);
            e.OutlinePx = 0f;
            e.RimPx = 0f;
            VectorShape glint = MenuUi.Shape(e.transform, "Glint", ShapeKind.Circle, 3f * k, 3f * k, 4f * k, 4f * k, cream, -1f);
            glint.OutlinePx = 0f;
            glint.RimPx = 0f;
            lid = MenuUi.Shape(parent, "Lid", ShapeKind.RoundRect, x * k - 1f, y * k - 1f, 14f * k, 16f * k, CMTokens.Biomass.Color, 4f * k);
            lid.OutlinePx = 0f;
            lid.RimPx = 0f;
            lid.gameObject.SetActive(false);
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float rock = 0f, armL = 0f, armR = 0f, bodyLift = 0f, footL = 0f, footR = 0f;
            bool blink = false;
            switch (Current)
            {
                case Pose.Idle:
                    bodyLift = 1.5f * Mathf.Sin(_t * Mathf.PI);
                    blink = (_t % 3.2f) > 3.05f;
                    break;
                case Pose.Waddle:
                    rock = 10f * Mathf.Sin(_t * Mathf.PI * 4f);
                    footL = Mathf.Max(0f, Mathf.Sin(_t * Mathf.PI * 4f)) * 5f;
                    footR = Mathf.Max(0f, -Mathf.Sin(_t * Mathf.PI * 4f)) * 5f;
                    armL = -15f * Mathf.Sin(_t * Mathf.PI * 4f);
                    armR = armL;
                    break;
                case Pose.Shrug:
                {
                    float cycle = _t % 1.6f;
                    float up = cycle < 0.25f ? cycle / 0.25f : cycle < 0.9f ? 1f : Mathf.Max(0f, 1f - (cycle - 0.9f) / 0.25f);
                    up = up * up * (3f - 2f * up);
                    armL = -55f * up;
                    armR = 55f * up;
                    bodyLift = 3f * up;
                    rock = 6f * up;
                    break;
                }
                case Pose.Look:
                    bodyLift = 1f * Mathf.Sin(_t * Mathf.PI * 0.8f);
                    blink = (_t % 2.4f) > 2.25f;
                    break;
            }
            _body.localRotation = Quaternion.Euler(0f, 0f, rock);
            _body.localPosition = new Vector3(_body.localPosition.x, BodyY + bodyLift, 0f);
            _armL.localRotation = Quaternion.Euler(0f, 0f, armL);
            _armR.localRotation = Quaternion.Euler(0f, 0f, armR);
            _footL.localPosition = new Vector3(_footL.localPosition.x, FootY + footL, 0f);
            _footR.localPosition = new Vector3(_footR.localPosition.x, FootY + footR, 0f);
            if (_lidL.gameObject.activeSelf != blink)
            {
                _lidL.gameObject.SetActive(blink);
                _lidR.gameObject.SetActive(blink);
            }
        }

        float BodyY => _bodyY ?? (_bodyY = _body.localPosition.y).Value;
        float FootY => _footY ?? (_footY = _footL.localPosition.y).Value;
        float? _bodyY, _footY;
    }
}
