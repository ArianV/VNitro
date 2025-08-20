using UnityEngine;
using MelonLoader;

namespace FoafNitroMod
{
    internal static class VNitroVFX
    {
        private const string LogTag = "[VNitroVFX]";

        private static Camera? _cam;
        private static float _baseFov = -1f;
        private static Quaternion _baseLocalRot;
        private static Vector3 _baseLocalPos;
        private static bool _haveBaseRot;
        private static bool _haveBasePos;

        private static bool _active;
        private static bool _boosting;
        private static float _boostIntensity;

        private static float _fovVel;
        private static float _rollDeg;
        private static float _rollVelDeg;
        private static Vector3 _shakeOffset;
        private static Vector3 _shakeVel;

        internal static void SetActive(bool enable)
        {
            _active = enable;
            if (!enable) Reset();
        }

        internal static void AttachToVehicle(GameObject go) => AttachToVehicle(go != null ? go.transform : null);
        internal static void AttachToVehicle(Transform? _)
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                MelonLogger.Msg($"{LogTag} No Camera.main found.");
                return;
            }

            if (_baseFov < 0f)
                _baseFov = _cam.fieldOfView;

            if (!_haveBaseRot)
            {
                _baseLocalRot = _cam.transform.localRotation;
                _haveBaseRot = true;
            }
            if (!_haveBasePos)
            {
                _baseLocalPos = _cam.transform.localPosition;
                _haveBasePos = true;
            }

            MelonLogger.Msg($"{LogTag} Camera bound. Base FOV={_baseFov:0.0}");
        }

        internal static void Detach() => Reset();

        internal static void ResetFov()
        {
            try
            {
                if (_cam != null && _baseFov > 0f)
                    _cam.fieldOfView = _baseFov;
            }
            catch { }
        }

        internal static void SetBoosting(bool boosting) => OnBoost(boosting, boosting ? 1f : 0f);

        internal static void OnBoost(bool pressed, float intensity01)
        {
            if (!_active) return;
            _boosting = pressed;
            _boostIntensity = Mathf.Clamp01(intensity01);
        }

        internal static void OnUpdate() => OnUpdate(Time.deltaTime);

        internal static void OnUpdate(float dt)
        {
            if (!_active) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            if (_baseFov > 0f)
            {
                float kick = Mathf.Lerp(8f, 14f, _boostIntensity);
                float targetFov = _boosting ? (_baseFov + kick) : _baseFov;
                _cam.fieldOfView = Mathf.SmoothDamp(_cam.fieldOfView, targetFov, ref _fovVel, 0.10f);
            }

            if (_haveBaseRot)
            {
                float targetRoll = _boosting ? Mathf.Lerp(0f, 3.0f, _boostIntensity) : 0f;

                if (_boosting)
                {
                    float t = Time.unscaledTime;
                    float wobble = Mathf.Sin(t * 13.0f) * 0.35f * _boostIntensity
                                 + Mathf.Sin(t * 7.7f + 1.1f) * 0.25f * _boostIntensity;
                    targetRoll += wobble;
                }

                _rollDeg = Mathf.SmoothDampAngle(_rollDeg, targetRoll, ref _rollVelDeg, 0.16f);
                _cam.transform.localRotation = _baseLocalRot * Quaternion.AngleAxis(_rollDeg, Vector3.forward);
            }

            if (_haveBasePos)
            {
                float targetAmp = _boosting ? Mathf.Lerp(0f, 0.025f, _boostIntensity) : 0f;
                float t = Time.unscaledTime;
                float nx = Mathf.PerlinNoise(t * 12.7f, 0.123f) - 0.5f;
                float ny = Mathf.PerlinNoise(0.987f, t * 10.9f) - 0.5f;
                Vector3 desired = new Vector3(nx, ny, 0f) * (targetAmp * 2f);

                _shakeOffset = Vector3.SmoothDamp(_shakeOffset, desired, ref _shakeVel, 0.08f, Mathf.Infinity, dt);

                _cam.transform.localPosition = _baseLocalPos + _shakeOffset;
            }
        }

        private static void Reset()
        {
            try
            {
                if (_cam != null)
                {
                    if (_baseFov > 0f) _cam.fieldOfView = _baseFov;
                    if (_haveBaseRot) _cam.transform.localRotation = _baseLocalRot;
                    if (_haveBasePos) _cam.transform.localPosition = _baseLocalPos;
                }
            }
            catch { }

            _boosting = false;
            _boostIntensity = 0f;
            _fovVel = 0f;
            _rollVelDeg = 0f;
            _rollDeg = 0f;
            _shakeOffset = Vector3.zero;
            _shakeVel = Vector3.zero;
        }
    }
}
