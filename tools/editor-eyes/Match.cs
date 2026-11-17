// Play mode only. Starts a fresh match through the game's own flow (GameFlow.QueueMatch + Boot reload) with a chosen class.
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class Match
{
    public static string Start(int cls, int seed)
    {
        if (!Application.isPlaying) return "not playing";
        MatchSetupChoice c = MatchSetupChoice.Default;
        c.Class = (ClassId)cls;
        GameFlow.QueueMatch(c, seed);
        SceneManager.LoadScene(GameBootstrap.BootScenePath);
        return "queued " + c.Class + " seed " + seed;
    }
    /// <summary>Hand actor 0 to the bot brain (true) or back to the human input (false).</summary>
    public static string Autopilot(bool on)
    {
        var d = Object.FindAnyObjectByType<SimDriverBehaviour>();
        if (d == null) return "no driver";
        d.HumanAutopilot = on;
        // The game hands an idle player (45 s, no input) to the bot brain; an agent sends no device input, so switch that off
        // while a capture needs actor 0 to stand still. Static, Play-mode only; resets on the next domain reload.
        HumanControlGuard.IdleAutopilotEnabled = on;
        return "autopilot " + on + " idleAutopilot " + HumanControlGuard.IdleAutopilotEnabled;
    }
}
