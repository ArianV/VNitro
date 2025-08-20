using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace FoafNitroMod
{
    internal static class VNitroAudio
    {
        private static GameObject? _go;
        private static AudioSource? _src;
        private static AudioClip? _clip;
        private static bool _active;
        private static bool _boosting;

        internal static void Initialize()
        {
            if (_go != null) return;

            _go = new GameObject("[VNitroAudio]");
            UnityEngine.Object.DontDestroyOnLoad(_go);

            _src = _go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = true;
            _src.volume = 0.75f;

            MelonCoroutines.Start(CoLoadClip());
        }

        internal static void SetActive(bool on)
        {
            _active = on;
            if (!on)
            {
                _boosting = false;
                if (_src != null && _src.isPlaying) _src.Stop();
            }
        }

        internal static void SetBoosting(bool on)
        {
            _boosting = on && _active && _clip != null;
            if (_src == null) return;

            if (_boosting)
            {
                if (!_src.isPlaying && _clip != null)
                {
                    _src.clip = _clip;
                    _src.Play();
                }
            }
            else
            {
                if (_src.isPlaying) _src.Stop();
            }
        }

        internal static void AttachToVehicle(GameObject? vehicleRoot)
        {
            try
            {
                if (_go == null) Initialize();
                if (_go == null || vehicleRoot == null || _src == null) return;

                _go.transform.SetParent(vehicleRoot.transform, false);
                _go.transform.localPosition = Vector3.zero;

                if (!TryCopyFromVehicleSound(vehicleRoot))
                {
                    _src.spatialBlend = 0f;
                    _src.dopplerLevel = 0f;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[VNitro] AttachToVehicle failed: {ex.Message}");
            }
        }

        internal static void Detach()
        {
            try
            {
                if (_go != null) _go.transform.SetParent(null, false);
                if (_src != null && _src.isPlaying) _src.Stop();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[VNitro] Detach failed: {ex.Message}");
            }
        }

        private static IEnumerator CoLoadClip()
        {
            string baseDir = MelonEnvironment.MelonBaseDirectory;
            string audioDir = Path.Combine(baseDir, "Mods", "VNitro", "Audio");

            string audioDirAlt = Path.Combine(baseDir, "Mods", "VNitro", "audio");

            string? chosen = null;
            string candidate1 = Path.Combine(audioDir, "nitro.wav");
            string candidate2 = Path.Combine(audioDirAlt, "nitro.wav");

            if (File.Exists(candidate1)) chosen = candidate1;
            else if (File.Exists(candidate2)) chosen = candidate2;
            else if (Directory.Exists(audioDir))
            {
                var wavs = Directory.GetFiles(audioDir, "*.wav", SearchOption.TopDirectoryOnly);
                if (wavs.Length > 0) chosen = wavs[0];
            }
            else if (Directory.Exists(audioDirAlt))
            {
                var wavs = Directory.GetFiles(audioDirAlt, "*.wav", SearchOption.TopDirectoryOnly);
                if (wavs.Length > 0) chosen = wavs[0];
            }

            if (string.IsNullOrEmpty(chosen))
            {
                MelonLogger.Warning($"[VNitro] No WAV found. Place nitro.wav in: {audioDir}");
                yield break;
            }

            try
            {
                _clip = LoadWavClip(chosen);
                if (_clip != null)
                    MelonLogger.Msg($"[VNitro] Loaded hiss WAV: {chosen}");
                else
                    MelonLogger.Warning($"[VNitro] WAV load returned null: {chosen}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[VNitro] WAV load failed: {chosen}\n{ex.Message}");
            }

            yield break;
        }
        private static AudioClip LoadWavClip(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int p = 0;

            string riff = ReadString(data, ref p, 4);
            int riffSize = ReadInt(data, ref p);
            string wave = ReadString(data, ref p, 4);
            if (riff != "RIFF" || wave != "WAVE")
                throw new Exception("Not a WAVE/RIFF file.");

            short channels = 0;
            int sampleRate = 0;
            short bitsPerSample = 0;
            ushort formatTag = 0;
            byte[]? pcm = null;

            while (p + 8 <= data.Length)
            {
                string id = ReadString(data, ref p, 4);
                int size = ReadInt(data, ref p);
                if (p + size > data.Length) break;

                if (id == "fmt ")
                {
                    formatTag = (ushort)ReadShort(data, ref p);
                    channels = ReadShort(data, ref p);
                    sampleRate = ReadInt(data, ref p);
                    int byteRate = ReadInt(data, ref p);
                    short blockAlign = ReadShort(data, ref p);
                    bitsPerSample = ReadShort(data, ref p);

                    int fmtLeft = size - 16;
                    if (fmtLeft > 0) p += fmtLeft; 
                }
                else if (id == "data")
                {
                    pcm = new byte[size];
                    Buffer.BlockCopy(data, p, pcm, 0, size);
                    p += size;
                }
                else
                {
                    p += size;
                }

                if (pcm != null && channels > 0 && sampleRate > 0 && (formatTag == 1 || formatTag == 3))
                    break;
            }

            if (pcm == null) throw new Exception("Missing data chunk.");
            if (channels < 1 || channels > 2) throw new Exception("Unsupported channel count: " + channels);
            if (sampleRate <= 0) throw new Exception("Invalid sample rate.");
            if (!(formatTag == 1 || formatTag == 3)) throw new Exception("Unsupported WAV format tag: " + formatTag + " (use PCM or float).");

            float[] samples;

            if (formatTag == 1)
            {
                if (bitsPerSample == 16)
                {
                    int count = pcm.Length / 2;
                    samples = new float[count];
                    int ip = 0;
                    for (int i = 0; i < count; i++)
                    {
                        short s = (short)(pcm[ip] | (pcm[ip + 1] << 8));
                        samples[i] = s / 32768f;
                        ip += 2;
                    }
                }
                else if (bitsPerSample == 24)
                {
                    int count = pcm.Length / 3;
                    samples = new float[count];
                    int ip = 0;
                    for (int i = 0; i < count; i++)
                    {
                        int b0 = pcm[ip];
                        int b1 = pcm[ip + 1] << 8;
                        int b2 = pcm[ip + 2] << 16;
                        int v = (b0 | b1 | b2);
                        if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                        samples[i] = Mathf.Clamp(v / 8388608f, -1f, 1f);
                        ip += 3;
                    }
                }
                else if (bitsPerSample == 32)
                {
                    int count = pcm.Length / 4;
                    samples = new float[count];
                    int ip = 0;
                    for (int i = 0; i < count; i++)
                    {
                        int v = BitConverter.ToInt32(pcm, ip);
                        samples[i] = Mathf.Clamp(v / 2147483648f, -1f, 1f);
                        ip += 4;
                    }
                }
                else
                {
                    throw new Exception("Unsupported PCM bit depth: " + bitsPerSample + " (use 16/24/32-bit PCM or 32-bit float).");
                }
            }
            else
            {
                if (bitsPerSample != 32) throw new Exception("Unsupported float bit depth: " + bitsPerSample);
                int count = pcm.Length / 4;
                samples = new float[count];
                Buffer.BlockCopy(pcm, 0, samples, 0, pcm.Length);
            }

            int lengthSamples = samples.Length / channels;
            AudioClip clip = AudioClip.Create("VNitro_Hiss", lengthSamples, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static bool TryCopyFromVehicleSound(GameObject? vehicleRoot)
        {
            try
            {
                if (vehicleRoot == null || _src == null) return false;

                Component? vs = null;
                var all = vehicleRoot.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    if (c == null) continue;
                    var tn = c.GetType().FullName;
                    if (tn != null && tn.IndexOf("VehicleSound", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        vs = c; break;
                    }
                }

                if (vs != null)
                {
                    var vt = vs.GetType();
                    var field = AccessTools.Field(vt, "EngineLoopSource") ?? AccessTools.Field(vt, "EngineIdleSource");
                    var ctrlObj = field != null ? field.GetValue(vs) : null;
                    AudioSource? tpl = null;
                    if (ctrlObj is Component ctrl)
                    {
                        tpl = ctrl.GetComponent<AudioSource>() ?? ctrl.GetComponentInChildren<AudioSource>(true);
                    }

                    if (tpl != null)
                    {
                        _src.outputAudioMixerGroup = tpl.outputAudioMixerGroup;
                        _src.spatialBlend = tpl.spatialBlend;
                        _src.dopplerLevel = tpl.dopplerLevel;
                        _src.rolloffMode = tpl.rolloffMode;
                        _src.minDistance = tpl.minDistance;
                        _src.maxDistance = tpl.maxDistance;
                        MelonLogger.Msg("[VNitro] Hiss copied VehicleSound mixer/3D settings.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[VNitro] Copy VehicleSound settings failed: {ex.Message}");
            }
            return false;
        }

        private static string ReadString(byte[] b, ref int p, int len)
        {
            string s = System.Text.Encoding.ASCII.GetString(b, p, len);
            p += len; return s;
        }
        private static int ReadInt(byte[] b, ref int p)
        {
            int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
            p += 4; return v;
        }
        private static short ReadShort(byte[] b, ref int p)
        {
            short v = (short)(b[p] | (b[p + 1] << 8));
            p += 2; return v;
        }
    }
}
