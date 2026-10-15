using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAStargate.Engine;
using AVAStargate.Views;
using Controls = AVAStargate.Engine.Controls;

namespace AVAStargate.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set GATE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avastargate-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("GATE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAStargate-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false, Bomb = false, Cloak = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 184250, 14);
        g.HighScores.Insert("AVA", 121800, 11);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Over_the_planet()
    {
        var (w, c, g) = Open(2);
        g.Go(3);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 6 && g.Lasers.Count > 0 && g.Ship.Alive && g.Enemies.Count(e => g.OnScreen(e.X)) >= 3 && g.Ship.Safe <= 0, 120));
        Shot(w, c, "planet");
    }

    [AvaloniaFact]
    public void A_colonist_carried_off()
    {
        var (w, c, g) = Open(3);
        g.Go(2);
        g.SetLives(9);
        g.Calm = true;
        g.Empty();
        var col = g.Colonists[0];
        g.SetShip(col.X - 380, 300);
        var s = g.PutEnemy(EnemyKind.Snatcher, col.X, 560);
        s.Target = col;
        col.Wanted = s;
        for (int t = 0; t < 60 * 20 && s.Holding == null; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Assert.NotNull(s.Holding);
        g.Run(220, default);
        g.PutEnemy(EnemyKind.Miner, s.X + 330, 250);
        g.PutEnemy(EnemyKind.Changeling, s.X - 160, 200);
        g.PutBolt(BoltKind.Mine, s.X + 250, 300);
        g.PutBolt(BoltKind.Mine, s.X + 200, 340);
        c.Advance(0.3);
        Shot(w, c, "snatched");
    }

    [AvaloniaFact]
    public void A_rescue()
    {
        var (w, c, g) = Open(4);
        g.Go(2);
        g.SetLives(9);
        g.Calm = true;
        g.Empty();
        var col = g.Colonists[0];
        // A Snatcher high up with its colonist; shoot it and catch them.
        var s = g.PutEnemy(EnemyKind.Snatcher, col.X, 280);
        col.State = ColonistState.Taken;
        s.Holding = col;
        g.SetShip(col.X - 300, 280);
        g.Run(6, new Controls(Fire: true));
        Assert.True(PlayUntil(g, c, g => g.Ship.Carrying.Count > 0, 10));
        g.Run(10, new Controls(Dx: 1));
        c.Advance(0.2);
        Shot(w, c, "rescue");
    }

    [AvaloniaFact]
    public void The_gate()
    {
        var (w, c, g) = Open(5);
        g.Go(1);
        g.SetLives(9);
        g.Calm = true;
        g.Empty();
        g.SetShip(g.Gate.X - 330, g.Gate.Y + 4);
        g.PutEnemy(EnemyKind.Hive, g.Gate.X + 300, 220);
        g.PutEnemy(EnemyKind.Hunter, g.Gate.X - 120, 560);
        g.Run(14, new Controls(Dx: 1));
        g.Ship.Cloaked = false;
        g.Run(4, new Controls(Dx: 1, Cloak: true));
        c.Advance(0.3);
        Shot(w, c, "gate");
    }

    [AvaloniaFact]
    public void Deep_space()
    {
        var (w, c, g) = Open(6);
        g.Go(5);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Enemies.Count(e => e.Kind == EnemyKind.Dart && g.OnScreen(e.X)) >= 3 && g.Enemies.Any(e => e.Kind == EnemyKind.Whirler && g.OnScreen(e.X)) && g.Ship.Alive, 60, holdFire: true));
        Shot(w, c, "swarm");
        g.Go(10);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Bolts.Count(b => b.Kind == BoltKind.Fireball && g.OnScreen(b.X)) >= 2 && g.Ship.Alive, 60, holdFire: true));
        Shot(w, c, "showdown");
    }

    [AvaloniaFact]
    public void Wave_complete()
    {
        var (w, c, g) = Open(7);
        g.Go(1);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Done && g.PhaseTime > 0.6 && g.Ship.Alive, 200));
        Shot(w, c, "wave-complete");
    }

    [AvaloniaFact]
    public void A_ship_lost()
    {
        var (w, c, g) = Open(8);
        g.Go(2);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 5 && g.Ship.Alive && g.Ship.Safe <= 0 && g.Enemies.Count(e => g.OnScreen(e.X)) >= 2));
        g.PutBolt(BoltKind.Shot, g.Ship.X, g.Ship.Y - 30, g.Ship.Vx, 300);
        g.Idle(20);
        Assert.Equal(Phase.Dying, g.Phase);
        c.Advance(0.3);
        Shot(w, c, "lost");
    }
}
