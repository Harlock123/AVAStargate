using AVAStargate.Engine;

namespace AVAStargate.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avastargate-tests-" + Guid.NewGuid().ToString("N"));

    private const double Mid = Field.World / 2;

    /// <summary>A wave in play with nothing in it, nothing coming and nothing firing unless a test makes it, and the ship in the middle of the planet.</summary>
    private static GameEngine Game(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        g.Empty();
        g.SetShip(Mid, 400);
        return g;
    }

    /// <summary>A real game in play, with plenty of ships.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    /// <summary>An enemy that stays at its height.</summary>
    private static Enemy Still(GameEngine g, EnemyKind kind, double x, double y)
    {
        var e = g.PutEnemy(kind, x, y);
        e.Vy = 0;
        return e;
    }

    private static readonly Controls FireButton = new(Fire: true);

    // ───────────────────────────── the ship ─────────────────────────────

    [Fact]
    public void The_ship_flies_either_way_coasts_and_slides_across_to_see_ahead()
    {
        var g = Game();
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(1, g.Ship.Facing);
        Assert.True(g.Ship.Vx > 500);
        double v = g.Ship.Vx;
        g.Idle(30);
        Assert.InRange(g.Ship.Vx, 1, v * 0.6);
        g.Run(60, new Controls(Dx: -1));
        Assert.Equal(-1, g.Ship.Facing);
        Assert.True(g.Ship.Vx < 0);
        Assert.True(g.Ship.Offset > 700, "facing left, it sits on the right of the screen");
    }

    [Fact]
    public void The_ship_climbs_and_dives_between_the_scanner_and_the_ground()
    {
        var g = Game();
        g.Run(120, new Controls(Dy: -1));
        Assert.Equal(Field.ShipTop, g.Ship.Y);
        g.Run(200, new Controls(Dy: 1));
        Assert.Equal(Field.ShipBottom, g.Ship.Y);
    }

    [Fact]
    public void The_planet_wraps_round()
    {
        var g = Game();
        g.SetShip(Field.World - 20, 400);
        g.Run(30, new Controls(Dx: 1));
        Assert.InRange(g.Ship.X, 0, 600);
    }

    [Fact]
    public void A_laser_stops_at_the_first_thing_level_with_it_and_ahead()
    {
        var g = Game();
        var near = Still(g, EnemyKind.Miner, Mid + 300, 402);
        var far = Still(g, EnemyKind.Miner, Mid + 520, 402);
        var behind = Still(g, EnemyKind.Miner, Mid - 300, 402);
        var above = Still(g, EnemyKind.Miner, Mid + 200, 360);
        g.Tick(FireButton);
        g.Idle(12);
        Assert.True(near.Dead);
        Assert.False(far.Dead);
        Assert.False(behind.Dead);
        Assert.False(above.Dead);
        Assert.Equal(GameEngine.Points(EnemyKind.Miner), g.Score);
    }

    [Fact]
    public void Tapping_fires_quicker_than_holding_but_four_lasers_at_most()
    {
        var g = Game();
        int most = 0;
        for (int t = 0; t < 120; t++)
        {
            g.Tick(new Controls(Fire: t % 6 < 3));
            most = Math.Max(most, g.Lasers.Count);
        }
        Assert.Equal(4, most);
        var h = Game();
        int held = 0;
        for (int t = 0; t < 120; t++)
        {
            h.Tick(FireButton);
            held = Math.Max(held, h.Lasers.Count);
        }
        Assert.InRange(held, 1, 2);
    }

    [Fact]
    public void A_smart_bomb_clears_the_screen_but_nothing_off_it()
    {
        var g = Game();
        var a = Still(g, EnemyKind.Miner, Mid + 400, 200);
        var hive = Still(g, EnemyKind.Hive, Mid + 600, 600);
        var far = Still(g, EnemyKind.Miner, Mid + 3000, 300);
        g.PutBolt(BoltKind.Mine, Mid + 200, 300);
        int bombs = g.SmartBombs;
        g.Tick(new Controls(Bomb: true));
        Assert.True(a.Dead && hive.Dead);
        Assert.False(far.Dead);
        Assert.Empty(g.Bolts);
        Assert.DoesNotContain(g.Enemies, e => e.Kind == EnemyKind.Stinger);
        Assert.Equal(bombs - 1, g.SmartBombs);
        Assert.Equal(GameEngine.Points(EnemyKind.Miner) + GameEngine.Points(EnemyKind.Hive), g.Score);
        g.SetBombs(0);
        var b = Still(g, EnemyKind.Miner, Mid + 400, 200);
        g.Idle(2);
        g.Tick(new Controls(Bomb: true));
        Assert.False(b.Dead);
    }

    [Fact]
    public void The_cloak_shields_the_ship_and_destroys_what_it_touches_while_it_lasts()
    {
        var g = Game();
        var e = Still(g, EnemyKind.Miner, Mid + 10, 400);
        g.PutBolt(BoltKind.Shot, Mid, 400);
        g.Run(20, new Controls(Cloak: true));
        Assert.True(e.Dead);
        Assert.True(g.Ship.Alive);
        Assert.Empty(g.Bolts);
        Assert.InRange(g.Ship.Energy, 80, 95);
        g.Ship.Energy = 5;
        g.Run(30, new Controls(Cloak: true));
        Assert.Equal(0, g.Ship.Energy);
        Assert.False(g.Ship.Cloaked);
    }

    [Fact]
    public void Hyperspace_throws_the_ship_somewhere_else_and_now_and_then_loses_it()
    {
        int moved = 0, lost = 0;
        for (int seed = 1; seed <= 40; seed++)
        {
            var g = Game(seed: seed);
            g.Tick(new Controls(Hyper: true));
            if (!g.Ship.Alive)
            {
                Assert.Equal("LOST IN HYPERSPACE", g.Cause);
                lost++;
            }
            else if (Math.Abs(Field.Dx(Mid, g.Ship.X)) > 50) moved++;
        }
        Assert.True(moved > 25, $"moved {moved}");
        Assert.InRange(lost, 1, 14);
    }

    // ───────────────────────────── the gate ─────────────────────────────

    [Fact]
    public void The_gate_throws_the_ship_to_the_colonist_in_most_danger()
    {
        var g = Game();
        g.SetShip(g.Gate.X - 20, g.Gate.Y);
        var low = g.PutColonist(6000);
        var high = g.PutColonist(7000);
        foreach (var (col, x, y) in new[] { (low, 6000.0, 600.0), (high, 7000.0, 250.0) })
        {
            var s = g.PutEnemy(EnemyKind.Snatcher, x, y);
            s.Holding = col;
            col.State = ColonistState.Taken;
        }
        g.Run(6, new Controls(Dx: 1));
        Assert.InRange(Math.Abs(Field.Dx(g.Ship.X, 7000)), 0, 400);
        Assert.InRange(g.Ship.Y, 200, 260);
    }

    [Fact]
    public void With_no_one_in_trouble_the_gate_goes_round_the_far_side()
    {
        var g = Game();
        g.SetShip(g.Gate.X - 20, g.Gate.Y);
        g.Run(6, new Controls(Dx: 1));
        Assert.InRange(Math.Abs(Field.Dx(g.Ship.X, g.Gate.X + Field.World / 2)), 0, 300);
    }

    [Fact]
    public void Four_colonists_through_the_gate_warp_three_waves_ahead()
    {
        var g = Game();
        g.SetShip(g.Gate.X - 20, g.Gate.Y);
        foreach (var c in g.Colonists.Take(4))
        {
            c.State = ColonistState.Carried;
            g.Ship.Carrying.Add(c);
        }
        g.Run(6, new Controls(Dx: 1));
        Assert.Equal(4, g.Wave);
        Assert.Empty(g.Ship.Carrying);
        Assert.Equal(4 * GameEngine.WarpPoints, g.Score);
        Assert.All(g.Colonists, c => Assert.Equal(ColonistState.Walking, c.State));
    }

    // ───────────────────────────── the colonists ─────────────────────────────

    [Fact]
    public void A_Snatcher_takes_a_colonist_to_the_top_and_becomes_a_Changeling()
    {
        var g = Game(2);
        g.SetShip(1000, 400);
        var col = g.PutColonist(5000);
        var s = g.PutEnemy(EnemyKind.Snatcher, 5200, 300);
        s.Target = col;
        col.Wanted = s;
        for (int t = 0; t < 600 && s.Holding == null; t++) g.Tick(default);
        Assert.Same(col, s.Holding);
        Assert.Equal(ColonistState.Taken, col.State);
        for (int t = 0; t < 60 * 30 && s.Kind == EnemyKind.Snatcher; t++) g.Tick(default);
        Assert.Equal(EnemyKind.Changeling, s.Kind);
        Assert.Equal(ColonistState.Dead, col.State);
        Assert.Null(s.Holding);
    }

    /// <summary>A Snatcher carrying a colonist level with the ship, ahead of it.</summary>
    private static (Enemy S, Colonist C) Carried(GameEngine g, double y)
    {
        var col = g.PutColonist(Mid + 300);
        var s = Still(g, EnemyKind.Snatcher, Mid + 300, y);
        s.Holding = col;
        col.State = ColonistState.Taken;
        col.Y = y + 26;
        g.SetShip(Mid, y - 2);
        return (s, col);
    }

    [Fact]
    public void Shoot_a_Snatcher_and_its_colonist_falls_too_far_to_live()
    {
        var g = Game();
        var (s, col) = Carried(g, 300);
        g.Tick(FireButton);
        g.Idle(10);
        Assert.True(s.Dead);
        Assert.Equal(ColonistState.Falling, col.State);
        g.Idle(300);
        Assert.Equal(ColonistState.Dead, col.State);
    }

    [Fact]
    public void A_short_fall_and_the_colonist_lands_safely()
    {
        var g = Game();
        var (_, col) = Carried(g, 560);
        g.Tick(FireButton);
        g.Idle(200);
        Assert.Equal(ColonistState.Walking, col.State);
        Assert.Equal(GameEngine.Points(EnemyKind.Snatcher) + GameEngine.LandPoints, g.Score);
    }

    [Fact]
    public void Catch_a_falling_colonist_and_set_them_down()
    {
        var g = Game();
        var col = g.PutColonist(Mid);
        col.State = ColonistState.Falling;
        col.FellFrom = col.Y = 410;
        g.Tick(default);
        Assert.Equal(ColonistState.Carried, col.State);
        Assert.Equal(GameEngine.CatchPoints, g.Score);
        g.Run(200, new Controls(Dy: 1));
        Assert.Equal(ColonistState.Walking, col.State);
        Assert.Empty(g.Ship.Carrying);
        Assert.Equal(GameEngine.CatchPoints + GameEngine.SetDownPoints, g.Score);
    }

    [Fact]
    public void Losing_the_ship_drops_whoever_it_carries()
    {
        var g = Game();
        var col = g.PutColonist(Mid);
        col.State = ColonistState.Falling;
        col.FellFrom = col.Y = 410;
        g.Tick(default);
        g.PutBolt(BoltKind.Shot, Mid, 400);
        g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal(ColonistState.Falling, col.State);
    }

    [Fact]
    public void Lose_the_last_colonist_and_the_planet_is_lost()
    {
        var g = Game(2);
        g.SetShip(1000, 400);
        foreach (var c in g.Colonists.Skip(1)) c.State = ColonistState.Dead;
        var last = g.Colonists[0];
        var s = g.PutEnemy(EnemyKind.Snatcher, 5000, Field.Top + 20);
        s.Holding = last;
        last.State = ColonistState.Taken;
        var other = g.PutEnemy(EnemyKind.Snatcher, 6000, 300);
        g.Idle(60);
        Assert.Equal(ColonistState.Dead, last.State);
        Assert.True(g.PlanetLost);
        Assert.Equal(EnemyKind.Changeling, other.Kind);
        // Any more that come are Changelings.
        g.Calm = false;
        g.SetToCome(3);
        g.Idle(2);
        Assert.DoesNotContain(g.Enemies, e => e.Kind == EnemyKind.Snatcher);
        Assert.True(g.Enemies.Count(e => e.Kind == EnemyKind.Changeling) >= 5);
        // Five waves on, it's restored.
        g.Begin(6);
        Assert.False(g.PlanetLost);
        Assert.Equal(GameEngine.Colonies, g.ColonistsLeft);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void A_Hive_bursts_into_Stingers()
    {
        var g = Game();
        Still(g, EnemyKind.Hive, Mid + 300, 402);
        g.Tick(FireButton);
        g.Idle(10);
        Assert.Equal(5, g.Enemies.Count(e => e.Kind == EnemyKind.Stinger));
        Assert.Equal(GameEngine.Points(EnemyKind.Hive), g.Score);
    }

    [Fact]
    public void A_Miner_sows_mines_and_a_mine_destroys_the_ship()
    {
        var g = Game();
        g.Calm = false;
        Still(g, EnemyKind.Miner, Mid + 2500, 300).FireIn = 0.5;
        g.Idle(200);
        Assert.True(g.Bolts.Count(b => b.Kind == BoltKind.Mine) >= 2);
        g.PutBolt(BoltKind.Mine, Mid, 400);
        g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("MINED", g.Cause);
    }

    [Fact]
    public void A_Whirler_takes_three_hits_and_hatches_Darts()
    {
        var g = Game(5);
        var w = Still(g, EnemyKind.Whirler, Mid + 300, 402);
        for (int i = 0; i < 2; i++)
        {
            g.Tick(FireButton);
            g.Idle(15);
            Assert.False(w.Dead);
        }
        g.Tick(FireButton);
        g.Idle(15);
        Assert.True(w.Dead);
        g.Calm = false;
        Still(g, EnemyKind.Whirler, Mid + 1000, 200);
        g.Idle(300);
        Assert.Contains(g.Enemies, e => e.Kind == EnemyKind.Dart);
    }

    [Fact]
    public void A_Hunter_comes_when_the_wave_drags_on()
    {
        var g = Game();
        g.Calm = false;
        Still(g, EnemyKind.Miner, Mid + 3000, 200);
        int t = 0;
        for (; t < 60 * 45 && !g.Enemies.Any(e => e.Kind == EnemyKind.Hunter); t++) g.Tick(default);
        Assert.Contains(g.Enemies, e => e.Kind == EnemyKind.Hunter);
        Assert.True(t > 60 * 25, $"after {t / 60}s");
    }

    [Fact]
    public void Only_what_is_on_the_screen_fires()
    {
        var g = Game();
        g.Calm = false;
        var far = g.PutEnemy(EnemyKind.Changeling, Mid + 3500, 200);
        far.FireIn = 0.2;
        g.Idle(30);
        Assert.Empty(g.Bolts);
        var near = Still(g, EnemyKind.Snatcher, Mid + 500, 200);
        near.FireIn = 0.2;
        g.Idle(30);
        Assert.NotEmpty(g.Bolts);
    }

    // ───────────────────────────── losing ships ─────────────────────────────

    [Fact]
    public void Shot_down_a_moment_of_safety_and_the_last_ship_ends_the_game()
    {
        var g = Game();
        g.SetLives(2);
        var e = Still(g, EnemyKind.Miner, Mid + 5, 400);
        g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("RAMMED BY A MINER", g.Cause);
        Assert.Equal(1, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.True(g.Ship.Alive && g.Ship.Safe > 0);
        g.PutBolt(BoltKind.Shot, g.Ship.X, g.Ship.Y);
        g.Tick(default);
        Assert.True(g.Ship.Alive, "safe for a moment");
        g.Idle((int)(GameEngine.SafeSeconds * 60));
        g.PutBolt(BoltKind.Shot, g.Ship.X, g.Ship.Y);
        g.Tick(default);
        Assert.Equal("SHOT DOWN", g.Cause);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── waves ─────────────────────────────

    [Fact]
    public void A_wave_ends_with_a_bonus_for_each_colonist_left()
    {
        var g = Game(2);
        foreach (var c in g.Colonists.Take(3)) c.State = ColonistState.Dead;
        g.Calm = false;
        g.Tick(default);
        Assert.Equal(Phase.Done, g.Phase);
        Assert.Equal(7 * 200, g.Bonus);
        Assert.Equal(7 * 200, g.Score);
        g.Idle((int)(GameEngine.DoneSeconds * 60) + 5);
        Assert.Equal(3, g.Wave);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.True(g.Remaining > 0);
    }

    [Theory]
    [InlineData(4, Sector.Planet)]
    [InlineData(5, Sector.Swarm)]
    [InlineData(10, Sector.Showdown)]
    [InlineData(15, Sector.Swarm)]
    [InlineData(20, Sector.Showdown)]
    public void Every_fifth_wave_is_out_in_deep_space(int wave, Sector sector)
    {
        var g = Real(wave);
        Assert.Equal(sector, g.Sector);
        Assert.Equal(sector != Sector.Planet, g.DeepSpace);
        if (sector == Sector.Planet) Assert.True(g.ToCome > 0);
        else Assert.Equal(0, g.ToCome);
        if (sector == Sector.Swarm) Assert.All(g.Enemies, e => Assert.Equal(EnemyKind.Whirler, e.Kind));
        if (sector == Sector.Showdown) Assert.True(g.Enemies.Count(e => e.Kind == EnemyKind.Firebrand) >= 8);
    }

    [Fact]
    public void Later_waves_send_more()
    {
        Assert.True(Real(6).ToCome > Real(1).ToCome);
        Assert.True(Real(6).Enemies.Count > Real(1).Enemies.Count);
    }

    [Fact]
    public void An_extra_ship_and_smart_bomb_every_10000()
    {
        var g = Game();
        g.SetScore(9900);
        int lives = g.Lives, bombs = g.SmartBombs;
        Still(g, EnemyKind.Snatcher, Mid + 300, 402);
        g.Tick(FireButton);
        g.Idle(10);
        Assert.Equal(lives + 1, g.Lives);
        Assert.Equal(bombs + 1, g.SmartBombs);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_pilot_beats_the_first_waves_and_rescues_colonists()
    {
        var g = Real();
        g.SetLives(3);
        bool caught = false;
        for (int i = 0; i < 60 * 150 && g.Wave < 4 && g.State == GameState.Playing; i++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Ship.Carrying.Count > 0) caught = true;
        }
        Assert.True(g.Wave >= 4, $"reached wave {g.Wave}");
        Assert.True(caught);
        Assert.True(g.ColonistsLeft >= 8);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Ship.X:0.00} {g.Ship.Y:0.00} {g.Remaining} {g.ColonistsLeft}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real(2);
        g.Run(30, new Controls(Dx: 1));
        double x = g.Ship.X, y = g.Ship.Y;
        g.Pause();
        g.Idle(120);
        Assert.Equal((x, y), (g.Ship.X, g.Ship.Y));
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
