using UdonSharp;
using UnityEngine;
using JLChnToZ.VRC.Foundation.I18N;
using VRC.SDKBase;

namespace JLChnToZ.VRC {
    /// <summary>
    /// A component that detects the enabling and disabling of the GameObject, and changes the state of a LazySwitch accordingly.
    /// </summary>
    [AddComponentMenu("JLChnToZ/Lazy Switch Enable Detector")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class EnableDetector : LazySwitchInteractionBlocker {
        [SerializeField, LocalizedLabel] bool detectOnEnable = true;
        [SerializeField, LocalizedLabel] int enableState = -1;
        [SerializeField, LocalizedLabel] bool detectOnDisable = true;
        [SerializeField, LocalizedLabel] int disableState = -1;

        protected override void OnEnable() {
            base.OnEnable();
            if (!detectOnEnable || !Utilities.IsValid(lazySwitch)) return;
            if (enableState >= 0)
                lazySwitch.State = enableState;
            else
                lazySwitch._SwitchState();
        }

        protected void OnDisable() {
            if (!detectOnDisable || !Utilities.IsValid(lazySwitch)) return;
            if (disableState >= 0)
                lazySwitch.State = disableState;
            else
                lazySwitch._SwitchState();
        }
    }
}