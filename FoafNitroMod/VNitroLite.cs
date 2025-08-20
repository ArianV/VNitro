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
        private object? _current; // keep as object to avoid direct game-assembly ref
        private Rigidbody? _rb;

        // Rigidbody-based fallback boost
        private const float RbAccelBoost = 9f;
        private const float RbVelNudgePerSec = 5f;
        private const float MaxAssistSpeed = 25f;

        // Nitro system
        private float _nitro01 = 1f;               // 0..1
        private const float DrainPerSec = 0.70f;   // while boosting
        private const float RechargePerSec = 0.18f; // while not boosting
        internal const float MinBoostPct = 0.30f;  // must have ≥20% to START boosting

        // Cooldown after empty
        private const float RegenDelaySeconds = 3f;
        private float _regenDelayTimer = 0f;       // counts down while at 0 before recharge starts

        // Latch: once boosting starts, it continues until key released or tank hits 0
        private bool _isBoosting = false;

        private bool _loggedFallback;
        private readonly KeyCode _boostKey = KeyCode.LeftShift;

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg("[VNitro] init…");

            _harmony = new HarmonyLib.Harmony("com.foaf.vnitrolite");

            PatchByName("Il2CppScheduleOne.Vehicles.LandVehicle", "EnterVehicle", nameof(EnterVehicle_Postfix));
            PatchByName("Il2CppScheduleOne.Vehicles.LandVehicle", "ExitVehicle", nameof(ExitVehicle_Postfix));
            PatchByName("Il2CppScheduleOne.Vehicles.LandVehicle", "FixedUpdate", nameof(FixedUpdate_Postfix));

            LoggerInstance.Msg("[VNitro] Loaded. Hold LeftShift to boost.");
        }

        public override void OnUpdate()
        {
            bool inVehicle = _current != null;

            // Keep HUD/Audio/VFX in sync with vehicle presence
            VNitroHud.SetActive(inVehicle);
            VNitroAudio.SetActive(inVehicle);
            VNitroVFX.SetActive(inVehicle);
            VNitroVFX.OnUpdate(); // handles particle & FOV smoothing each frame

            if (!inVehicle)
            {
                // Hard stop when leaving vehicle
                if (_isBoosting) _isBoosting = false;
                VNitroAudio.SetBoosting(false);
                VNitroVFX.SetBoosting(false);
                VNitroHud.SetBoosting(false);
                VNitroHud.SetFill01(_nitro01);
                return;
            }

            float dt = Time.deltaTime;
            bool wantsBoost = Input.GetKey(_boostKey);

            // START condition: only if not already boosting AND we have ≥ 20%
            if (!_isBoosting && wantsBoost && _nitro01 >= MinBoostPct)
                _isBoosting = true;

            // STOP conditions: key released OR tank empty
            if (_isBoosting && (!wantsBoost || _nitro01 <= 0f))
                _isBoosting = false;

            // Update tank
            if (_isBoosting)
            {
                _nitro01 = Mathf.Max(0f, _nitro01 - DrainPerSec * dt);
                if (_nitro01 <= 0f)
                {
                    _isBoosting = false;
                    _regenDelayTimer = RegenDelaySeconds; // start cooldown when we hit empty
                    LoggerInstance.Msg("[VNitro] Tank empty — starting 3s cooldown.");
                }
            }
            else
            {
                // Not boosting: recharge, but only after cooldown if we are empty
                if (_nitro01 <= 0f && _regenDelayTimer > 0f)
                {
                    _regenDelayTimer -= dt;
                    if (_regenDelayTimer < 0f) _regenDelayTimer = 0f;
                    // hold at 0 during cooldown
                }
                else
                {
                    _nitro01 = Mathf.Min(1f, _nitro01 + RechargePerSec * dt);
                }
            }

            // Drive audio + HUD + VFX
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
            LoggerInstance.Msg($"[VNitro] Patched {typeName}.{methodName} (postfix)");
        }

        public static void EnterVehicle_Postfix(object __instance)
        {
            var self = Instance;
            if (self == null || __instance == null) return;

            self._current = __instance;

            var comp = __instance as Component;
            self._rb = comp != null ? comp.gameObject.GetComponentInParent<Rigidbody>() : null;

            // Attach hiss & VFX to this vehicle (3D + mixer copy if VehicleSound exists)
            if (comp != null)
            {
                var go = comp.gameObject;
                VNitroAudio.AttachToVehicle(go);
                VNitroVFX.AttachToVehicle(go);
                VNitroVFX.SetActive(true);
            }

            self.LoggerInstance.Msg("Entered vehicle: " + __instance.GetType().FullName);
            VNitroHud.SetActive(true);
            VNitroAudio.SetActive(true);
        }

        public static void ExitVehicle_Postfix(object __instance)
        {
            var self = Instance;
            if (self == null) return;

            self.LoggerInstance.Msg("Exited vehicle: " + __instance?.GetType().FullName);

            self._isBoosting = false; // unlatch
            self._regenDelayTimer = 0f; // cancel cooldown on exit (optional)

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

            // Physics boost only while latched
            if (!self._isBoosting) return;

            var rb = self._rb;
            if (rb == null) return;

            if (!self._loggedFallback)
            {
                self.LoggerInstance.Msg("[VNitro] Fallback path active: applying Rigidbody acceleration.");
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
