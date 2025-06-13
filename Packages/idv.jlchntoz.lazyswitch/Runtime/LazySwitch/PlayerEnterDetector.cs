using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using JLChnToZ.VRC.Foundation;
using JLChnToZ.VRC.Foundation.I18N;

namespace JLChnToZ.VRC {
    /// <summary>
    /// A component that detects player entering and exiting the collider, and changes the state of a LazySwitch accordingly.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    [RequireComponent(typeof(Collider))]
    public class PlayerEnterDetector : LazySwitchInteractionBlocker {
        [SerializeField, LocalizedLabel, ToggleAndNumberField] int playerEnterState = -1;
        [SerializeField, LocalizedLabel, ToggleAndNumberField] int playerExitState = -1;
        [SerializeField, LocalizedLabel] internal bool detectAllPlayers = false;
        [SerializeField, LocalizedLabel] internal bool anyOwnedObjects = false;
        [SerializeField, HideInInspector] internal GameObject[] childrenToCheck;
        DataDictionary enteredPlayers;

        protected override void OnEnable() {
            base.OnEnable();
            if (detectAllPlayers) {
                if (Utilities.IsValid(enteredPlayers))
                    enteredPlayers.Clear();
                else
                    enteredPlayers = new DataDictionary();
            }
        }

        public override void OnPlayerTriggerEnter(VRCPlayerApi player) {
            if (!detectAllPlayers) {
                if (player.isLocal && playerEnterState >= 0)
                    lazySwitch.State = playerEnterState;
                return;
            }
            if (enteredPlayers.Count == 0 &&
                playerEnterState >= 0 && (IsAnyOwned() || player.isLocal))
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

        bool IsAnyOwned() {
            if (!anyOwnedObjects ||
                !Utilities.IsValid(childrenToCheck) ||
                childrenToCheck.Length == 0)
                return true;
            foreach (var child in childrenToCheck)
                if (Utilities.IsValid(child) && Networking.IsOwner(child))
                    return true;
            return false;
        }
    }
}