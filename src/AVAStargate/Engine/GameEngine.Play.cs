namespace AVAStargate.Engine;

/// <summary>What sort of wave it is: over the planet, or out in deep space.</summary>
public enum Sector
{
    /// <summary>Over the planet: Snatchers come for the colonists.</summary>
    Planet,
    /// <summary>Every tenth wave from the fifth: Whirlers in deep space, hatching Darts.</summary>
    Swarm,
    /// <summary>Every tenth wave: a showdown with the Firebrands in deep space.</summary>
    Showdown,
}

public sealed partial class GameEngine
{
    public const double Thrust = 1500, MaxSpeed = 720, Drag = 1.6, ClimbSpeed = 400;
    public const double LaserSpeed = 3000, LaserReach = 1100, TapRepeat = 0.09, HoldRepeat = 0.2;
    public const double StartSeconds = 2.2, DyingSeconds = 2.4, SafeSeconds = 2, DoneSeconds = 3.5;
    public const double CloakDrain = 30, CloakRefill = 40, HyperspaceRisk = 0.15;
    public const int StartBombs = 3, Colonies = 10, BonusEvery = 10000, MaxCarry = 8;
    public const int CatchPoints = 500, SetDownPoints = 500, LandPoints = 250, WarpPoints = 1000;

    public static int Points(EnemyKind k) => k switch
    {
        EnemyKind.Snatcher or EnemyKind.Changeling or EnemyKind.Stinger => 150,
        EnemyKind.Miner or EnemyKind.Firebrand => 250,
        EnemyKind.Hive => 1000,
        EnemyKind.Hunter => 200,
        EnemyKind.Whirler => 300,
        _ => 100,
    };

    public static string NameOf(EnemyKind k) => k.ToString().ToUpperInvariant();

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public int SmartBombs { get; private set; }
    public Sector Sector { get; private set; }
    public Ship Ship { get; } = new();
    public Gate Gate { get; } = new();
    public List<Laser> Lasers { get; } = [];
    public List<Bolt> Bolts { get; } = [];
    public List<Colonist> Colonists { get; } = [];
    public List<Enemy> Enemies { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double WaveTime { get; private set; }
    /// <summary>How many more Snatchers the wave has still to send.</summary>
    public int ToCome { get; private set; }
    /// <summary>Every colonist is gone: the planet is lost until it's restored, every fifth wave.</summary>
    public bool PlanetLost { get; private set; }
    /// <summary>The flash of a smart bomb, counting down.</summary>
    public double Flash { get; private set; }
    /// <summary>The bonus tallied at the end of the wave.</summary>
    public int Bonus { get; private set; }
    /// <summary>For tests: nothing new comes, and nothing fires, by itself.</summary>
    internal bool Calm { get; set; }

    private int _nextBonus;
    private double _groupIn, _hunterIn, _thrustSound, _sinceFire = 9;
    private bool _firePrev, _bombPrev, _hyperPrev, _warpAhead;

    public bool DeepSpace => Sector != Sector.Planet;
    public int Remaining => ToCome + Enemies.Count(e => !e.Dead && e.Kind != EnemyKind.Hunter);
    public int ColonistsLeft => Colonists.Count(c => c.State != ColonistState.Dead);
    private int MaxBolts => Math.Min(18, 6 + Wave);

    /// <summary>The planet's left edge of the screen.</summary>
    public double CamX => Field.Wrap(Ship.X - Ship.Offset);

    /// <summary>Where a place on the planet is on the screen (off the left edge is negative).</summary>
    public double ScreenX(double x)
    {
        double d = Field.Wrap(x - CamX);
        return d > Field.World - 400 ? d - Field.World : d;
    }

    public bool OnScreen(double x, double margin = 0)
    {
        double sx = ScreenX(x);
        return sx >= -margin && sx <= Field.Width + margin;
    }

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        SmartBombs = StartBombs;
        Wave = 1;
        _nextBonus = BonusEvery;
        var s = Ship;
        s.X = Field.World / 2;
        s.Facing = 1;
        s.Offset = 300;
        s.Energy = 100;
        s.Carrying.Clear();
        ResetShip();
        Clear();
        Colonists.Clear();
        Wreckage.Clear();
        PlanetLost = false;
        Populate();
        SetPhase(Phase.Start);
    }

    private void Clear()
    {
        Lasers.Clear();
        Bolts.Clear();
        Enemies.Clear();
    }

    /// <summary>Ten colonists, spread round the planet.</summary>
    private void Populate()
    {
        Colonists.RemoveAll(c => c.State == ColonistState.Dead);
        while (Colonists.Count < Colonies)
            Colonists.Add(new Colonist { X = _rng.NextDouble() * Field.World, Walk = _rng.Next(2) * 2 - 1 });
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        WaveTime = 0;
        Bonus = 0;
        Clear();
        Sector = wave % 10 == 0 ? Sector.Showdown : wave % 10 == 5 ? Sector.Swarm : Sector.Planet;
        Ship.Energy = Math.Min(100, Ship.Energy + (wave > 1 ? CloakRefill : 0));
        Ship.Cloaked = false;
        _hunterIn = HunterAfter;
        string? sub = Sector switch { Sector.Swarm => "WHIRLER SWARM", Sector.Showdown => "FIREBRAND SHOWDOWN", _ => null };
        // Every fifth wave the planet is made whole again.
        if (wave % 5 == 1 && wave > 1 && ColonistsLeft < Colonies)
        {
            PlanetLost = false;
            Populate();
            sub = "THE PLANET IS RESTORED";
            Play(Sfx.PlanetBack);
        }
        // Everyone back on their feet.
        foreach (var c in Colonists.Where(c => c.State != ColonistState.Dead))
        {
            if (c.State == ColonistState.Carried) continue;
            c.State = ColonistState.Walking;
            c.Y = Field.Ground;
            c.Vy = 0;
            c.Wanted = null;
        }
        switch (Sector)
        {
            case Sector.Planet:
                ToCome = Math.Min(30, 15 + 2 * wave);
                _groupIn = 0.1;
                Put(EnemyKind.Miner, wave == 1 ? 1 : Math.Min(6, 1 + wave / 2));
                Put(EnemyKind.Hive, Math.Min(4, (wave + 1) / 2));
                Put(EnemyKind.Firebrand, wave >= 2 ? Math.Min(5, wave / 2) : 0);
                break;
            case Sector.Swarm:
                ToCome = 0;
                Put(EnemyKind.Whirler, Math.Min(10, 6 + wave / 10));
                break;
            case Sector.Showdown:
                ToCome = 0;
                Put(EnemyKind.Firebrand, Math.Min(14, 7 + wave / 10));
                Put(EnemyKind.Miner, 2);
                Put(EnemyKind.Hive, 2);
                break;
        }
        SetPhase(Phase.Start);
        Say($"WAVE {wave}", sub, StartSeconds + 0.5);
    }

    private void ResetShip()
    {
        var s = Ship;
        s.Y = 400;
        s.Vx = 0;
        s.Alive = true;
        s.Cloaked = false;
        s.FireCool = 0;
        s.Safe = SafeSeconds;
        s.GateCool = 0;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        Flash = Math.Max(0, Flash - dt);
        switch (Phase)
        {
            case Phase.Start:
                FlyShip(c, dt, canFire: false);
                MoveColonists(dt);
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    Play(Sfx.WaveStart);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                WaveTime += dt;
                FlyShip(c, dt, canFire: true);
                if (_warpAhead) { WarpAhead(); break; }
                if (Phase != Phase.Play) break;
                MoveLasers(dt);
                Spawn(dt);
                MoveEnemies(dt);
                MoveBolts(dt);
                MoveColonists(dt);
                Hits();
                if (Phase == Phase.Play) Progress();
                break;
            case Phase.Dying:
                MoveLasers(dt);
                MoveEnemies(dt);
                MoveBolts(dt);
                MoveColonists(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    ResetShip();
                    // Nothing waiting right where the ship comes back.
                    foreach (var e in Enemies.Where(e => Math.Abs(Field.Dx(Ship.X, e.X)) < 260 && Math.Abs(e.Y - Ship.Y) < 160))
                        e.Y = e.Y < Ship.Y ? Math.Max(Field.Top + 20, Ship.Y - 200) : Math.Min(Field.Ground - 40, Ship.Y + 200);
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Done:
                FlyShip(c, dt, canFire: false);
                MoveLasers(dt);
                MoveColonists(dt);
                if (PhaseTime > DoneSeconds) BeginWave(Wave + 1);
                break;
        }
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X = Field.Wrap(d.X + d.Vx * dt);
            d.Y += d.Vy * dt;
        }
        Wreckage.RemoveAll(d => d.Age > d.Life);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    private void FlyShip(Controls c, double dt, bool canFire)
    {
        var s = Ship;
        bool fresh = c.Fire && !_firePrev, bomb = c.Bomb && !_bombPrev, hyper = c.Hyper && !_hyperPrev;
        _firePrev = c.Fire;
        _bombPrev = c.Bomb;
        _hyperPrev = c.Hyper;
        if (!s.Alive) return;
        s.Safe = Math.Max(0, s.Safe - dt);
        s.GateCool = Math.Max(0, s.GateCool - dt);
        s.Thrusting = c.Dx != 0;
        if (s.Thrusting)
        {
            s.Facing = Math.Sign(c.Dx);
            s.Vx += s.Facing * Thrust * dt;
            _thrustSound -= dt;
            if (_thrustSound <= 0) { _thrustSound = 0.3; Play(Sfx.Thrust); }
        }
        else s.Vx *= Math.Max(0, 1 - Drag * dt);
        s.Vx = Math.Clamp(s.Vx, -MaxSpeed, MaxSpeed);
        s.X = Field.Wrap(s.X + s.Vx * dt);
        s.Y = Math.Clamp(s.Y + Math.Sign(c.Dy) * ClimbSpeed * dt, Field.ShipTop, Field.ShipBottom);
        // The ship slides across the screen when it turns round, so there's always room to see ahead.
        double want = s.Facing > 0 ? 300 : Field.Width - 300;
        double slide = (700 + Math.Abs(s.Vx)) * dt;
        s.Offset += Math.Clamp(want - s.Offset, -slide, slide);

        bool cloak = canFire && c.Cloak && s.Energy > 0;
        if (cloak && !s.Cloaked) Play(Sfx.Cloak);
        s.Cloaked = cloak;
        if (s.Cloaked) s.Energy = Math.Max(0, s.Energy - CloakDrain * dt);

        _sinceFire += dt;
        if (canFire && Lasers.Count < 4 && ((fresh && _sinceFire >= TapRepeat) || (c.Fire && _sinceFire >= HoldRepeat)))
        {
            _sinceFire = 0;
            Lasers.Add(new Laser { X = Field.Wrap(s.X + s.Facing * 24), Y = s.Y + 2, Dir = s.Facing });
            Play(Sfx.Fire);
        }
        if (!canFire) return;
        if (bomb && SmartBombs > 0) SmartBomb();
        if (hyper) Hyperspace();
        if (!s.Alive) return;
        if (s.GateCool <= 0 && Math.Abs(Field.Dx(s.X, Gate.X)) < Gate.HalfWidth && Math.Abs(s.Y - Gate.Y) < Gate.HalfHeight) EnterGate();
    }

    private void MoveLasers(double dt)
    {
        foreach (var l in Lasers)
        {
            l.Head += LaserSpeed * dt;
            l.Age += dt;
            // It stops at the first thing in its way.
            Enemy? hit = null;
            double nearest = double.MaxValue;
            foreach (var e in Enemies)
            {
                if (e.Dead || Math.Abs(e.Y - l.Y) > e.Radius + 3) continue;
                double along = Field.Dx(l.X, e.X) * l.Dir;
                if (along < l.Tail - e.HalfWidth || along > l.Head + e.HalfWidth || along >= nearest) continue;
                nearest = along;
                hit = e;
            }
            if (hit != null)
            {
                l.Dead = true;
                // A Whirler takes three hits.
                if (--hit.Hp > 0)
                {
                    hit.Hurt = 0.15;
                    Play(Sfx.Explode);
                }
                else Kill(hit);
            }
            if (l.Head > LaserReach) l.Dead = true;
        }
        Lasers.RemoveAll(l => l.Dead);
        Enemies.RemoveAll(e => e.Dead);
    }

    /// <summary>Everything on the screen, gone in a flash.</summary>
    private void SmartBomb()
    {
        SmartBombs--;
        Flash = 0.3;
        Play(Sfx.SmartBomb);
        foreach (var e in Enemies.Where(e => !e.Dead && OnScreen(e.X, 20)).ToList()) Kill(e, quiet: true, hatch: false);
        foreach (var b in Bolts.Where(b => OnScreen(b.X, 20))) b.Dead = true;
        Enemies.RemoveAll(e => e.Dead);
        Bolts.RemoveAll(b => b.Dead);
    }

    /// <summary>Somewhere else on the planet, at once; it doesn't always come out the other side.</summary>
    private void Hyperspace()
    {
        var s = Ship;
        s.X = _rng.NextDouble() * Field.World;
        s.Y = Field.ShipTop + 60 + _rng.NextDouble() * (Field.ShipBottom - Field.ShipTop - 120);
        s.Vx = 0;
        s.Offset = s.Facing > 0 ? 300 : Field.Width - 300;
        Lasers.Clear();
        Effects.Add(new Effect { Kind = EffectKind.Beam, X = s.X, Y = s.Y, Life = 0.6, Colour = 0xFFB0E0FF });
        Play(Sfx.Hyperspace);
        if (_rng.NextDouble() < HyperspaceRisk) Die("LOST IN HYPERSPACE");
        else s.Safe = Math.Max(s.Safe, 0.6);
    }

    /// <summary>
    /// The gate throws the ship to the colonist in most danger: beside the Snatcher carrying them highest. With
    /// four or more colonists aboard it warps the ship three waves ahead instead.
    /// </summary>
    private void EnterGate()
    {
        var s = Ship;
        s.GateCool = 2;
        Effects.Add(new Effect { Kind = EffectKind.Beam, X = s.X, Y = s.Y, Life = 0.6, Colour = 0xFFFFE04A });
        if (Sector == Sector.Planet && s.Carrying.Count >= 4)
        {
            _warpAhead = true;
            return;
        }
        var snatcher = Enemies.Where(e => !e.Dead && e.Kind == EnemyKind.Snatcher && e.Holding != null).MinBy(e => e.Y);
        if (snatcher != null)
        {
            s.X = Field.Wrap(snatcher.X - 280 * s.Facing);
            s.Y = Math.Clamp(snatcher.Y, Field.ShipTop, Field.ShipBottom);
        }
        else s.X = Field.Wrap(s.X + Field.World / 2);
        s.Vx = 0;
        s.Offset = s.Facing > 0 ? 300 : Field.Width - 300;
        Lasers.Clear();
        Effects.Add(new Effect { Kind = EffectKind.Beam, X = s.X, Y = s.Y, Life = 0.6, Colour = 0xFFFFE04A });
        Play(Sfx.Gate);
    }

    private void WarpAhead()
    {
        _warpAhead = false;
        var s = Ship;
        int n = s.Carrying.Count;
        foreach (var c in s.Carrying)
        {
            c.State = ColonistState.Walking;
            c.X = _rng.NextDouble() * Field.World;
            c.Y = Field.Ground;
        }
        s.Carrying.Clear();
        AddScore(n * WarpPoints);
        Play(Sfx.WarpAhead);
        BeginWave(Wave + 3);
        Say("STARGATE WARP", $"THREE WAVES AHEAD  +{n * WarpPoints}", StartSeconds + 0.5);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    private double HunterAfter => Math.Max(18, 34 - 2 * Wave);

    /// <summary>Somewhere well away from the ship for something to appear.</summary>
    private (double X, double Y) Away()
    {
        double x;
        do x = _rng.NextDouble() * Field.World;
        while (Math.Abs(Field.Dx(Ship.X, x)) < 700);
        return (x, Field.Top + 40 + _rng.NextDouble() * 400);
    }

    private void Put(EnemyKind kind, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var (x, y) = Away();
            Appear(kind, x, y);
        }
    }

    private Enemy Appear(EnemyKind kind, double x, double y)
    {
        var e = new Enemy
        {
            Kind = kind, X = x, Y = y, Dir = _rng.Next(2) * 2 - 1,
            Vy = (_rng.NextDouble() < 0.5 ? -1 : 1) * (40 + _rng.NextDouble() * 50),
            FireIn = 1.5 + _rng.NextDouble() * 2,
            // A Snatcher's patience before it picks a colonist.
            Spin = kind == EnemyKind.Snatcher ? 2 + _rng.NextDouble() * 6 : _rng.NextDouble() * 6,
        };
        if (kind == EnemyKind.Whirler) e.Hp = 3;
        Enemies.Add(e);
        if (OnScreen(x, 40)) Effects.Add(new Effect { Kind = EffectKind.Beam, X = x, Y = y, Life = 0.5, Colour = Tint(kind) });
        return e;
    }

    public static uint Tint(EnemyKind k) => k switch
    {
        EnemyKind.Snatcher => 0xFF6AFF5A,
        EnemyKind.Changeling => 0xFFC05AFF,
        EnemyKind.Miner => 0xFFFFD84A,
        EnemyKind.Hive => 0xFFFF4A6A,
        EnemyKind.Stinger => 0xFFFF9A4A,
        EnemyKind.Hunter => 0xFF4AFFE0,
        EnemyKind.Firebrand => 0xFFFF6A2A,
        EnemyKind.Whirler => 0xFF4A9AFF,
        _ => 0xFFFFFFFF,
    };

    /// <summary>The Snatchers come in fives; a Hunter comes when the wave drags on.</summary>
    private void Spawn(double dt)
    {
        if (Calm) return;
        _groupIn -= dt;
        int snatchers = Enemies.Count(e => e.Kind is EnemyKind.Snatcher or EnemyKind.Changeling);
        if (ToCome > 0 && (_groupIn <= 0 || snatchers < 3))
        {
            _groupIn = Math.Max(8, 16 - Wave);
            int n = Math.Min(5, ToCome);
            ToCome -= n;
            for (int i = 0; i < n; i++)
            {
                var (x, y) = Away();
                Appear(PlanetLost ? EnemyKind.Changeling : EnemyKind.Snatcher, x, Field.Top + 30 + _rng.NextDouble() * 160);
            }
            Play(Sfx.Materialise);
        }
        _hunterIn -= dt;
        if (_hunterIn <= 0 && WaveTime > HunterAfter && Enemies.Count(e => e.Kind == EnemyKind.Hunter) < 3)
        {
            _hunterIn = Math.Max(6, 14 - Wave);
            int side = _rng.Next(2) * 2 - 1;
            Appear(EnemyKind.Hunter, Field.Wrap(Ship.X + side * 620), Field.Top + 40 + _rng.NextDouble() * 400);
            Play(Sfx.HunterIn);
        }
    }

    private double RiseSpeed => Math.Min(80, 34 + 4 * Wave);

    private void MoveEnemies(double dt)
    {
        var s = Ship;
        foreach (var e in Enemies.ToList())
        {
            if (e.Dead) continue;
            e.T += dt;
            e.Hurt = Math.Max(0, e.Hurt - dt);
            switch (e.Kind)
            {
                case EnemyKind.Snatcher: MoveSnatcher(e, dt); break;
                case EnemyKind.Changeling:
                {
                    double speed = Math.Min(360, 240 + 8 * Wave);
                    double dx = Field.Dx(e.X, s.X);
                    e.Vx += Math.Clamp(Math.Sign(dx) * speed - e.Vx, -700 * dt, 700 * dt);
                    // Jittery, but always closing.
                    e.Vy = Math.Sign(s.Y - e.Y) * 150 + Math.Sin(e.T * 13 + e.Spin) * 220;
                    Fly(e, dt);
                    Shoot(e, Math.Max(0.45, 0.9 - 0.04 * Wave));
                    break;
                }
                case EnemyKind.Miner:
                    e.Vx = e.Dir * 90;
                    Fly(e, dt, bottom: Field.Ground - 150);
                    e.FireIn -= dt;
                    if (e.FireIn <= 0 && !Calm)
                    {
                        e.FireIn = 1.2;
                        if (Bolts.Count(b => b.Kind == BoltKind.Mine) < 40)
                        {
                            Bolts.Add(new Bolt { Kind = BoltKind.Mine, X = e.X, Y = e.Y, Life = 8 });
                            if (OnScreen(e.X)) Play(Sfx.Mine);
                        }
                    }
                    break;
                case EnemyKind.Hive:
                    e.Vx = e.Dir * 50;
                    e.Spin += dt;
                    Fly(e, dt);
                    break;
                case EnemyKind.Stinger:
                {
                    double speed = Math.Min(420, 300 + 6 * Wave);
                    double dx = Field.Dx(e.X, s.X);
                    // They swoop past and turn back, rather than sitting on the ship.
                    e.Vx += Math.Clamp(Math.Sign(dx) * speed - e.Vx, -520 * dt, 520 * dt);
                    e.Vy = Math.Sin(e.T * 4 + e.Spin) * 130 + Math.Sign(s.Y - e.Y) * 70;
                    Fly(e, dt);
                    Shoot(e, 1.5);
                    break;
                }
                case EnemyKind.Hunter:
                {
                    // As quick as the ship: it hangs about just off its nose or its tail, firing.
                    double dx = Field.Dx(e.X, s.X + (e.Spin > 3 ? 1 : -1) * 240);
                    e.Vx = s.Vx + Math.Clamp(dx * 2.5, -420, 420);
                    e.Vy = Math.Clamp((s.Y + Math.Sin(e.T * 2) * 70 - e.Y) * 2.5, -300, 300);
                    Fly(e, dt);
                    Shoot(e, Math.Max(0.55, 0.95 - 0.03 * Wave));
                    break;
                }
                case EnemyKind.Firebrand:
                {
                    e.Vx = e.Dir * 130;
                    // A zigzag: up for a while, then down.
                    e.Vy = ((int)(e.T / 0.7 + e.Spin) % 2 == 0 ? 1 : -1) * 170;
                    Fly(e, dt);
                    e.FireIn -= dt;
                    if (e.FireIn <= 0 && CanFire(e))
                    {
                        e.FireIn = Math.Max(1.0, 2.0 - 0.05 * Wave) * (0.7 + 0.6 * _rng.NextDouble());
                        double a = Math.Atan2(s.Y - e.Y, Field.Dx(e.X, s.X));
                        Bolts.Add(new Bolt { Kind = BoltKind.Fireball, X = e.X, Y = e.Y, Vx = Math.Cos(a) * 210, Vy = Math.Sin(a) * 210, Life = 3.5 });
                        Play(Sfx.Fireball);
                    }
                    break;
                }
                case EnemyKind.Whirler:
                    e.Vx = e.Dir * 60;
                    e.Spin += dt * 5;
                    Fly(e, dt);
                    Shoot(e, 1.6);
                    // It hatches Darts while the ship is about.
                    if (!Calm && Math.Abs(Field.Dx(e.X, s.X)) < 1300 && e.T > 2.2)
                    {
                        int darts = Enemies.Count(d => d.Kind == EnemyKind.Dart);
                        if (darts < Math.Min(24, 4 * Enemies.Count(w => w.Kind == EnemyKind.Whirler)))
                        {
                            e.T = 0;
                            var d = Appear(EnemyKind.Dart, e.X, e.Y);
                            d.Vx = e.Dir * 120;
                            if (OnScreen(e.X)) Play(Sfx.Hatch);
                        }
                    }
                    break;
                case EnemyKind.Dart:
                {
                    double speed = Math.Min(320, 230 + 5 * Wave);
                    double dx = Field.Dx(e.X, s.X), dy = s.Y - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    e.Vx += dx / d * 520 * dt;
                    e.Vy += dy / d * 520 * dt + Math.Sin(e.T * 9 + e.Spin) * 300 * dt;
                    double v = Math.Sqrt(e.Vx * e.Vx + e.Vy * e.Vy);
                    if (v > speed) { e.Vx *= speed / v; e.Vy *= speed / v; }
                    Fly(e, dt);
                    break;
                }
            }
        }
        Enemies.RemoveAll(e => e.Dead);
    }

    /// <summary>Moves it along, bouncing between the top of the sky and the mountains.</summary>
    private static void Fly(Enemy e, double dt, double bottom = Field.Ground - 40)
    {
        e.X = Field.Wrap(e.X + e.Vx * dt);
        e.Y += e.Vy * dt;
        if (e.Y < Field.Top + 14) { e.Y = Field.Top + 14; e.Vy = Math.Abs(e.Vy); }
        if (e.Y > bottom) { e.Y = bottom; e.Vy = -Math.Abs(e.Vy); }
    }

    /// <summary>
    /// A Snatcher cruises along until it picks a colonist; then it flies over them, drops down, takes hold and
    /// climbs. If it gets them to the top of the sky it becomes a Changeling, and the colonist is lost.
    /// </summary>
    private void MoveSnatcher(Enemy e, double dt)
    {
        if (e.Holding is { } held)
        {
            e.Vx = Math.Sin(e.T * 2) * 30;
            e.Vy = -RiseSpeed;
            e.X = Field.Wrap(e.X + e.Vx * dt);
            e.Y += e.Vy * dt;
            held.X = e.X;
            held.Y = e.Y + 26;
            if (e.Y <= Field.Top + 6) Mutate(e);
            return;
        }
        if (e.Target is { } t && (t.State != ColonistState.Walking || DeepSpace))
        {
            t.Wanted = null;
            e.Target = null;
            e.Diving = false;
        }
        if (e.Target == null && !DeepSpace && e.T > e.Spin)
        {
            var pick = Colonists.Where(c => c.State == ColonistState.Walking && c.Wanted == null).MinBy(c => Math.Abs(Field.Dx(e.X, c.X)));
            if (pick != null)
            {
                e.Target = pick;
                pick.Wanted = e;
            }
        }
        if (e.Target is { } target)
        {
            double dx = Field.Dx(e.X, target.X);
            if (!e.Diving && Math.Abs(dx) < 10) e.Diving = true;
            e.Vx = Math.Clamp(dx * 4, -130, 130);
            if (e.Diving)
            {
                e.Vy = 140;
                e.X = Field.Wrap(e.X + e.Vx * dt);
                e.Y = Math.Min(target.Y - 26, e.Y + e.Vy * dt);
                if (e.Y >= target.Y - 26.5)
                {
                    e.Holding = target;
                    target.State = ColonistState.Taken;
                    target.Wanted = null;
                    e.Target = null;
                    e.Diving = false;
                    Play(Sfx.Snatch);
                }
                return;
            }
            // Cruising over to them, a little above the mountains.
            e.Vy = Math.Clamp((Field.Ground - 230 - e.Y) * 1.5, -90, 90);
            e.X = Field.Wrap(e.X + e.Vx * dt);
            e.Y += e.Vy * dt;
        }
        else
        {
            e.Vx = e.Dir * (70 + 4 * Math.Min(Wave, 10));
            e.Vy = Math.Sin(e.T * 1.3 + e.Spin) * 50;
            Fly(e, dt, bottom: Field.Ground - 180);
        }
        Shoot(e, Math.Max(0.7, 1.6 - 0.08 * Wave));
    }

    private void Mutate(Enemy e)
    {
        if (e.Holding is { } c)
        {
            c.State = ColonistState.Dead;
            Shatter(c.X, c.Y, 8, 0xFFFF9AC0);
            Play(Sfx.ColonistLost);
        }
        e.Holding = null;
        e.Kind = EnemyKind.Changeling;
        e.FireIn = 1;
        if (OnScreen(e.X, 40)) Effects.Add(new Effect { Kind = EffectKind.Burst, X = e.X, Y = e.Y, Life = 0.5, Colour = Tint(EnemyKind.Changeling) });
        Play(Sfx.Mutate);
        CheckPlanet();
    }

    /// <summary>The last colonist gone: the planet is lost, and every Snatcher becomes a Changeling.</summary>
    private void CheckPlanet()
    {
        if (PlanetLost || DeepSpace || ColonistsLeft > 0) return;
        PlanetLost = true;
        foreach (var e in Enemies.Where(e => e.Kind == EnemyKind.Snatcher))
        {
            e.Kind = EnemyKind.Changeling;
            e.Target = null;
            e.Diving = false;
        }
        Flash = 0.6;
        Say("THE PLANET IS LOST", null, 3);
        Play(Sfx.PlanetLost);
    }

    private bool CanFire(Enemy e) => !Calm && Phase == Phase.Play && Ship.Alive && OnScreen(e.X, -20) && Bolts.Count(b => b.Kind != BoltKind.Mine) < MaxBolts;

    /// <summary>A shot at the ship, leading it a little, now and then; only from things on the screen.</summary>
    private void Shoot(Enemy e, double every)
    {
        e.FireIn -= Field.Step;
        if (e.FireIn > 0 || !CanFire(e)) return;
        e.FireIn = every * (0.7 + 0.6 * _rng.NextDouble());
        var s = Ship;
        double speed = Math.Min(420, 260 + 10 * Wave);
        double dx = Field.Dx(e.X, s.X), dy = s.Y - e.Y;
        double t = Math.Sqrt(dx * dx + dy * dy) / speed;
        dx += s.Vx * t * 0.7;
        double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        Bolts.Add(new Bolt { Kind = BoltKind.Shot, X = e.X, Y = e.Y, Vx = dx / d * speed, Vy = dy / d * speed, Life = 3 });
        Play(Sfx.EnemyFire);
    }

    private void MoveBolts(double dt)
    {
        var s = Ship;
        foreach (var b in Bolts)
        {
            b.Age += dt;
            b.Life -= dt;
            if (b.Kind == BoltKind.Fireball && b.Age < 1.4 && s.Alive)
            {
                // A fireball curls after the ship for a moment.
                double want = Math.Atan2(s.Y - b.Y, Field.Dx(b.X, s.X)), now = Math.Atan2(b.Vy, b.Vx);
                double turn = Math.Clamp(Math.IEEERemainder(want - now, Math.PI * 2), -1.3 * dt, 1.3 * dt);
                double v = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
                b.Vx = Math.Cos(now + turn) * v;
                b.Vy = Math.Sin(now + turn) * v;
            }
            b.X = Field.Wrap(b.X + b.Vx * dt);
            b.Y += b.Vy * dt;
            if (b.Life <= 0 || b.Y < Field.Top || b.Y > Field.Ground + 10) b.Dead = true;
        }
        Bolts.RemoveAll(b => b.Dead);
    }

    // ───────────────────────────── the colonists ─────────────────────────────

    private void MoveColonists(double dt)
    {
        if (DeepSpace) return;
        var s = Ship;
        foreach (var c in Colonists)
        {
            switch (c.State)
            {
                case ColonistState.Walking:
                    if (_rng.NextDouble() < 0.004) c.Walk = -c.Walk;
                    c.X = Field.Wrap(c.X + c.Walk * 14 * dt);
                    break;
                case ColonistState.Falling:
                    c.Vy = Math.Min(280, c.Vy + 200 * dt);
                    c.Y += c.Vy * dt;
                    if (c.Y >= Field.Ground)
                    {
                        c.Y = Field.Ground;
                        c.Vy = 0;
                        if (Field.Ground - c.FellFrom > 300)
                        {
                            c.State = ColonistState.Dead;
                            Shatter(c.X, c.Y - 8, 8, 0xFFFF9AC0);
                            Play(Sfx.ColonistLost);
                        }
                        else
                        {
                            c.State = ColonistState.Walking;
                            AddScore(LandPoints);
                            Effects.Add(new Effect { Kind = EffectKind.Score, X = c.X, Y = c.Y - 30, Text = LandPoints.ToString() });
                        }
                    }
                    break;
                case ColonistState.Carried:
                {
                    int i = s.Carrying.IndexOf(c);
                    c.X = Field.Wrap(s.X - s.Facing * (6 - i % 2 * 12));
                    c.Y = s.Y + 22 + i * 13;
                    break;
                }
            }
        }
        // Put down gently when the ship comes down to the ground.
        if (s.Alive && s.Carrying.Count > 0 && s.Y >= Field.ShipBottom - 6 && Phase != Phase.Dying)
        {
            int k = 0;
            foreach (var c in s.Carrying)
            {
                c.State = ColonistState.Walking;
                c.X = Field.Wrap(s.X + (k++ - s.Carrying.Count / 2.0) * 16);
                c.Y = Field.Ground;
                AddScore(SetDownPoints);
            }
            Effects.Add(new Effect { Kind = EffectKind.Score, X = s.X, Y = s.Y - 30, Text = (SetDownPoints * s.Carrying.Count).ToString(), Colour = 0xFFFFE04A });
            s.Carrying.Clear();
            Play(Sfx.SetDown);
        }
        CheckPlanet();
    }

    // ───────────────────────────── hits ─────────────────────────────

    private void Hits()
    {
        var s = Ship;
        if (!s.Alive) return;
        // Catching a falling colonist.
        foreach (var c in Colonists)
        {
            if (c.State != ColonistState.Falling || s.Carrying.Count >= MaxCarry) continue;
            if (Math.Abs(Field.Dx(s.X, c.X)) > 28 || Math.Abs(c.Y - 10 - s.Y) > 28) continue;
            c.State = ColonistState.Carried;
            s.Carrying.Add(c);
            AddScore(CatchPoints);
            Effects.Add(new Effect { Kind = EffectKind.Score, X = c.X, Y = c.Y - 24, Text = CatchPoints.ToString(), Colour = 0xFFFFE04A });
            Play(Sfx.Catch);
        }
        foreach (var e in Enemies.ToList())
        {
            if (e.Dead || Math.Abs(Field.Dx(s.X, e.X)) > 18 + e.HalfWidth * 0.8 || Math.Abs(e.Y - s.Y) > 8 + e.Radius * 0.8) continue;
            if (s.Cloaked) { Kill(e); continue; }
            if (s.Safe > 0) continue;
            Kill(e, quiet: true);
            Enemies.RemoveAll(x => x.Dead);
            Die($"RAMMED BY A {NameOf(e.Kind)}");
            return;
        }
        Enemies.RemoveAll(e => e.Dead);
        foreach (var b in Bolts)
        {
            double reach = b.Kind switch { BoltKind.Mine => 6, BoltKind.Fireball => 8, _ => 4 };
            if (Math.Abs(Field.Dx(s.X, b.X)) > 18 + reach || Math.Abs(b.Y - s.Y) > 7 + reach) continue;
            b.Dead = true;
            if (s.Cloaked || s.Safe > 0) continue;
            Die(b.Kind switch { BoltKind.Mine => "MINED", BoltKind.Fireball => "FIREBALLED", _ => "SHOT DOWN" });
            return;
        }
        Bolts.RemoveAll(b => b.Dead);
    }

    private void Kill(Enemy e, bool quiet = false, bool hatch = true)
    {
        if (e.Dead) return;
        e.Dead = true;
        int points = Points(e.Kind);
        AddScore(points);
        bool big = e.Kind is EnemyKind.Hive or EnemyKind.Whirler or EnemyKind.Hunter;
        Shatter(e.X, e.Y, big ? 22 : 14, Tint(e.Kind));
        if (points >= 250) Effects.Add(new Effect { Kind = EffectKind.Score, X = e.X, Y = e.Y - 22, Text = points.ToString(), Colour = points >= 1000 ? 0xFFFFE04A : 0xFFFFFFFF });
        if (!quiet) Play(big ? Sfx.BigExplode : Sfx.Explode);
        if (e.Target is { } t) t.Wanted = null;
        // The colonist it had hold of falls.
        if (e.Holding is { } c)
        {
            c.State = ColonistState.Falling;
            c.FellFrom = c.Y;
            c.Vy = 0;
            Play(Sfx.Scream);
        }
        // A Hive bursts into Stingers.
        if (e.Kind == EnemyKind.Hive && hatch)
        {
            int n = Math.Min(8, 5 + Wave / 4);
            for (int i = 0; i < n; i++)
            {
                var st = Appear(EnemyKind.Stinger, Field.Wrap(e.X + (_rng.NextDouble() - 0.5) * 40), e.Y + (_rng.NextDouble() - 0.5) * 40);
                st.Vx = (_rng.NextDouble() - 0.5) * 300;
                st.FireIn = 1.5 + _rng.NextDouble() * 2;
            }
        }
    }

    private void Shatter(double x, double y, int n, uint colour)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 60 + _rng.NextDouble() * 220;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = 0.5 + _rng.NextDouble() * 0.6, Colour = i % 3 == 0 ? 0xFFFFFFFF : colour });
        }
    }

    private void Die(string cause)
    {
        Cause = cause;
        var s = Ship;
        s.Alive = false;
        s.Cloaked = false;
        Lives--;
        // Whoever it was carrying falls.
        foreach (var c in s.Carrying)
        {
            c.State = ColonistState.Falling;
            c.FellFrom = c.Y;
            c.Vy = 0;
        }
        s.Carrying.Clear();
        Shatter(s.X, s.Y, 40, 0xFFFFE070);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = s.X, Y = s.Y, Life = 1, Size = 2.2, Colour = 0xFFFFE070 });
        Bolts.Clear();
        Lasers.Clear();
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
    }

    /// <summary>The wave's beaten once it has sent all its Snatchers and nothing but Hunters is left.</summary>
    private void Progress()
    {
        if (Calm || Remaining > 0) return;
        var s = Ship;
        foreach (var c in s.Carrying)
        {
            c.State = ColonistState.Walking;
            c.Y = Field.Ground;
            AddScore(SetDownPoints);
        }
        s.Carrying.Clear();
        s.Cloaked = false;
        Enemies.Clear();
        Bolts.Clear();
        int each = 100 * Math.Min(5, Wave);
        Bonus = DeepSpace ? 1000 * Math.Min(5, (Wave + 5) / 10 + 1) : ColonistsLeft * each;
        AddScore(Bonus);
        SetPhase(Phase.Done);
        Music(false);
        Say($"WAVE {Wave} COMPLETE", DeepSpace ? $"SECTOR CLEARED  {Bonus}" : $"{ColonistsLeft} COLONISTS x {each}  =  {Bonus}", DoneSeconds);
        Play(Sfx.WaveDone);
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextBonus)
        {
            _nextBonus += BonusEvery;
            Lives++;
            SmartBombs = Math.Min(9, SmartBombs + 1);
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a wave, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
        Ship.Safe = 0;
    }

    /// <summary>Nothing left in the wave and nothing still to come.</summary>
    internal void Empty()
    {
        Clear();
        ToCome = 0;
    }

    internal Enemy PutEnemy(EnemyKind kind, double x, double y)
    {
        var e = Appear(kind, Field.Wrap(x), y);
        e.FireIn = 99;
        return e;
    }

    internal Colonist PutColonist(double x)
    {
        var c = new Colonist { X = Field.Wrap(x) };
        Colonists.Add(c);
        return c;
    }

    internal void PutBolt(BoltKind kind, double x, double y, double vx = 0, double vy = 0) => Bolts.Add(new Bolt { Kind = kind, X = Field.Wrap(x), Y = y, Vx = vx, Vy = vy, Life = 5 });

    internal void SetShip(double x, double y, int facing = 1)
    {
        var s = Ship;
        s.X = Field.Wrap(x);
        s.Y = y;
        s.Vx = 0;
        s.Facing = facing;
        s.Offset = facing > 0 ? 300 : Field.Width - 300;
    }

    internal void Begin(int wave) => BeginWave(wave);
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
    internal void SetBombs(int n) => SmartBombs = n;
    internal void SetToCome(int n) => ToCome = n;
}
