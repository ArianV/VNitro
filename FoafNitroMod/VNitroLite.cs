using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(FoafNitroMod.VNitroLiteMod), "VNitro", "0.4.2", "foaf")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace FoafNitroMod
{
    public class VNitroLiteMod : MelonMod
    {
        public static VNitroLiteMod? Instance { get; private set; }

        private HarmonyLib.Harmony? _harmony;
        private object? _current;
        private Rigidbody? _rb;

        private const float RbAccelBoost = 9f;
        private const float RbVelNudgePerSec = 5f;
        private const float MaxAssistSpeed = 25f;

        private float _nitro01 = 1f;
        private const float DrainPerSec = 0.70f;
        private const float RechargePerSec = 0.18f;
        internal const float MinBoostPct = 0.30f;

        private const float RegenDelaySeconds = 3f;
        private float _regenDelayTimer = 0f;

        private bool _isBoosting = false;

        private bool _loggedFallback;
        private readonly KeyCode _boostKey = KeyCode.LeftShift;

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg("[VNitro] initializing");

            _harmony = new HarmonyLib.Harmony("com.foaf.vnitrolite");

            PatchByName("Il2CppScheduleOne.Vehicles.LandVehicle", "EnterVehicle", nameof(EnterVehicle_Postfix));
            PatchByName("Il2CppScheduleOne.Vehicles.LandVehicle", "ExitVehicle", nameof(ExitVehicle_Postfix));
            PatchByName("Il2CppScheduleOne.Vehicles.LandVehicle", "FixedUpdate", nameof(FixedUpdate_Postfix));

            LoggerInstance.Msg("[VNitro] Loaded. Hold LeftShift to boost.");
        }

        public override void OnUpdate()
        {
            bool inVehicle = _current != null;

            VNitroHud.SetActive(inVehicle);
            VNitroAudio.SetActive(inVehicle);
            VNitroVFX.SetActive(inVehicle);
            VNitroVFX.OnUpdate();

            if (!inVehicle)
            {
                if (_isBoosting) _isBoosting = false;
                VNitroAudio.SetBoosting(false);
                VNitroVFX.SetBoosting(false);
                VNitroHud.SetBoosting(false);
                VNitroHud.SetFill01(_nitro01);
                return;
            }

            float dt = Time.deltaTime;
            bool wantsBoost = Input.GetKey(_boostKey);

            if (!_isBoosting && wantsBoost && _nitro01 >= MinBoostPct)
                _isBoosting = true;

            if (_isBoosting && (!wantsBoost || _nitro01 <= 0f))
                _isBoosting = false;

            if (_isBoosting)
            {
                _nitro01 = Mathf.Max(0f, _nitro01 - DrainPerSec * dt);
                if (_nitro01 <= 0f)
                {
                    _isBoosting = false;
                    _regenDelayTimer = RegenDelaySeconds;
                    // LoggerInstance.Msg("[VNitro] Tank empty — starting 3s cooldown.");
                }
            }
            else
            {
                if (_nitro01 <= 0f && _regenDelayTimer > 0f)
                {
                    _regenDelayTimer -= dt;
                    if (_regenDelayTimer < 0f) _regenDelayTimer = 0f;
                }
                else
                {
                    _nitro01 = Mathf.Min(1f, _nitro01 + RechargePerSec * dt);
                }
            }

            VNitroAudio.SetBoosting(_isBoosting);
            VNitroVFX.SetBoosting(_isBoosting);
            VNitroHud.SetBoosting(_isBoosting);
            VNitroHud.SetFill01(_nitro01);
        }

        public override void OnGUI()
        {
            VNitroHud.Draw();
        }

        private void PatchByName(string typeName, string methodName, string postfixName)
        {
            var t = AccessTools.TypeByName(typeName);
            if (t == null)
            {
                LoggerInstance.Warning($"[VNitro] Type not found: {typeName}");
                return;
            }

            var original = AccessTools.Method(t, methodName);
            var postfix = AccessTools.Method(typeof(VNitroLiteMod), postfixName);

            if (original == null || postfix == null)
            {
                LoggerInstance.Warning($"[VNitro] Could not patch {typeName}.{methodName}");
                return;
            }

            _harmony!.Patch(original, postfix: new HarmonyMethod(postfix));
            // LoggerInstance.Msg($"[VNitro] Patched {typeName}.{methodName} (postfix)");
        }

        public static void EnterVehicle_Postfix(object __instance)
        {
            var self = Instance;
            if (self == null || __instance == null) return;

            self._current = __instance;

            var comp = __instance as Component;
            self._rb = comp != null ? comp.gameObject.GetComponentInParent<Rigidbody>() : null;

            if (comp != null)
            {
                var go = comp.gameObject;
                VNitroAudio.AttachToVehicle(go);
                VNitroVFX.AttachToVehicle(go);
                VNitroVFX.SetActive(true);
            }

            // self.LoggerInstance.Msg("Entered vehicle: " + __instance.GetType().FullName);
            VNitroHud.SetActive(true);
            VNitroAudio.SetActive(true);
        }

        public static void ExitVehicle_Postfix(object __instance)
        {
            var self = Instance;
            if (self == null) return;

            // self.LoggerInstance.Msg("Exited vehicle: " + __instance?.GetType().FullName);

            self._isBoosting = false;
            self._regenDelayTimer = 0f;

            VNitroAudio.SetBoosting(false);
            VNitroAudio.SetActive(false);
            VNitroAudio.Detach();

            VNitroVFX.SetBoosting(false);
            VNitroVFX.SetActive(false);
            VNitroVFX.Detach();
            VNitroVFX.ResetFov();

            VNitroHud.SetActive(false);

            self._current = null;
            self._rb = null;
            self._loggedFallback = false;
        }

        public static void FixedUpdate_Postfix(object __instance)
        {
            var self = Instance;
            if (self == null) return;
            if (self._current == null || !ReferenceEquals(self._current, __instance)) return;

            if (!self._isBoosting) return;

            var rb = self._rb;
            if (rb == null) return;

            if (!self._loggedFallback)
            {
                // self.LoggerInstance.Msg("[VNitro] Fallback path active: applying Rigidbody acceleration.");
                self._loggedFallback = true;
            }

            Vector3 fwd = rb.transform.forward;
            rb.AddForce(fwd * RbAccelBoost, ForceMode.Acceleration);

            float dt = Time.fixedDeltaTime;
            var v = rb.velocity;
            if (v.magnitude < MaxAssistSpeed)
                rb.velocity = v + fwd * (RbVelNudgePerSec * dt);
        }
    }
}
