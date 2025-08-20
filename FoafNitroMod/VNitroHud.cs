using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace FoafNitroMod
{
    internal static class VNitroHud
    {
        // -------- Public API --------
        internal static void SetActive(bool isInVehicle)
        {
            _inVehicle = isInVehicle;

            // keep the hiss audio in sync with vehicle state
            VNitroAudio.SetActive(isInVehicle);

            if (!isInVehicle)
            {
                // ensure hiss fades out immediately when we leave the vehicle
                VNitroAudio.SetBoosting(false);
                _sheenOffset = 0f; // reset bar sheen animation (optional)
            }
        }

        internal static void SetFill01(float pct) => _nitro = Mathf.Clamp01(pct);
        internal static void SetBoosting(bool boosting) => _boosting = boosting;

        internal static void OnUpdate()
        {
            // Toggle HUD visibility
            if (Input.GetKeyDown(KeyCode.F9)) _visible = !_visible;

            // Quick layout flip (Bottom <-> Top)
            if (Input.GetKeyDown(KeyCode.F8)) _dockBottom = !_dockBottom;

            // Scale (hold Shift for finer steps)
            float step = Input.GetKey(KeyCode.LeftShift) ? 0.02f : 0.05f;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) _scale = Mathf.Clamp(_scale + step, 0.6f, 1.6f);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) _scale = Mathf.Clamp(_scale - step, 0.6f, 1.6f);

            // Opacity (glass panel)
            if (Input.GetKeyDown(KeyCode.RightBracket)) _panelOpacity = Mathf.Clamp01(_panelOpacity + 0.05f);
            if (Input.GetKeyDown(KeyCode.LeftBracket)) _panelOpacity = Mathf.Clamp01(_panelOpacity - 0.05f);

            if (!_inVehicle) return;

            // Animate sheen based on boosting state
            if (_boosting)
                _sheenOffset = (_sheenOffset + Time.deltaTime * 0.7f) % 1f;
            else
                _sheenOffset = Mathf.MoveTowards(_sheenOffset, 0f, Time.deltaTime * 0.8f);
        }

        internal static void Draw()
        {
            if (!_visible || !_inVehicle) return;
            EnsureAssets();

            // Layout (responsive & scalable)
            float baseW = 420f, baseH = 94f;
            float panelW = baseW * _scale;
            float panelH = baseH * _scale;

            float margin = 32f * _scale;
            float x = (Screen.width - panelW) * 0.5f;
            float y = _dockBottom
                ? (Screen.height - panelH - margin)
                : margin;

            // Soft drop shadow
            var shadowR = new Rect(x, y + (4f * _scale), panelW, panelH);
            GUI.color = new Color(0f, 0f, 0f, 0.25f * _panelOpacity);
            GUI.DrawTexture(shadowR, _roundedPanelTex, ScaleMode.StretchToFill, true);
            GUI.color = Color.white;

            // Glass panel
            GUI.color = new Color(1f, 1f, 1f, _panelOpacity);
            GUI.DrawTexture(new Rect(x, y, panelW, panelH), _panelGradTex, ScaleMode.StretchToFill, true);
            GUI.DrawTexture(new Rect(x, y, panelW, panelH), _roundedPanelTex, ScaleMode.StretchToFill, true);
            // Subtle top highlight line
            GUI.color = new Color(1f, 1f, 1f, 0.12f * _panelOpacity);
            GUI.DrawTexture(new Rect(x, y, panelW, 1.5f * _scale), _whiteTex);
            GUI.color = Color.white;

            // Icon
            float iconSize = 56f * _scale;
            float iconX = x + (14f * _scale);
            float iconY = y + (panelH - iconSize) * 0.5f;
            if (_icon != null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.95f);
                GUI.DrawTexture(new Rect(iconX, iconY, iconSize, iconSize), _icon, ScaleMode.ScaleToFit, true);
                GUI.color = Color.white;
            }

            // Text
            float textLeft = iconX + (_icon != null ? (iconSize + 16f * _scale) : (8f * _scale));
            float titleY = y + (10f * _scale);

            // Title + percent
            int pct = Mathf.RoundToInt(_nitro * 100f);
            string title = $"NITRO   {pct}%";
            ShadowLabel(new Rect(textLeft, titleY, panelW - (textLeft - x) - 12f, 28f * _scale), title, _titleStyle);

            // Hint (also show LOW when under threshold)
            string hint = (!_boosting && _nitro < _minBoostPct)
                ? "LOW: Need ≥ 20% to boost   •   F8 layout  F9 hide"
                : "Hold Left Shift to boost   •   F8 layout  F9 hide";
            ShadowLabel(new Rect(textLeft, titleY + (26f * _scale), panelW - (textLeft - x) - 12f, 22f * _scale), hint, _hintStyle);

            // Bar
            float barLeft = textLeft;
            float barTop = y + panelH - (26f * _scale);
            float barW = panelW - (barLeft - x) - (12f * _scale);
            float barH = 12f * _scale;

            DrawModernBar(new Rect(barLeft, barTop, barW, barH), _nitro);
        }

        // --------- visuals ---------
        private static void DrawModernBar(Rect r, float pct)
        {
            // Bar background (rounded + subtle border)
            GUI.color = new Color(1f, 1f, 1f, 0.28f);
            GUI.DrawTexture(r, _roundedBarTex, ScaleMode.StretchToFill, true);

            // Fill
            float w = Mathf.Round(pct * r.width);
            if (w > 1f)
            {
                var fillRect = new Rect(r.x, r.y, w, r.height);

                // If we're below threshold, dim the gradient slightly
                float dim = pct < _minBoostPct ? 0.65f : 1f;

                // Gradient fill
                GUI.color = new Color(1f, 1f, 1f, dim);
                GUI.DrawTexture(fillRect, _barGradTex, ScaleMode.StretchToFill, true);

                // Gloss (top inner highlight)
                GUI.color = new Color(1f, 1f, 1f, 0.18f * dim);
                GUI.DrawTexture(new Rect(fillRect.x, fillRect.y, fillRect.width, fillRect.height * 0.45f), _whiteTex);
                GUI.color = Color.white;

                // Animated sheen
                if (_sheenOffset > 0f && pct > 0.1f && _boosting)
                {
                    float sheenW = Mathf.Min(80f * _scale, fillRect.width * 0.6f);
                    float x = fillRect.x + (fillRect.width - sheenW) * _sheenOffset;
                    var sheenRect = new Rect(x, fillRect.y, sheenW, fillRect.height);
                    GUI.color = new Color(1f, 1f, 1f, 0.22f);
                    GUI.DrawTexture(sheenRect, _sheenGradTex, ScaleMode.StretchToFill, true);
                    GUI.color = Color.white;
                }
            }

            // Outer border
            GUI.color = new Color(1f, 1f, 1f, 0.10f);
            GUI.DrawTexture(r, _roundedBarBorderTex, ScaleMode.StretchToFill, true);
            GUI.color = Color.white;

            // Tiny notch marker at 20% (visual cue)
            float notchX = r.x + r.width * _minBoostPct;
            var notch = new Rect(notchX - 1f, r.y - 2f, 2f, r.height + 4f);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(notch, _whiteTex);
            GUI.color = Color.white;
        }

        private static void ShadowLabel(Rect r, string text, GUIStyle style)
        {
            var off = 1f * _scale;
            var shadow = new Rect(r.x + off, r.y + off, r.width, r.height);
            var was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(shadow, text, style);
            GUI.color = was;
            GUI.Label(r, text, style);
        }

        // --------- assets / setup ---------
        private static void EnsureAssets()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(18 * _scale),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft,
                    richText = false
                };
            }
            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(12 * _scale),
                    alignment = TextAnchor.UpperLeft,
                    richText = false
                };
            }

            if (_whiteTex == null) _whiteTex = MakeTex(new Color(1, 1, 1, 1));

            if (_panelGradTex == null)
                _panelGradTex = MakeVerticalGradientTex(
                    new Color(0.06f, 0.08f, 0.10f, 0.70f * _panelOpacity),
                    new Color(0.06f, 0.08f, 0.10f, 0.55f * _panelOpacity));

            if (_roundedPanelTex == null) _roundedPanelTex = MakeRoundedBoxTex(32, new Color(1, 1, 1, 0.14f));

            if (_roundedBarTex == null) _roundedBarTex = MakeRoundedBoxTex(16, new Color(0, 0, 0, 0.55f));
            if (_roundedBarBorderTex == null) _roundedBarBorderTex = MakeRoundedBoxBorderTex(16, new Color(1, 1, 1, 0.35f));

            if (_barGradTex == null)
                _barGradTex = MakeHorizontalGradientTex(_accentA, _accentB);

            if (_sheenGradTex == null)
                _sheenGradTex = MakeHorizontalGradientTex(
                    new Color(1f, 1f, 1f, 0f),
                    new Color(1f, 1f, 1f, 1f),
                    new Color(1f, 1f, 1f, 0f));

            if (_icon == null)
            {
                string baseDir = MelonEnvironment.MelonBaseDirectory;
                string p1 = Path.Combine(baseDir, "Mods", "VNitro", "UI", "nitro.png");
                string p2 = Path.Combine(baseDir, "Mods", "VNitro", "ui", "nitro.png");
                string? p = File.Exists(p1) ? p1 : (File.Exists(p2) ? p2 : null);
                if (!string.IsNullOrEmpty(p))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(p);
                        var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                        if (ImageConversion.LoadImage(tex, bytes))
                        {
                            tex.wrapMode = TextureWrapMode.Clamp;
                            _icon = tex;
                            MelonLogger.Msg($"[VNitroHUD] Loaded icon: {p}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"[VNitroHUD] Icon load failed: {ex.Message}");
                    }
                }
            }
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static Texture2D MakeVerticalGradientTex(Color top, Color bottom, int h = 64, int w = 2)
        {
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1);
                var c = Color.Lerp(top, bottom, t);
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D MakeHorizontalGradientTex(Color left, Color right, Color? mid = null, int w = 256, int h = 2)
        {
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            for (int x = 0; x < w; x++)
            {
                float t = (float)x / (w - 1);
                Color c;
                if (mid.HasValue)
                {
                    // three-stop gradient (left -> mid at 0.5 -> right)
                    if (t < 0.5f)
                        c = Color.Lerp(left, mid.Value, t / 0.5f);
                    else
                        c = Color.Lerp(mid.Value, right, (t - 0.5f) / 0.5f);
                }
                else c = Color.Lerp(left, right, t);

                for (int y = 0; y < h; y++)
                    tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D MakeRoundedBoxTex(int radius, Color fill)
        {
            int size = radius * 2;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            // draw a quarter circle mask and mirror via GUI scaling
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - radius + 0.5f;
                    float dy = y - radius + 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Clamp01((radius - dist));
                    // feather edge a bit
                    float a = Mathf.Clamp01(edge / 1.5f) * fill.a;
                    tex.SetPixel(x, y, new Color(fill.r, fill.g, fill.b, a));
                }
            tex.Apply();
            return tex;
        }

        private static Texture2D MakeRoundedBoxBorderTex(int radius, Color border)
        {
            int size = radius * 2;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            float borderWidth = 1.25f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - radius + 0.5f;
                    float dy = y - radius + 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float d = Mathf.Abs(dist - radius + 1f);
                    float a = Mathf.Clamp01(1f - (d / borderWidth)) * border.a;
                    tex.SetPixel(x, y, new Color(border.r, border.g, border.b, a));
                }
            tex.Apply();
            return tex;
        }

        // --------- state ---------
        private static bool _inVehicle;
        private static bool _visible = true;
        private static bool _dockBottom = true;
        private static float _scale = 1f;
        private static float _panelOpacity = 0.9f;

        private static float _nitro = 1f;           // 0..1 (set by VNitroLite)
        private static bool _boosting = false;     // for sheen animation only
        private const float _minBoostPct = 0.20f;   // visual cue + hint text

        private static float _sheenOffset = 0f; // 0..1

        // Accent gradient (cyan -> purple)
        private static readonly Color _accentA = new Color(0.18f, 0.90f, 1f, 1f);
        private static readonly Color _accentB = new Color(0.67f, 0.41f, 1f, 1f);

        // Styles & textures (lazy-inited, null-forgiven to keep warnings quiet)
        private static GUIStyle _titleStyle = null!;
        private static GUIStyle _hintStyle = null!;

        private static Texture2D _whiteTex = null!;
        private static Texture2D _panelGradTex = null!;
        private static Texture2D _roundedPanelTex = null!;
        private static Texture2D _roundedBarTex = null!;
        private static Texture2D _roundedBarBorderTex = null!;
        private static Texture2D _barGradTex = null!;
        private static Texture2D _sheenGradTex = null!;
        private static Texture2D _icon = null!;
    }
}
