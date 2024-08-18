using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace JLChnToZ.VRC {
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    [RequireComponent(typeof(Collider))]
    public class PlayerEnterDetector : UdonSharpBehaviour {
        [SerializeField] LazySwitch lazySwitch;
        [Tooltip("The state to set when player enter. Set to -1 to disable.")]
        [SerializeField] int playerEnterState = -1;
        [Tooltip("The state to set when player exit. Set to -1 to disable.")]
        [SerializeField] int playerExitState = -1;
        [Tooltip("Detect all players in the world.\nWhen enabled, state changes only when the first player enter or the last player exit.")]
        [SerializeField] bool detectAllPlayers = false;
        DataDictionary enteredPlayers;

        void OnEnable() {
            if (lazySwitch == null) {
                Debug.LogError("LazySwitch is not assigned.");
                enabled = false;
                return;
            }
            if (lazySwitch.gameObject == gameObject)
                lazySwitch.DisableInteractive = true; // You don't want the detection collider interactable.
            var collider = GetComponent<Collider>();
            collider.isTrigger = true; // Make sure the collider is a trigger.
            if (detectAllPlayers) {
                if (enteredPlayers == null)
                    enteredPlayers = new DataDictionary();
                else
                    enteredPlayers.Clear();
            }
        }

        public override void OnPlayerTriggerEnter(VRCPlayerApi player) {
            if (!detectAllPlayers) {
                if (player.isLocal && playerEnterState >= 0)
                    lazySwitch.State = playerEnterState;
                return;
            }
            if (enteredPlayers.Count == 0 &&
                playerEnterState >= 0)
                lazySwitch.State = playerEnterState;
            enteredPlayers[player.playerId] = true;
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player) {
            if (!detectAllPlayers) {
                if (player.isLocal && playerExitState >= 0)
                    lazySwitch.State = playerExitState;
                return;
            }
            if (enteredPlayers.Remove(player.playerId) &&
                enteredPlayers.Count == 0 &&
                playerExitState >= 0)
                lazySwitch.State = playerExitState;
        }
    }
}