using SilentNight.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SilentNight.UI
{
    /// <summary>Christmas-toy palette shared by USS (hardcoded there too) and vector-drawn icons.</summary>
    public static class Palette
    {
        public static readonly Color Outline = Hex(0x2A140A);
        public static readonly Color WoodDark = Hex(0x3B2314);
        public static readonly Color Wood = Hex(0x6B3E22);
        public static readonly Color Cream = Hex(0xFFF3D6);
        public static readonly Color Paper = Hex(0xF7E6C4);
        public static readonly Color Gold = Hex(0xF2B630);
        public static readonly Color GoldDark = Hex(0xB07A12);
        public static readonly Color Red = Hex(0xC8282E);
        public static readonly Color RedDark = Hex(0x7E1418);
        public static readonly Color Green = Hex(0x2E9A45);
        public static readonly Color GreenDark = Hex(0x18491F);
        public static readonly Color Blue = Hex(0x2F6FB8);
        public static readonly Color Orange = Hex(0xF07A1A);
        public static readonly Color Purple = Hex(0x6A3A9A);
        public static readonly Color PurpleLight = Hex(0xB07CFF);
        public static readonly Color Unlit = Hex(0x4A2E1E);
        public static readonly Color SleepBlue = Hex(0x8FA8C8);

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

        public static Color GiftColor(GiftKind k) => k switch
        {
            GiftKind.Noisy => Blue,
            GiftKind.Huge => Green,
            _ => Red
        };

        public static IconKind GiftIcon(GiftKind k) => k switch
        {
            GiftKind.Noisy => IconKind.GiftNoisy,
            GiftKind.Huge => IconKind.GiftHuge,
            _ => IconKind.GiftSmall
        };
    }

    internal static class Paint
    {
        public static Angle Deg(float d) => new Angle(d, AngleUnit.Degree);

        public static void Circle(Painter2D p, Vector2 c, float r)
        {
            p.BeginPath();
            p.Arc(c, r, Deg(0f), Deg(360f)); // full 360 sweep = closed circle
            p.ClosePath();
        }

        public static void RoundRect(Painter2D p, float x, float y, float w, float h, float r)
        {
            r = Mathf.Min(r, w * 0.5f, h * 0.5f);
            p.BeginPath();
            p.MoveTo(new Vector2(x + r, y));
            p.LineTo(new Vector2(x + w - r, y));
            p.Arc(new Vector2(x + w - r, y + r), r, Deg(-90f), Deg(0f));
            p.LineTo(new Vector2(x + w, y + h - r));
            p.Arc(new Vector2(x + w - r, y + h - r), r, Deg(0f), Deg(90f));
            p.LineTo(new Vector2(x + r, y + h));
            p.Arc(new Vector2(x + r, y + h - r), r, Deg(90f), Deg(180f));
            p.LineTo(new Vector2(x, y + r));
            p.Arc(new Vector2(x + r, y + r), r, Deg(180f), Deg(270f));
            p.ClosePath();
        }

        /// <summary>Fill the current path, then stroke it with the dark toy outline
        /// (Painter2D keeps the path until the next BeginPath, so Stroke() reuses it).</summary>
        public static void FillStroke(Painter2D p, Color fill, float outline)
        {
            p.fillColor = fill;
            p.Fill();
            p.strokeColor = Palette.Outline;
            p.lineWidth = outline;
            p.Stroke();
        }
    }

    public enum IconKind { Coin, GiftSmall, GiftNoisy, GiftHuge, Jetpack, Boot, Sack, Mitten, Star, Close }

    /// <summary>Small vector icon drawn with Painter2D (no textures, no emoji).</summary>
    public class IconElement : VisualElement
    {
        private IconKind _kind;
        private Color _main;
        private Color _accent;
        private Vector2 _o;
        private float _s;

        public IconElement(IconKind kind) : this(kind, DefaultMain(kind), DefaultAccent(kind)) { }

        public IconElement(IconKind kind, Color main, Color accent)
        {
            _kind = kind; _main = main; _accent = accent;
            pickingMode = PickingMode.Ignore;
            AddToClassList("sn-icon");
            generateVisualContent += Draw;
        }

        public void Set(IconKind kind, Color main, Color accent)
        {
            _kind = kind; _main = main; _accent = accent;
            MarkDirtyRepaint();
        }

        public void SetColors(Color main, Color accent)
        {
            if (main == _main && accent == _accent) return;
            _main = main; _accent = accent;
            MarkDirtyRepaint();
        }

        public static Color DefaultMain(IconKind k) => k switch
        {
            IconKind.Coin => Palette.Gold,
            IconKind.GiftSmall => Palette.Red,
            IconKind.GiftNoisy => Palette.Blue,
            IconKind.GiftHuge => Palette.Green,
            IconKind.Jetpack => Palette.Red,
            IconKind.Boot => Palette.Green,
            IconKind.Sack => Palette.Hex(0x9A6A3A),
            IconKind.Mitten => Palette.Red,
            IconKind.Star => Palette.Gold,
            _ => Palette.Cream
        };

        public static Color DefaultAccent(IconKind k) => k switch
        {
            IconKind.Coin => Palette.GoldDark,
            IconKind.Jetpack => Palette.Orange,
            IconKind.Mitten => Palette.Cream,
            IconKind.Close => Palette.Cream,
            _ => Palette.Gold
        };

        private Vector2 P(float x, float y) => new Vector2(_o.x + x * _s, _o.y + y * _s);
        private float L(float v) => v * _s;

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 2f || r.height < 2f) return;
            _s = Mathf.Min(r.width, r.height);
            _o = new Vector2(r.x + (r.width - _s) * 0.5f, r.y + (r.height - _s) * 0.5f);
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            float ow = L(0.07f);

            switch (_kind)
            {
                case IconKind.Coin:
                    Paint.Circle(p, P(0.5f, 0.5f), L(0.42f));
                    Paint.FillStroke(p, _main, ow);
                    Paint.Circle(p, P(0.5f, 0.5f), L(0.27f));
                    p.strokeColor = _accent; p.lineWidth = L(0.06f); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(0.5f, 0.34f)); p.LineTo(P(0.5f, 0.66f));
                    p.strokeColor = _accent; p.lineWidth = L(0.08f); p.Stroke();
                    break;

                case IconKind.GiftSmall:
                case IconKind.GiftNoisy:
                case IconKind.GiftHuge:
                    DrawGift(p, ow);
                    break;

                case IconKind.Jetpack:
                    for (int i = 0; i < 2; i++)
                    {
                        float x = i == 0 ? 0.16f : 0.54f;
                        p.BeginPath();
                        p.MoveTo(P(x + 0.04f, 0.70f)); p.LineTo(P(x + 0.26f, 0.70f)); p.LineTo(P(x + 0.15f, 0.97f)); p.ClosePath();
                        Paint.FillStroke(p, _accent, L(0.05f));
                        Paint.RoundRect(p, P(x, 0.1f).x, P(x, 0.1f).y, L(0.30f), L(0.62f), L(0.14f));
                        Paint.FillStroke(p, _main, ow);
                    }
                    Paint.RoundRect(p, P(0.40f, 0.28f).x, P(0.40f, 0.28f).y, L(0.20f), L(0.12f), L(0.04f));
                    Paint.FillStroke(p, Palette.Gold, L(0.05f));
                    break;

                case IconKind.Boot:
                    p.BeginPath();
                    p.MoveTo(P(0.28f, 0.12f)); p.LineTo(P(0.60f, 0.12f)); p.LineTo(P(0.60f, 0.56f));
                    p.QuadraticCurveTo(P(0.84f, 0.58f), P(0.92f, 0.46f));
                    p.QuadraticCurveTo(P(1.0f, 0.70f), P(0.84f, 0.88f));
                    p.LineTo(P(0.22f, 0.88f)); p.LineTo(P(0.22f, 0.60f)); p.ClosePath();
                    Paint.FillStroke(p, _main, ow);
                    Paint.RoundRect(p, P(0.24f, 0.08f).x, P(0.24f, 0.08f).y, L(0.40f), L(0.16f), L(0.05f));
                    Paint.FillStroke(p, _accent, L(0.05f));
                    Paint.Circle(p, P(0.93f, 0.44f), L(0.06f));
                    Paint.FillStroke(p, _accent, L(0.04f));
                    break;

                case IconKind.Sack:
                    Paint.Circle(p, P(0.5f, 0.62f), L(0.32f));
                    Paint.FillStroke(p, _main, ow);
                    p.BeginPath();
                    p.MoveTo(P(0.38f, 0.32f)); p.LineTo(P(0.30f, 0.12f)); p.LineTo(P(0.50f, 0.20f)); p.LineTo(P(0.70f, 0.12f)); p.LineTo(P(0.62f, 0.32f)); p.ClosePath();
                    Paint.FillStroke(p, _main, ow);
                    Paint.RoundRect(p, P(0.34f, 0.28f).x, P(0.34f, 0.28f).y, L(0.32f), L(0.08f), L(0.03f));
                    Paint.FillStroke(p, _accent, L(0.04f));
                    break;

                case IconKind.Mitten:
                    Paint.Circle(p, P(0.25f, 0.50f), L(0.13f));
                    Paint.FillStroke(p, _main, ow);
                    Paint.RoundRect(p, P(0.28f, 0.12f).x, P(0.28f, 0.12f).y, L(0.46f), L(0.64f), L(0.22f));
                    Paint.FillStroke(p, _main, ow);
                    Paint.RoundRect(p, P(0.24f, 0.70f).x, P(0.24f, 0.70f).y, L(0.54f), L(0.18f), L(0.06f));
                    Paint.FillStroke(p, _accent, ow);
                    break;

                case IconKind.Star:
                    p.BeginPath();
                    for (int i = 0; i < 10; i++)
                    {
                        float a = (-90f + i * 36f) * Mathf.Deg2Rad;
                        float rr = (i % 2 == 0) ? 0.46f : 0.20f;
                        Vector2 pt = P(0.5f + Mathf.Cos(a) * rr, 0.53f + Mathf.Sin(a) * rr);
                        if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                    }
                    p.ClosePath();
                    Paint.FillStroke(p, _main, ow);
                    break;

                case IconKind.Close:
                    for (int pass = 0; pass < 2; pass++)
                    {
                        p.strokeColor = pass == 0 ? Palette.Outline : _accent;
                        p.lineWidth = pass == 0 ? L(0.30f) : L(0.16f);
                        p.BeginPath(); p.MoveTo(P(0.25f, 0.25f)); p.LineTo(P(0.75f, 0.75f)); p.Stroke();
                        p.BeginPath(); p.MoveTo(P(0.75f, 0.25f)); p.LineTo(P(0.25f, 0.75f)); p.Stroke();
                    }
                    break;
            }
        }

        private void DrawGift(Painter2D p, float ow)
        {
            bool huge = _kind == IconKind.GiftHuge;
            float bx = huge ? 0.06f : 0.14f, bw = huge ? 0.88f : 0.72f;
            float top = huge ? 0.30f : 0.36f;
            // bow loops
            Paint.Circle(p, P(0.37f, top - 0.10f), L(0.10f));
            Paint.FillStroke(p, _accent, L(0.05f));
            Paint.Circle(p, P(0.63f, top - 0.10f), L(0.10f));
            Paint.FillStroke(p, _accent, L(0.05f));
            // body
            Paint.RoundRect(p, P(bx, top).x, P(bx, top).y, L(bw), L(0.90f - top), L(0.06f));
            Paint.FillStroke(p, _main, ow);
            // lid
            Paint.RoundRect(p, P(bx - 0.05f, top - 0.03f).x, P(bx - 0.05f, top - 0.03f).y, L(bw + 0.10f), L(0.15f), L(0.05f));
            Paint.FillStroke(p, _main, ow);
            // ribbon
            p.BeginPath();
            p.MoveTo(P(0.5f, top - 0.02f)); p.LineTo(P(0.5f, 0.88f));
            p.strokeColor = _accent; p.lineWidth = L(0.11f); p.lineCap = LineCap.Butt; p.Stroke();
            if (huge)
            {
                p.BeginPath(); p.MoveTo(P(bx + 0.02f, 0.66f)); p.LineTo(P(bx + bw - 0.02f, 0.66f)); p.Stroke();
            }
            p.lineCap = LineCap.Round;
            if (_kind == IconKind.GiftNoisy)
            {
                // wind-up clock face
                Paint.Circle(p, P(0.5f, 0.66f), L(0.15f));
                Paint.FillStroke(p, Palette.Cream, L(0.05f));
                p.BeginPath(); p.MoveTo(P(0.5f, 0.66f)); p.LineTo(P(0.5f, 0.57f));
                p.MoveTo(P(0.5f, 0.66f)); p.LineTo(P(0.57f, 0.69f));
                p.strokeColor = Palette.Outline; p.lineWidth = L(0.045f); p.Stroke();
            }
        }
    }

    /// <summary>
    /// Diegetic noise meter: a chunky semicircular "sound wave" of segments that thicken toward the loud end,
    /// a needle and a cream tick at the grandpa wake threshold.
    /// </summary>
    public class NoiseGaugeElement : VisualElement
    {
        private const int Segments = 11;
        private float _value01;
        private float _threshold01 = GameConstants.GrandpaWakeThreshold / 100f;

        public float Value01
        {
            get => _value01;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Abs(value - _value01) < 0.004f) return;
                _value01 = value;
                MarkDirtyRepaint();
            }
        }

        public float Threshold01
        {
            get => _threshold01;
            set { _threshold01 = Mathf.Clamp01(value); MarkDirtyRepaint(); }
        }

        public NoiseGaugeElement()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("sn-gauge");
            generateVisualContent += Draw;
        }

        private static Color SegmentColor(float t, float threshold)
        {
            if (t >= threshold) return Color.Lerp(Palette.Orange, Palette.Red, Mathf.InverseLerp(threshold, 1f, t));
            return Color.Lerp(Palette.Green, Palette.Gold, Mathf.InverseLerp(0f, threshold, t));
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 10f || r.height < 10f) return;
            var p = ctx.painter2D;
            float baseThick = Mathf.Min(r.width, r.height * 2f) * 0.085f;
            float R = Mathf.Min(r.width * 0.5f, r.height) - baseThick * 1.4f;
            Vector2 c = new Vector2(r.center.x, r.yMax - baseThick * 0.6f);
            float step = 180f / Segments;
            float gap = 3.2f;

            p.lineCap = LineCap.Butt;
            for (int i = 0; i < Segments; i++)
            {
                float t = (i + 0.5f) / Segments;
                float thick = baseThick * Mathf.Lerp(0.65f, 1.35f, i / (float)(Segments - 1));
                float a0 = 180f + i * step + gap * 0.5f;
                float a1 = a0 + step - gap;

                p.strokeColor = Palette.Outline;
                p.lineWidth = thick + 7f;
                p.BeginPath(); p.Arc(c, R, Paint.Deg(a0 - 0.9f), Paint.Deg(a1 + 0.9f)); p.Stroke();

                bool lit = t <= _value01 + 0.0001f;
                p.strokeColor = lit ? SegmentColor(t, _threshold01) : Palette.Unlit;
                p.lineWidth = thick;
                p.BeginPath(); p.Arc(c, R, Paint.Deg(a0), Paint.Deg(a1)); p.Stroke();
            }

            // threshold tick
            float ta = (180f + _threshold01 * 180f) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta));
            p.lineCap = LineCap.Round;
            for (int pass = 0; pass < 2; pass++)
            {
                p.strokeColor = pass == 0 ? Palette.Outline : Palette.Cream;
                p.lineWidth = pass == 0 ? 9f : 4f;
                p.BeginPath();
                p.MoveTo(c + dir * (R - baseThick * 1.1f));
                p.LineTo(c + dir * (R + baseThick * 1.1f));
                p.Stroke();
            }

            // needle
            float na = (180f + _value01 * 180f) * Mathf.Deg2Rad;
            Vector2 nd = new Vector2(Mathf.Cos(na), Mathf.Sin(na));
            for (int pass = 0; pass < 2; pass++)
            {
                p.strokeColor = pass == 0 ? Palette.Outline : Palette.Cream;
                p.lineWidth = pass == 0 ? 10f : 5f;
                p.BeginPath(); p.MoveTo(c); p.LineTo(c + nd * (R - baseThick * 1.0f)); p.Stroke();
            }
            Paint.Circle(p, c, baseThick * 0.7f);
            Paint.FillStroke(p, Palette.Red, 4f);
        }
    }

    /// <summary>Grandpa alert eye: closed when asleep, opens and fills its ring as alertness grows.</summary>
    public class AlertEyeElement : VisualElement
    {
        private static readonly float[] Open = { 0f, 0.22f, 0.5f, 0.8f, 1.0f };
        private AlertLevel _level = AlertLevel.Asleep;

        public AlertLevel Level
        {
            get => _level;
            set { if (value == _level) return; _level = value; MarkDirtyRepaint(); }
        }

        public AlertEyeElement()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("sn-eye");
            generateVisualContent += Draw;
        }

        public static Color LevelColor(AlertLevel l) => l switch
        {
            AlertLevel.Asleep => Palette.SleepBlue,
            AlertLevel.Stirring => Palette.Hex(0xE8D36A),
            AlertLevel.Suspicious => Palette.Gold,
            AlertLevel.Searching => Palette.Orange,
            _ => Palette.Red
        };

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 10f || r.height < 10f) return;
            var p = ctx.painter2D;
            float s = Mathf.Min(r.width, r.height);
            Vector2 c = r.center;
            int li = (int)_level;
            Color col = LevelColor(_level);

            // wooden medallion
            Paint.Circle(p, c, s * 0.46f);
            Paint.FillStroke(p, _level == AlertLevel.Chasing ? Palette.RedDark : Palette.WoodDark, s * 0.05f);

            // fill ring
            p.lineCap = LineCap.Round;
            p.strokeColor = Palette.Unlit;
            p.lineWidth = s * 0.07f;
            Paint.Circle(p, c, s * 0.38f);
            p.Stroke();
            float fill = li / 4f;
            if (fill > 0.001f)
            {
                p.strokeColor = col;
                p.BeginPath();
                p.Arc(c, s * 0.38f, Paint.Deg(-90f), Paint.Deg(-90f + 359.9f * fill));
                p.Stroke();
            }

            Vector2 left = c + new Vector2(-s * 0.27f, 0f);
            Vector2 right = c + new Vector2(s * 0.27f, 0f);
            float open = Open[Mathf.Clamp(li, 0, 4)] * s * 0.19f;

            if (li == 0)
            {
                // closed, sleepy lid with lashes
                p.strokeColor = Palette.Cream;
                p.lineWidth = s * 0.05f;
                p.BeginPath();
                p.MoveTo(left);
                p.QuadraticCurveTo(c + new Vector2(0f, s * 0.16f), right);
                p.Stroke();
                p.lineWidth = s * 0.035f;
                for (int i = -1; i <= 1; i++)
                {
                    Vector2 b = c + new Vector2(i * s * 0.12f, s * 0.07f - Mathf.Abs(i) * s * 0.025f);
                    p.BeginPath(); p.MoveTo(b); p.LineTo(b + new Vector2(i * s * 0.03f, s * 0.07f)); p.Stroke();
                }
                return;
            }

            p.BeginPath();
            p.MoveTo(left);
            p.QuadraticCurveTo(c + new Vector2(0f, -open * 2f), right);
            p.QuadraticCurveTo(c + new Vector2(0f, open * 2f), left);
            p.ClosePath();
            Paint.FillStroke(p, _level == AlertLevel.Chasing ? Palette.Hex(0xFFD9CF) : Palette.Cream, s * 0.04f);

            float iris = Mathf.Min(s * 0.13f, open * 0.92f);
            if (iris > 1f)
            {
                Paint.Circle(p, c, iris);
                Paint.FillStroke(p, col, s * 0.025f);
                p.fillColor = Palette.Outline;
                Paint.Circle(p, c, iris * (_level == AlertLevel.Chasing ? 0.3f : 0.48f));
                p.Fill();
                p.fillColor = Palette.Cream;
                Paint.Circle(p, c + new Vector2(iris * 0.35f, -iris * 0.35f), iris * 0.18f);
                p.Fill();
            }
        }
    }
}
