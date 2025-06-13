using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces;
using UdonSharp;
using JLChnToZ.VRC.Foundation;

namespace JLChnToZ.VRC {
    [AddComponentMenu("JLChnToZ/Lazy Respawn")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [BindEvent(typeof(Button), nameof(Button.onClick), nameof(Interact))]
    public class LazyRespawn : UdonSharpBehaviour {
        [SerializeField] TriggerMode triggerMode;
        [SerializeField] float respawnDelay;
        [SerializeField] bool debounce;
        [SerializeField] internal GameObject[] targetObjects;
        [SerializeField, HideInInspector] internal Transform[] targetTransforms;
        [SerializeField, HideInInspector] internal VRCObjectSync[] targetObjectSyncs;
        [SerializeField] internal VRCObjectPool[] targetObjectPools;
        [SerializeField, HideInInspector] internal VRCPickup[] targetPickups;
        [SerializeField, HideInInspector] internal bool[] targetPickupDropOnRespawn;
        Vector3[] targetPositions;
        Quaternion[] targetRotations;
        float actualRepawnTime;
        DataDictionary enteredPlayers;

        void Start() {
            targetPositions = new Vector3[targetTransforms.Length];
            targetRotations = new Quaternion[targetTransforms.Length];
            for (int i = 0; i < targetTransforms.Length; i++) {
                targetPositions[i] = targetTransforms[i].position;
                targetRotations[i] = targetTransforms[i].rotation;
            }
            if (triggerMode != TriggerMode.InteractLocal &&
                triggerMode != TriggerMode.InteractGlobal)
                DisableInteractive = true;
            if (triggerMode == TriggerMode.FirstPlayerEnter ||
                triggerMode == TriggerMode.LastPlayerExit)
                enteredPlayers = new DataDictionary();
        }

        public override void Interact() {
            switch (triggerMode) {
                case TriggerMode.InteractLocal:
                case TriggerMode.InteractGlobal:
                    DoRespawn();
                    break;
            }
        }

        public override void OnPlayerTriggerEnter(VRCPlayerApi player) {
            switch (triggerMode) {
                case TriggerMode.LocalPlayerEnter:
                    if (player.isLocal) DoRespawn();
                    break;
                case TriggerMode.AnyPlayerEnter:
                    DoRespawn();
                    break;
                case TriggerMode.FirstPlayerEnter:
                    if (enteredPlayers.Count == 0) DoRespawn();
                    enteredPlayers[player.playerId] = true;
                    break;
                case TriggerMode.LastPlayerExit:
                    enteredPlayers[player.playerId] = true;
                    break;
            }
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player) {
            switch (triggerMode) {
                case TriggerMode.LocalPlayerExit:
                    if (player.isLocal) DoRespawn();
                    break;
                case TriggerMode.AnyPlayerExit:
                    DoRespawn();
                    break;
                case TriggerMode.FirstPlayerEnter:
                    enteredPlayers.Remove(player.playerId);
                    break;
                case TriggerMode.LastPlayerExit:
                    if (enteredPlayers.Remove(player.playerId) &&
                        enteredPlayers.Count == 0) DoRespawn();
                    break;
            }
        }

        public void DoRespawn() {
            if (respawnDelay <= 0) {
                DoRespawn_Broadcast();
                return;
            }
            if (!debounce && !float.IsInfinity(actualRepawnTime))
                return;
            actualRepawnTime = Time.time + respawnDelay;
            SendCustomEventDelayedSeconds(nameof(_DoRespawn_Check), respawnDelay);
        }

        void CancelRespawn() {
            actualRepawnTime = float.PositiveInfinity;
        }

        public void _DoRespawn_Check() {
            if (float.IsInfinity(actualRepawnTime)) return;
            var diff = actualRepawnTime - Time.time;
            if (diff > 0) {
                SendCustomEventDelayedSeconds(nameof(_DoRespawn_Check), diff);
                return;
            }
            DoRespawn_Broadcast();
        }

        void DoRespawn_Broadcast() {
            if (triggerMode == TriggerMode.InteractGlobal)
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(DoRespawn_Exec));
            else
                DoRespawn_Exec();
        }

        public void DoRespawn_Exec() {
            actualRepawnTime = float.PositiveInfinity;
            bool delayed = false;
            for (int i = 0, count = targetObjects.Length; i < count; i++) {
                var obj = targetObjects[i];
                if (!obj) continue;
                var sync = targetObjectSyncs[i];
                var pool = targetObjectPools[i];
                var pickup = targetPickups[i];
                if (!Utilities.IsValid(sync) || Networking.IsOwner(obj)) {
                    if (Utilities.IsValid(pickup) && pickup.IsHeld) {
                        if (!targetPickupDropOnRespawn[i]) continue;
                        pickup.Drop();
                    }
                    if (Utilities.IsValid(sync)) sync.Respawn();
                    else targetTransforms[i].SetPositionAndRotation(targetPositions[i], targetRotations[i]);
                    if (Utilities.IsValid(pool)) {
                        var poolOwner = Networking.GetOwner(pool.gameObject);
                        if (poolOwner.isLocal) pool.Return(targetObjects[i]);
                        else Networking.SetOwner(poolOwner, obj);
                    }
                } else if (!delayed && Utilities.IsValid(pool) && !Networking.IsOwner(pool.gameObject) &&
                    (!Utilities.IsValid(pickup) || !pickup.IsHeld || targetPickupDropOnRespawn[i]))
                    delayed = true;
            }
            if (delayed) SendCustomEventDelayedSeconds(nameof(DoRespawn_Exec), 0.5F);
        }
    }
}