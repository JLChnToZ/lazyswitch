using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using UdonSharp;
using JLChnToZ.VRC.Foundation;
using JLChnToZ.VRC.Foundation.I18N;

namespace JLChnToZ.VRC {
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    [AddComponentMenu("JLChnToZ/Lazy Switch Toggle Group Helper")]
    public class ToggleGroupHelper : UdonSharpBehaviour {
        [LocalizedLabel(Key = "JLChnToZ.VRC.LazySwitch.masterSwitch"), SerializeField, BindUdonSharpEvent] LazySwitch masterSwitch;
        [LocalizedLabel, SerializeField, BindEvent(nameof(Toggle.onValueChanged), nameof(_OnToggleChecked))]
        Toggle[] toggles;

        void OnEnable() => SendCustomEventDelayedFrames(nameof(_OnLazySwitchStateChanged), 0);
        
        public void _OnToggleChecked() {
            if (!isActiveAndEnabled || !Utilities.IsValid(masterSwitch)) return;
            int lastState = masterSwitch.State;
            int currentState = -1;
            for (int i = 0, count = toggles.Length; i < count; i++) {
                var toggle = toggles[i];
                if (!Utilities.IsValid(toggle)) continue;
                if (toggle.isOn && lastState != i) {
                    currentState = i;
                    break;
                }
            }
            if (currentState >= 0)
                masterSwitch.State = currentState; // Will implicitly call _OnLazySwitchStateChanged via event
            else
                _OnLazySwitchStateChanged();
        }

        public void _OnLazySwitchStateChanged() {
            if (!isActiveAndEnabled) return;
            int state = masterSwitch.State;
            for (int i = 0, count = toggles.Length; i < count; i++) {
                var toggle = toggles[i];
                if (!Utilities.IsValid(toggle)) continue;
                bool shouldBeOn = i == state;
                if (toggle.isOn != shouldBeOn) toggle.SetIsOnWithoutNotify(shouldBeOn);
            }
        }
    }
}