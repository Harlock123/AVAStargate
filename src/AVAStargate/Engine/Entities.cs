namespace AVAStargate.Engine;

/// <summary>Fly left or right (Dx), climb or dive (Dy), fire, and the three emergency buttons.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Bomb = false, bool Cloak = false, bool Hyper = false);

/// <summary>
/// The planet: a long strip of world that wraps round, eight screens wide. The screen shows a 1024-wide
/// window on it below the scanner; colonists walk along the ground at the bottom.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double World = Width * 8;
    /// <summary>The top of the play area, under the scanner and score bar.</summary>
    public const double Top = 112;
    /// <summary>Where the colonists' feet are.</summary>
    public const double Ground = 744;
    /// <summary>How far up and down the ship can go.</summary>
    public const double ShipTop = Top + 14, ShipBottom = Ground - 22;

    public static double Wrap(double x) => ((x % World) + World) % World;

    /// <summary>The shortest way from one place to another, round the world if that's nearer.</summary>
    public static double Dx(double from, double to)
    {
        double d = Wrap(to - from);
        return d > World / 2 ? d - World : d;
    }

    /// <summary>The height of the mountains above the ground line, at a place on the planet: whole waves round the world, so it joins up.</summary>
    public static double Mountain(double x)
    {
        double a = x / World * Math.PI * 2;
        double h = 46 + 30 * Math.Sin(a * 7 + 0.4) + 22 * Math.Sin(a * 17 + 1.3) + 12 * Math.Sin(a * 41 + 2.1);
        return Math.Max(4, h);
    }
}

public sealed class Ship
{
    public double X = Field.World / 2, Y = 400, Vx;
    /// <summary>+1 facing right, -1 facing left.</summary>
    public int Facing = 1;
    /// <summary>Where on the screen it sits: it slides across when it turns round, to see ahead.</summary>
    public double Offset = 300;
    public bool Alive = true, Cloaked, Thrusting;
    /// <summary>The cloak's charge, 0 to 100.</summary>
    public double Energy = 100;
    public double FireCool, Safe, GateCool;
    public List<Colonist> Carrying { get; } = [];
}

/// <summary>The ship's laser: a streak racing out level from its nose.</summary>
public sealed class Laser
{
    public double X, Y, Head, Age;
    public int Dir;
    public bool Dead;
    /// <summary>The streak's back end, as a distance from where it started.</summary>
    public double Tail => Math.Max(0, Head - 640);
}

public enum BoltKind { Shot, Mine, Fireball }

/// <summary>Something an enemy has fired or left behind.</summary>
public sealed class Bolt
{
    public BoltKind Kind;
    public double X, Y, Vx, Vy, Life, Age;
    public bool Dead;
}

public enum ColonistState { Walking, Taken, Falling, Carried, Dead }

/// <summary>One of the planet's people, walking the ground until something comes for them.</summary>
public sealed class Colonist
{
    public double X, Y = Field.Ground, Vy, FellFrom;
    public int Walk = 1;
    public ColonistState State;
    /// <summary>A Snatcher on its way down for them.</summary>
    public Enemy? Wanted;
}

public enum EnemyKind
{
    /// <summary>Comes down for a colonist and carries them off; if it gets them to the top, it becomes a Changeling.</summary>
    Snatcher,
    /// <summary>A Snatcher that got its colonist away: fast and erratic, it comes straight for the ship.</summary>
    Changeling,
    /// <summary>Drifts across, sowing mines behind it.</summary>
    Miner,
    /// <summary>Slow and fat: shoot it and its Stingers pour out.</summary>
    Hive,
    /// <summary>A little dart from a Hive, swooping at the ship.</summary>
    Stinger,
    /// <summary>Sent when a wave drags on: as quick as the ship, and it fires and fires.</summary>
    Hunter,
    /// <summary>Zigzags, lobbing fireballs that curl after the ship.</summary>
    Firebrand,
    /// <summary>A spinning top from deep space that hatches Darts.</summary>
    Whirler,
    /// <summary>Hatched by a Whirler: a little fish that homes in.</summary>
    Dart,
}

public sealed class Enemy
{
    public EnemyKind Kind;
    public double X, Y, Vx, Vy, T, FireIn = 2, Spin, Hurt;
    public int Dir = 1, Hp = 1;
    /// <summary>The colonist it's after, and the one it has hold of.</summary>
    public Colonist? Target, Holding;
    /// <summary>A Snatcher on its way down to its colonist.</summary>
    public bool Diving;
    public bool Dead;

    public double Radius => Kind switch
    {
        EnemyKind.Stinger or EnemyKind.Dart => 8,
        EnemyKind.Hive or EnemyKind.Whirler => 18,
        EnemyKind.Hunter => 14,
        EnemyKind.Miner or EnemyKind.Firebrand => 14,
        _ => 15,
    };

    /// <summary>Half its width: a Hunter is long and flat.</summary>
    public double HalfWidth => Kind == EnemyKind.Hunter ? 26 : Radius;
}

/// <summary>The gate: fly into it to be thrown across the planet to a colonist in trouble.</summary>
public sealed class Gate
{
    public double X = Field.World * 0.25, Y = 420;
    public const double HalfWidth = 30, HalfHeight = 48;
}

/// <summary>A fleck of something blown up, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word, Beam }

public sealed class Effect
{
    public EffectKind Kind;
    /// <summary>X is a place on the planet (Word effects are on the screen).</summary>
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
