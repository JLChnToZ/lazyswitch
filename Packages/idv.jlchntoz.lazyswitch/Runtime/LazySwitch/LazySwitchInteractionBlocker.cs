using UdonSharp;
using UnityEngine;
using JLChnToZ.VRC.Foundation;

namespace JLChnToZ.VRC {
    /// <summary>
    /// A base class for components controlling the state of a <see cref="LazySwitch"/>, which disables the interaction.
    /// </summary>
    public abstract class LazySwitchInteractionBlocker : UdonSharpBehaviour {
        [SerializeField, Resolve(".")]
#if COMPILER_UDONSHARP
        public
#else
        internal protected
#endif
        LazySwitch lazySwitch;

        protected virtual void OnEnable() {
            if (lazySwitch == null) {
                Debug.LogError("LazySwitch is not assigned.");
                enabled = false;
                return;
            }
            if (lazySwitch.gameObject == gameObject)
                lazySwitch.DisableInteractive = true;
        }
    }
}
