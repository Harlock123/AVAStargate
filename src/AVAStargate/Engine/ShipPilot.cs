namespace AVAStargate.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). It climbs or dives out of the way of anything
/// about to hit it, cloaking if it's too late for that and smart-bombing a crowded screen. A falling colonist
/// comes first: it catches them and takes them down to the ground, or with four aboard to the gate. Otherwise
/// it lines up level with the best target (a Snatcher carrying someone off before anything else), faces it,
/// and fires.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private int _tick;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var s = g.Ship;
        if (!s.Alive) return default;
        if (g.Phase != Phase.Play)
        {
            Doing = "wait";
            return new Controls(0, Toward(s.Y, 400, 8));
        }

        // Anything about to run into us.
        double soonest = 99, threatY = 0;
        void Threat(double x, double y, double vx, double vy, double hw, double hh)
        {
            for (double t = 0; t <= 0.5; t += 0.05)
            {
                double dx = Field.Dx(s.X + s.Vx * t, x + vx * t), dy = y + vy * t - s.Y;
                if (Math.Abs(dx) < 24 + hw && Math.Abs(dy) < 13 + hh)
                {
                    if (t < soonest) { soonest = t; threatY = y + vy * t; }
                    return;
                }
            }
        }
        // Like a player, it takes a moment to see a shot coming.
        foreach (var b in g.Bolts.Where(b => b.Age > 0.15)) Threat(b.X, b.Y, b.Vx, b.Vy, 6, 6);
        foreach (var e in g.Enemies) Threat(e.X, e.Y, e.Vx, e.Vy, e.HalfWidth * 0.8, e.Radius * 0.8);
        bool exposed = s.Safe <= 0.2;

        var onScreen = g.Enemies.Where(e => g.OnScreen(e.X, -10)).ToList();
        int close = onScreen.Count(e => Math.Abs(Field.Dx(s.X, e.X)) < 260);
        bool bomb = g.SmartBombs > 0 && _tick % 2 == 0 &&
                    (onScreen.Count >= 7 || (onScreen.Count >= 4 && close >= 2) || (exposed && soonest < 0.2 && s.Energy < 8 && onScreen.Count >= 2));
        bool cloak = exposed && soonest < 0.3 && s.Energy > 6;

        int dx = 0, dy = 0;
        bool fire = false;
        string doing;
        var falling = g.DeepSpace ? null : g.Colonists.Where(c => c.State == ColonistState.Falling).MinBy(c => Math.Abs(Field.Dx(s.X, c.X)));
        if (falling != null && Math.Abs(Field.Dx(s.X, falling.X)) < 2500 && s.Carrying.Count < GameEngine.MaxCarry)
        {
            doing = "catch";
            (dx, dy) = Reach(s, falling.X, falling.Y - 10 + falling.Vy * 0.08);
        }
        else if (s.Carrying.Count >= 4 && g.Sector == Sector.Planet)
        {
            doing = "gate";
            (dx, dy) = Reach(s, g.Gate.X, g.Gate.Y);
        }
        else if (s.Carrying.Count > 0)
        {
            doing = "set down";
            dy = 1;
        }
        else
        {
            // The best target: near, level, and a Snatcher carrying someone off above all.
            Enemy? best = null;
            double bestCost = double.MaxValue;
            foreach (var e in g.Enemies)
            {
                double cost = Math.Abs(Field.Dx(s.X, e.X)) + 1.5 * Math.Abs(e.Y - s.Y)
                              - (e.Holding != null ? 900 : 0) - (e.Kind == EnemyKind.Hunter ? 200 : 0);
                if (cost < bestCost) { bestCost = cost; best = e; }
            }
            if (best == null)
            {
                doing = "wait";
                dy = Toward(s.Y, 400, 8);
            }
            else
            {
                doing = "attack";
                double ex = Field.Dx(s.X, best.X), dist = Math.Abs(ex);
                int dir = Math.Sign(ex);
                // Close in from afar; turn to face it; otherwise let the ship coast and line up.
                if (dist > 520 || s.Facing != dir) dx = dir;
                double aimY = best.Y + best.Vy * Math.Min(0.4, dist / GameEngine.LaserSpeed + 0.05);
                dy = Toward(s.Y, aimY, 4);
            }
        }
        // Fire whenever something is level and ahead.
        foreach (var e in g.Enemies)
        {
            double ahead = Field.Dx(s.X, e.X) * s.Facing;
            if (ahead > 0 && ahead < 950 && Math.Abs(e.Y - s.Y) < e.Radius + 1) { fire = true; break; }
        }
        // Out of the way: up or down, whichever is away from it and has room.
        if (exposed && soonest < 0.55 && !cloak)
        {
            doing = "evade";
            dy = threatY > s.Y ? -1 : 1;
            if (s.Y < Field.ShipTop + 50) dy = 1;
            if (s.Y > Field.ShipBottom - 50) dy = -1;
        }
        Doing = cloak ? "cloak" : bomb ? "bomb" : doing;
        // Pressing again and again fires quicker than holding the button down.
        return new Controls(dx, dy, fire && _tick % 6 < 3, bomb, cloak);
    }

    private static int Toward(double from, double to, double slack) => Math.Abs(to - from) > slack ? Math.Sign(to - from) : 0;

    /// <summary>Fly to a place: towards it until close enough to coast the rest, and up or down to it.</summary>
    private static (int Dx, int Dy) Reach(Ship s, double x, double y)
    {
        double ex = Field.Dx(s.X, x);
        // Coast in once the ship's speed would carry it there.
        int dx = Math.Abs(ex) > 20 && Math.Abs(ex) > Math.Abs(s.Vx) * 0.45 ? Math.Sign(ex) : 0;
        return (dx, Toward(s.Y, y, 6));
    }
}
