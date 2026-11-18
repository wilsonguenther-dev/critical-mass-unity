using System;
using System.Collections;
using CriticalMass.Runtime.View;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// b15 world look, item 5 (WORLD-AUDIT "post-hatch spikes", 3.5 MB and 24 MB frames): with the Kaiju stages warmed the
    /// way FirstRunWarm's wide render now warms them (ActorViews.PrewarmKaiju), the frames from the hatch on allocate no
    /// more than WorldLookBudgets.HatchFrameAllocBytes each on the main thread (managed: GC.GetAllocatedBytesForCurrentThread
    /// around each frame; the sim's own hatch tick is measured and printed apart, it is not the view's). Prints every
    /// frame's bytes. Frame TIME is measured in a build with the Editor/WorldLook frame probe (CI runs without a GPU).
    /// </summary>
    public sealed class HatchFramePlayTests
    {
        SanityWorld _w;

        [TearDown]
        public void TearDown()
        {
            SanityWorld.Reset();
            _w = null;
        }

        [UnityTest]
        public IEnumerator TheHatchFramesStayInsideTheAllocationBudget()
        {
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            World w = _w.W;
            int warmed = _w.Views.PrewarmKaiju(true);
            yield return null;
            _w.Views.PrewarmKaiju(false);
            Debug.Log($"[b15 hatch] prewarm built or found {warmed} Kaiju materials");
            Assert.IsFalse(_w.Views.KaijuPrewarming);
            for (int f = 0; f < 20; f++) { _w.Tick(); yield return null; }

            w.ForceHatch = true;
            long tickBytes = 0, worst = 0;
            int worstFrame = -1, hatchAt = -1;
            var line = new System.Text.StringBuilder();
            for (int f = 0; f < 90; f++)
            {
                long t0 = GC.GetAllocatedBytesForCurrentThread();
                _w.Tick();
                long t1 = GC.GetAllocatedBytesForCurrentThread();
                if (hatchAt < 0 && w.Kaiju.State != KaijuState.Egg) { hatchAt = f; tickBytes = t1 - t0; }
                yield return null;
                long bytes = GC.GetAllocatedBytesForCurrentThread() - t1;
                if (hatchAt >= 0)
                {
                    line.Append(bytes).Append(' ');
                    if (bytes > worst) { worst = bytes; worstFrame = f - hatchAt; }
                }
            }
            Assert.GreaterOrEqual(hatchAt, 0, "the forced hatch did not happen");
            Debug.Log($"[b15 hatch] sim hatch tick {tickBytes} B; view frames from the hatch (B): {line}");
            Debug.Log($"[b15 hatch] worst view frame {worst} B at hatch + {worstFrame} (limit {WorldLookBudgets.HatchFrameAllocBytes} B)");
            Assert.LessOrEqual(worst, WorldLookBudgets.HatchFrameAllocBytes, $"frame hatch + {worstFrame} allocated {worst} B");
        }
    }
}
