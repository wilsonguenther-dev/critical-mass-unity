using UnityEngine;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>
    /// px lane F round 4 (lead's instruction): a ModelView's PlayableGraph is destroyed with its model. ModelView is a plain
    /// class; pooled models are disposed by ActorViews, but a model whose root dies with the scene (a match reload, Play
    /// stopped) never was, and Unity warned "PlayableGraph was not destroyed" (78 at Play stop in the pass 4 capture).
    /// </summary>
    public sealed class ModelGraphReleaser : MonoBehaviour
    {
        internal ModelView Owner;

        void OnDestroy()
        {
            ModelView m = Owner;
            Owner = null;
            m?.ReleaseGraph();   // the root is already going; only the graph needs freeing
        }
    }
}
