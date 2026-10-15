using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAStargate.Engine;
using Effect = AVAStargate.Engine.Effect;

namespace AVAStargate.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAStargate/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;
    private const uint GateGold = 0xFFFFD84A, Rock = 0xFFC8743A, Pink = 0xFFFF8AC8;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>The colours a laser streak flickers through.</summary>
    private static readonly uint[] Rainbow = [0xFFFF4A4A, 0xFFFFA04A, 0xFFFFF04A, 0xFF6AFF6A, 0xFF6AE0FF, 0xFFB06AFF, 0xFFFFFFFF];

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(g.DeepSpace && InGame ? 0xFF02000E : 0xFF000006), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top)))
            {
                DrawStars(dc);
                if (!g.DeepSpace && !g.PlanetLost) DrawMountains(dc);
                DrawGate(dc);
                if (!g.DeepSpace)
                    foreach (var c in g.Colonists.Where(c => c.State != ColonistState.Dead && g.OnScreen(c.X, 20)))
                        DrawColonist(dc, new Point(g.ScreenX(c.X), c.Y), c.State, c.Walk, 1);
                foreach (var e in g.Enemies.Where(e => g.OnScreen(e.X, 40)))
                    DrawEnemy(dc, e.Kind, new Point(g.ScreenX(e.X), e.Y), e.Spin + e.T, e.Hurt > 0, 1);
                foreach (var b in g.Bolts.Where(b => g.OnScreen(b.X, 10))) DrawBolt(dc, b);
                foreach (var l in g.Lasers) DrawLaser(dc, l);
                foreach (var d in g.Wreckage.Where(d => g.OnScreen(d.X, 10)))
                {
                    double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                    dc.FillRectangle(Brush(Alpha(d.Colour, a)), new Rect(g.ScreenX(d.X) - 2, d.Y - 2, 4, 4));
                }
                var ship = g.Ship;
                if (ship.Alive && (ship.Safe <= 0 || (int)(_clock * 10) % 2 == 0))
                    DrawShip(dc, new Point(ship.Offset, ship.Y), ship.Facing, ship.Thrusting, ship.Cloaked, 1);
                foreach (var e in g.Effects.Where(e => e.Kind != EffectKind.Word)) DrawEffect(dc, e, g.ScreenX(e.X));
            }
        }
        foreach (var e in g.Effects.Where(e => e.Kind == EffectKind.Word)) DrawEffect(dc, e, e.X);
        if (g.Flash > 0 && InGame) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, g.Flash * 2.2)), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 380), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── the planet ─────────────────────────────

    /// <summary>Stars at three depths, the farther ones scrolling slower.</summary>
    private void DrawStars(DrawingContext dc)
    {
        double cam = Engine.CamX;
        for (int k = 0; k < 120; k++)
        {
            double depth = 0.08 + k % 3 * 0.1;
            double x = ((Hash(k, 1, 9) % 4096 - cam * depth) % 1024 + 1024) % 1024;
            double y = Field.Top + 6 + Hash(k, 2, 9) % (uint)(Field.Ground - Field.Top - 60);
            double tw = 0.5 + 0.5 * Math.Sin(_clock * (0.8 + k % 5 * 0.5) + k);
            uint col = (k % 6) switch { 0 => 0xFFFFC0E0, 1 => 0xFFA0E0FF, 2 => 0xFFFFF0A0, _ => 0xFFFFFFFF };
            double size = k % 3 == 2 ? 2.5 : 1.8;
            dc.FillRectangle(Brush(Alpha(col, 0.2 + 0.6 * tw * (0.5 + depth * 2))), new Rect(x, y, size, size));
        }
    }

    /// <summary>The mountains along the bottom: a jagged outline over dark rock.</summary>
    private void DrawMountains(DrawingContext dc)
    {
        double cam = Engine.CamX;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(-8, Field.Ground + 20), true);
            for (double sx = -8; sx <= Field.Width + 8; sx += 8)
                c.LineTo(new Point(sx, Field.Ground - Field.Mountain(Field.Wrap(cam + sx))));
            c.LineTo(new Point(Field.Width + 8, Field.Ground + 20));
            c.EndFigure(true);
        }
        dc.DrawGeometry(Brush(0xFF1A0C06), Pen(Rock, 2), g);
        // The ground the colonists walk on, with markers scrolling by.
        dc.DrawLine(Pen(0xFF6A3A1A, 2), new Point(0, Field.Ground + 2), new Point(Field.Width, Field.Ground + 2));
        double first = Math.Ceiling(cam / 64) * 64;
        for (double wx = first; wx < cam + Field.Width; wx += 64)
            dc.FillRectangle(Brush(0xFF6A3A1A), new Rect(wx - cam, Field.Ground + 6, 3, 3));
    }

    /// <summary>The gate: a frame of gold with bands of colour rippling through it.</summary>
    private void DrawGate(DrawingContext dc)
    {
        var g = Engine;
        if (!g.OnScreen(g.Gate.X, 60)) return;
        double x = g.ScreenX(g.Gate.X), y = g.Gate.Y, w = Gate.HalfWidth, h = Gate.HalfHeight;
        for (int i = 0; i < 8; i++)
        {
            double t = ((_clock * 1.6 + i / 8.0) % 1);
            double k = 1 - t;
            dc.DrawRectangle(null, Pen(Alpha(Rainbow[i % 6], 0.15 + 0.6 * t), 2), new Rect(x - w * k, y - h * k, 2 * w * k, 2 * h * k));
        }
        dc.DrawRectangle(null, Pen(Alpha(GateGold, 0.35), 8), new Rect(x - w, y - h, 2 * w, 2 * h));
        dc.DrawRectangle(null, Pen(GateGold, 2.5), new Rect(x - w, y - h, 2 * w, 2 * h));
        foreach (var (cx, cy) in new[] { (-w, -h), (w, -h), (-w, h), (w, h) })
            dc.FillRectangle(Brush(White), new Rect(x + cx - 3, y + cy - 3, 6, 6));
    }

    // ───────────────────────────── the ship, the colonists and the shots ─────────────────────────────

    /// <summary>The ship: a long silver dart with a red stripe and a swept fin, its nose the way it's facing; cloaked, just a shimmer.</summary>
    private void DrawShip(DrawingContext dc, Point at, int facing, bool thrusting, bool cloaked, double k)
    {
        Point P(double x, double y) => new(at.X + x * facing * k, at.Y + y * k);
        if (cloaked)
        {
            // A ghost of the ship in shifting colours, sparkling.
            uint c = Rainbow[(int)(_clock * 20) % Rainbow.Length];
            Poly(dc, Alpha(c, 0.18), Alpha(c, 0.9), 2, P(26, 2), P(8, -5), P(-12, -7), P(-20, -14), P(-24, -14), P(-22, 7), P(14, 7));
            for (int i = 0; i < 6; i++)
            {
                uint h = Hash(i, (int)(_clock * 24), 11);
                dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.9)), new Rect(at.X + ((int)(h % 52) - 26) * k, at.Y + ((int)(h / 52 % 20) - 10) * k, 2, 2));
            }
            return;
        }
        if (thrusting)
        {
            double f = 10 + 6 * Math.Sin(_clock * 45);
            Poly(dc, Alpha(Fire, 0.9), null, 0, P(-22, -3), P(-22 - f, 0), P(-22, 4));
            Poly(dc, 0xFFFFF0A0, null, 0, P(-22, -1), P(-22 - f * 0.5, 0.5), P(-22, 2));
        }
        Poly(dc, 0xFFE4E8F4, 0xFF6A7088, 1, P(26, 2), P(8, -5), P(-12, -7), P(-22, -7), P(-22, 7), P(14, 7));
        Poly(dc, 0xFFC03AE0, null, 0, P(-10, -7), P(-20, -15), P(-24, -15), P(-22, -7));
        dc.FillRectangle(Brush(0xFFFF3A5A), new Rect(Math.Min(P(-20, 0).X, P(16, 0).X), at.Y + 1 * k, 36 * k, 2.5 * k));
        dc.DrawEllipse(Brush(0xFF3AA0FF), null, P(4, -3), 6 * k, 2.6 * k);
    }

    /// <summary>A colonist: a little figure with a yellow head, legs going as they walk; arms up when something has hold of them.</summary>
    private void DrawColonist(DrawingContext dc, Point feet, ColonistState state, int walk, double k)
    {
        Point P(double x, double y) => new(feet.X + x * k, feet.Y + y * k);
        bool panic = state is ColonistState.Taken or ColonistState.Falling;
        uint body = panic && (int)(_clock * 8) % 2 == 0 ? 0xFFFFFFFF : Pink;
        dc.DrawEllipse(Brush(0xFFFFE04A), null, P(0, -17), 3.2 * k, 3.2 * k);
        dc.FillRectangle(Brush(body), new Rect(feet.X - 2.5 * k, feet.Y - 14 * k, 5 * k, 8 * k));
        double step = state == ColonistState.Walking ? Math.Sin(_clock * 8 + feet.X) * 2.5 : 1.5;
        dc.DrawLine(Pen(0xFF8A6AFF, 2 * k), P(-1, -6), P(-1 - step, 0));
        dc.DrawLine(Pen(0xFF8A6AFF, 2 * k), P(1, -6), P(1 + step, 0));
        if (panic)
        {
            dc.DrawLine(Pen(body, 1.6 * k), P(-2, -13), P(-6, -19));
            dc.DrawLine(Pen(body, 1.6 * k), P(2, -13), P(6, -19));
        }
        else
        {
            dc.DrawLine(Pen(body, 1.6 * k), P(-2, -13), P(-4 * walk, -8));
            dc.DrawLine(Pen(body, 1.6 * k), P(2, -13), P(3 * walk, -8));
        }
    }

    /// <summary>A laser streak, flickering through the colours, white-hot at its head.</summary>
    private void DrawLaser(DrawingContext dc, Laser l)
    {
        var g = Engine;
        double x0 = g.ScreenX(l.X);
        if (x0 < -1200 || x0 > 2200) return;
        double head = x0 + l.Dir * l.Head, tail = x0 + l.Dir * l.Tail;
        int pieces = 6;
        for (int i = 0; i < pieces; i++)
        {
            double a = tail + (head - tail) * i / pieces, b = tail + (head - tail) * (i + 1) / pieces;
            uint c = Rainbow[(int)(Hash(i, (int)(_clock * 30), 3) % (uint)Rainbow.Length)];
            dc.DrawLine(Pen(Alpha(c, 0.4 + 0.6 * (i + 1) / pieces), 2), new Point(a, l.Y), new Point(b, l.Y));
        }
        dc.DrawLine(Pen(0xFFFFFFFF, 3), new Point(head - l.Dir * 40, l.Y), new Point(head, l.Y));
    }

    private void DrawBolt(DrawingContext dc, Bolt b)
    {
        double x = Engine.ScreenX(b.X), y = b.Y;
        switch (b.Kind)
        {
            case BoltKind.Mine:
            {
                double a = 0.5 + 0.5 * Math.Sin(_clock * 10 + b.X);
                dc.DrawLine(Pen(Alpha(Yellow, 0.6 + 0.4 * a), 2), new Point(x - 4, y - 4), new Point(x + 4, y + 4));
                dc.DrawLine(Pen(Alpha(Yellow, 0.6 + 0.4 * a), 2), new Point(x - 4, y + 4), new Point(x + 4, y - 4));
                dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(x - 1.5, y - 1.5, 3, 3));
                break;
            }
            case BoltKind.Fireball:
                dc.DrawLine(Pen(Alpha(Fire, 0.4), 6), new Point(x, y), new Point(x - b.Vx * 0.06, y - b.Vy * 0.06));
                dc.DrawEllipse(Brush(Fire), null, new Point(x, y), 6, 6);
                dc.DrawEllipse(Brush(0xFFFFF0A0), null, new Point(x, y), 3, 3);
                break;
            default:
            {
                uint c = (int)(_clock * 20 + b.X) % 2 == 0 ? 0xFFFFFFFF : 0xFFFF6AA0;
                dc.FillRectangle(Brush(Alpha(c, 0.3)), new Rect(x - 4, y - 4, 8, 8));
                dc.FillRectangle(Brush(c), new Rect(x - 2, y - 2, 4, 4));
                break;
            }
        }
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    /// <summary>Each of the nine kinds, about (0, 0).</summary>
    private void DrawEnemy(DrawingContext dc, EnemyKind kind, Point at, double t, bool hurt, double k)
    {
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        uint tint = hurt ? 0xFFFFFFFF : GameEngine.Tint(kind);
        switch (kind)
        {
            case EnemyKind.Snatcher:
                // A saucer with a yellow dome and three grabbing claws.
                dc.DrawEllipse(Brush(0xFFFFE04A), null, P(0, -5), 7 * k, 6 * k);
                dc.DrawEllipse(Brush(tint), Pen(0xFF1A6A1A, 1), P(0, 0), 15 * k, 6 * k);
                for (int i = -1; i <= 1; i++)
                {
                    double sway = Math.Sin(t * 5 + i) * 2;
                    dc.DrawLine(Pen(tint, 2 * k), P(i * 8, 4), P(i * 11 + sway, 13));
                }
                for (int i = 0; i < 3; i++)
                    dc.FillRectangle(Brush((int)(t * 6) % 3 == i ? 0xFFFF4A4A : 0xFF0A3A0A), new Rect(at.X + (i - 1) * 7 * k - 1.5, at.Y - 1.5, 3, 3));
                break;
            case EnemyKind.Changeling:
            {
                // Jagged and twitching, purple and green.
                double j = Math.Sin(t * 23) * 2;
                Poly(dc, tint, 0xFF6AFF5A, 1.5, P(0, -15 - j), P(7, -6), P(15 + j, -2), P(7, 3), P(9, 14), P(0, 8), P(-9, 14), P(-7, 3), P(-15 - j, -2), P(-7, -6));
                dc.FillRectangle(Brush(0xFF6AFF5A), new Rect(at.X - 5 * k, at.Y - 5 * k, 3 * k, 3 * k));
                dc.FillRectangle(Brush(0xFF6AFF5A), new Rect(at.X + 2 * k, at.Y - 5 * k, 3 * k, 3 * k));
                break;
            }
            case EnemyKind.Miner:
            {
                // A squat box with a spinning band.
                Poly(dc, 0xFF5A4A10, tint, 2, P(-12, -10), P(12, -10), P(14, 10), P(-14, 10));
                double b = Math.Sin(t * 6) * 9;
                dc.DrawLine(Pen(tint, 3 * k), P(b, -9), P(-b, 9));
                dc.FillRectangle(Brush(Red), new Rect(at.X - 2 * k, at.Y + 10 * k, 4 * k, 4 * k));
                break;
            }
            case EnemyKind.Hive:
            {
                // A swollen bulb, studded with its brood.
                double p = 1 + 0.08 * Math.Sin(t * 4);
                dc.DrawEllipse(Brush(Alpha(tint, 0.85)), Pen(0xFFFFB0C0, 1.5), at, 17 * k * p, 15 * k * p);
                for (int i = 0; i < 6; i++)
                {
                    double a = t * 0.8 + i * Math.PI / 3;
                    dc.DrawEllipse(Brush(Stinger(i)), null, P(Math.Cos(a) * 9, Math.Sin(a) * 8), 3 * k, 3 * k);
                }
                break;
            }
            case EnemyKind.Stinger:
                Poly(dc, tint, null, 0, P(0, -8), P(8, 0), P(0, 8), P(-3, 0));
                dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(at.X - 1.5, at.Y - 1.5, 3, 3));
                break;
            case EnemyKind.Hunter:
            {
                // Long and flat, lights racing along it.
                Poly(dc, 0xFF0A3A3A, tint, 2, P(-26, 0), P(-16, -7), P(16, -7), P(26, 0), P(16, 7), P(-16, 7));
                for (int i = 0; i < 5; i++)
                    dc.FillRectangle(Brush((int)(t * 10) % 5 == i ? 0xFFFFFFFF : tint), new Rect(at.X + (i - 2) * 8 * k - 2, at.Y - 2, 4, 4));
                break;
            }
            case EnemyKind.Firebrand:
            {
                double f = Math.Sin(t * 18) * 3;
                Poly(dc, tint, 0xFFFFF0A0, 1.2, P(0, -14 - f), P(10, 0), P(0, 14 + f), P(-10, 0));
                dc.DrawEllipse(Brush(0xFFFFF0A0), null, at, 4 * k, 4 * k);
                break;
            }
            case EnemyKind.Whirler:
            {
                // A spinning top: a ring round a turning cross.
                dc.DrawEllipse(null, Pen(tint, 3), at, 17 * k, 17 * k);
                for (int i = 0; i < 4; i++)
                {
                    double a = t * 2 + i * Math.PI / 2;
                    Poly(dc, i % 2 == 0 ? 0xFFFFFFFF : tint, null, 0, at, P(Math.Cos(a) * 14, Math.Sin(a) * 14), P(Math.Cos(a + 0.5) * 14, Math.Sin(a + 0.5) * 14));
                }
                dc.DrawEllipse(Brush(Yellow), null, at, 4 * k, 4 * k);
                break;
            }
            default:
            {
                // A Dart: a little fish, tail flicking.
                double f = Math.Sin(t * 20) * 3;
                dc.DrawEllipse(Brush(tint), null, at, 7 * k, 4 * k);
                Poly(dc, 0xFF6AE0FF, null, 0, P(-5, 0), P(-11, -4 + f), P(-11, 4 + f));
                dc.FillRectangle(Brush(0xFF0A0A3A), new Rect(at.X + 3 * k - 1, at.Y - 2, 2, 2));
                break;
            }
        }
    }

    private static uint Stinger(int i) => i % 2 == 0 ? GameEngine.Tint(EnemyKind.Stinger) : 0xFFFFE04A;

    private void DrawEffect(DrawingContext dc, Effect e, double x)
    {
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (8 + 50 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 3), new Point(x, e.Y), r, r * 0.8);
                break;
            }
            case EffectKind.Beam:
                // Something appearing (or the ship going through): flecks drawing in to a point.
                for (int k = 0; k < 12; k++)
                {
                    double a = k * Math.PI / 6, d = 40 * (1 - t);
                    dc.FillRectangle(Brush(Alpha(e.Colour, 1 - t * 0.5)), new Rect(x + Math.Cos(a) * d - 2, e.Y + Math.Sin(a) * d - 2, 4, 4));
                }
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(x, e.Y - t * 24), 26, Alpha(e.Colour, 1 - t * t), center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(x, e.Y - t * 30), 32, Alpha(e.Colour, 1 - t * t), center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and scanner ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 36, Yellow);
        Text(dc, "HI", new Point(1008, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(1008, 44), 30, Dim, right: true);
        if (InGame)
        {
            int spare = Math.Max(0, g.Lives - (g.Ship.Alive ? 1 : 0));
            for (int i = 0; i < Math.Min(spare, 6); i++) DrawShip(dc, new Point(30 + i * 34, 78), 1, false, false, 0.6);
            for (int i = 0; i < Math.Min(g.SmartBombs, 9); i++)
            {
                double x = 30 + i * 18, y = 98;
                Poly(dc, 0xFFFF4A8A, 0xFFFFFFFF, 1, new Point(x - 6, y), new Point(x, y - 5), new Point(x + 6, y), new Point(x, y + 5));
            }
            // The cloak's charge.
            Text(dc, "CLOAK", new Point(870, 78), 20, Dim, right: true);
            dc.DrawRectangle(Brush(0xFF101830), Pen(Dim, 1), new Rect(880, 70, 128, 14));
            uint ec = g.Ship.Cloaked ? Rainbow[(int)(_clock * 20) % Rainbow.Length] : g.Ship.Energy < 25 ? Red : Cyan;
            dc.FillRectangle(Brush(ec), new Rect(882, 72, 124 * g.Ship.Energy / 100, 10));
            Text(dc, $"WAVE {g.Wave}", new Point(1008, 98), 22, White, right: true);
            DrawScanner(dc);
            dc.DrawLine(Pen(0xFF3A2A6A, 2), new Point(0, Field.Top - 4), new Point(1024, Field.Top - 4));
        }
    }

    /// <summary>The scanner: the whole planet at a glance, the screen's stretch of it bracketed in the middle.</summary>
    private void DrawScanner(DrawingContext dc)
    {
        var g = Engine;
        const double left = 256, width = 512, top = 8, height = 92;
        dc.FillRectangle(Brush(0xFF060418), new Rect(left, top, width, height));
        dc.DrawRectangle(null, Pen(0xFF3A2A6A, 2), new Rect(left, top, width, height));
        double centre = Field.Wrap(g.CamX + Field.Width / 2);
        double Sx(double x) => left + width / 2 + Field.Dx(centre, x) / Field.World * width;
        double Sy(double y) => top + 4 + (y - Field.Top) / (Field.Ground - Field.Top) * (height - 10);
        if (!g.DeepSpace && !g.PlanetLost)
        {
            Point? prev = null;
            for (int i = 0; i <= 128; i++)
            {
                double sx = left + i * width / 128;
                double wx = Field.Wrap(centre + (sx - left - width / 2) / width * Field.World);
                var p = new Point(sx, Sy(Field.Ground - Field.Mountain(wx)));
                if (prev is { } q) dc.DrawLine(Pen(Alpha(Rock, 0.7), 1), q, p);
                prev = p;
            }
            foreach (var c in g.Colonists.Where(c => c.State != ColonistState.Dead))
                dc.FillRectangle(Brush(c.State == ColonistState.Walking ? Pink : 0xFFFFFFFF), new Rect(Sx(c.X) - 1, Sy(c.Y) - 3, 2, 3));
        }
        dc.FillRectangle(Brush(GateGold), new Rect(Sx(g.Gate.X) - 2, Sy(g.Gate.Y) - 4, 4, 8));
        foreach (var e in g.Enemies) dc.FillRectangle(Brush(GameEngine.Tint(e.Kind)), new Rect(Sx(e.X) - 1.5, Sy(e.Y) - 1.5, 3, 3));
        if (g.Ship.Alive) dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(Sx(g.Ship.X) - 2.5, Sy(g.Ship.Y) - 1.5, 5, 3));
        // The bracket round what's on the screen.
        double half = Field.Width / Field.World * width / 2;
        foreach (int s in new[] { -1, 1 })
        {
            double x = left + width / 2 + s * half;
            dc.DrawLine(Pen(White, 1.5), new Point(x, top + 2), new Point(x, top + 8));
            dc.DrawLine(Pen(White, 1.5), new Point(x, top + height - 8), new Point(x, top + height - 2));
            dc.DrawLine(Pen(White, 1.5), new Point(x, top + 2), new Point(x - s * 6, top + 2));
            dc.DrawLine(Pen(White, 1.5), new Point(x, top + height - 2), new Point(x - s * 6, top + height - 2));
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = 512, cy = 300;
        var (w, h) = (Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xA0000010), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 12 ? 46 : 58, g.Phase == Phase.Dying || msg.Contains("LOST") ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    // ───────────────────────────── the screens ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = 512;
        // The gate behind the name.
        for (int i = 0; i < 8; i++)
        {
            double t = ((_clock * 0.8 + i / 8.0) % 1), k = 1 - t;
            dc.DrawRectangle(null, Pen(Alpha(Rainbow[i % 6], 0.1 + 0.4 * t), 2), new Rect(cx - 420 * k, 150 - 70 * k, 840 * k, 140 * k));
        }
        Text(dc, "AVA STARGATE", new Point(cx, 140), 112, GateGold, center: true);
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", new Point(cx, 208), 28, Dim, center: true);
        double sweep = (_clock * 260) % 1300 - 140;
        DrawShip(dc, new Point(sweep, 256), 1, true, false, 1.3);
        dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.8), 2), new Point(sweep + 40, 258), new Point(sweep + 260, 258));
        (EnemyKind Kind, string Name)[] roster =
        [
            (EnemyKind.Snatcher, "SNATCHER"), (EnemyKind.Changeling, "CHANGELING"), (EnemyKind.Miner, "MINER"),
            (EnemyKind.Hive, "HIVE"), (EnemyKind.Stinger, "STINGER"), (EnemyKind.Hunter, "HUNTER"),
            (EnemyKind.Firebrand, "FIREBRAND"), (EnemyKind.Whirler, "WHIRLER"), (EnemyKind.Dart, "DART"),
        ];
        for (int i = 0; i < roster.Length; i++)
        {
            double x = cx - 360 + i % 5 * 180 + (i >= 5 ? 90 : 0), y = 320 + i / 5 * 100;
            DrawEnemy(dc, roster[i].Kind, new Point(x, y), _clock + i, false, 1.5);
            Text(dc, roster[i].Name, new Point(x, y + 36), 22, GameEngine.Tint(roster[i].Kind), center: true);
            Text(dc, GameEngine.Points(roster[i].Kind).ToString(), new Point(x, y + 58), 22, White, center: true);
        }
        DrawColonist(dc, new Point(cx - 420, 548), ColonistState.Walking, 1, 1.5);
        DrawColonist(dc, new Point(cx + 420, 548), ColonistState.Walking, -1, 1.5);
        Text(dc, "SAVE THE COLONISTS - CATCH THEM AS THEY FALL - FLY THROUGH THE GATE", new Point(cx, 534), 22, Pink, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 596), 46, Yellow, center: true);
        Text(dc, "ARROWS: FLY    SPACE: FIRE    X: SMART BOMB    C: CLOAK    H: HYPERSPACE", new Point(cx, 650), 23, Dim, center: true);
        Text(dc, $"EXTRA SHIP AND SMART BOMB EVERY {GameEngine.BonusEvery}    P: PAUSE    M: SOUND", new Point(cx, 684), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "GATEKEEPERS", new Point(cx, 170), 72, GateGold, center: true);
        Text(dc, "RANK NAME    SCORE   WAVE", new Point(cx, 230), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 272 + i * 40), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 220), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 290), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 370), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 460), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 500, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 580), 26, White, center: true);
    }
}
