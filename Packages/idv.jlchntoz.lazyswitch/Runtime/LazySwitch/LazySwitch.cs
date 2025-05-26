using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Animations;
using VRC.SDKBase;
#if VRC_ENABLE_PLAYER_PERSISTENCE
using VRC.SDK3.Persistence;
#endif
using VRC.Udon.Common.Interfaces;
using UdonSharp;
using JLChnToZ.VRC.Foundation;

namespace JLChnToZ.VRC {
    [AddComponentMenu("JLChnToZ/Lazy Switch")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [BindEvent(typeof(Button), nameof(Button.onClick), nameof(Interact))]
    [BindEvent(typeof(Toggle), nameof(Toggle.onValueChanged), nameof(Interact))]
    public class LazySwitch : UdonSharpBehaviour {
        [SerializeField] internal int state;
        [Tooltip("Will this switch synchronized across the network.")]
        [SerializeField] internal bool isSynced;
        [Tooltip("Randomize the state when interacted.")]
        [SerializeField] internal bool isRandomized;
        [Tooltip("Link this switch to other switch, both switches will synchronize their state.")]
        [SerializeField] internal LazySwitch masterSwitch;
        [SerializeField, HideInInspector] internal LazySwitch[] slaveSwitches;
        [SerializeField] internal Object[] targetObjects;
        [SerializeField, HideInInspector] internal SwitchDrivenType[] targetObjectTypes;
        [SerializeField, HideInInspector] internal int[] targetObjectEnableMask;
        [SerializeField] internal int[] targetObjectGroupOffsets;
        [SerializeField] internal int stateCount;
        [SerializeField] internal FixupMode fixupMode;
#if VRC_ENABLE_PLAYER_PERSISTENCE
        [Tooltip("The optional key to save the state of this switch with player persistence.")]
        [SerializeField] internal string persistenceKey;
#endif
        [UdonSynced] byte syncedState;

        public int State {
            get => state;
            set {
                if (masterSwitch != null) {
                    masterSwitch.State = value;
                    return;
                }
                value %= stateCount;
                if (state == value) return;
                state = value;
                UpdateAndSync();
#if VRC_ENABLE_PLAYER_PERSISTENCE
                Save();
#endif
            }
        }

        void OnEnable() {
            if (masterSwitch != null) {
                _UpdateState();
                return;
            }
            if (isSynced && !Networking.IsOwner(gameObject)) {
                if (!Networking.IsObjectReady(gameObject)) return;
                state = syncedState;
#if VRC_ENABLE_PLAYER_PERSISTENCE
                Save();
            } else {
                Load(Networking.LocalPlayer);
#endif
            }
            UpdateState();
        }

#if VRC_ENABLE_PLAYER_PERSISTENCE
        public override void OnPlayerRestored(VRCPlayerApi player) {
            if (player.isLocal && masterSwitch == null && Load(player)) UpdateAndSync();
        }
#endif

        public override void Interact() {
            if (masterSwitch != null) {
                masterSwitch.Interact();
                return;
            }
            state = isRandomized ? Random.Range(0, stateCount) : (state + 1) % stateCount;
            UpdateAndSync();
#if VRC_ENABLE_PLAYER_PERSISTENCE
            Save();
#endif
        }

        public override void OnPreSerialization() {
            if (!isSynced) return;
            syncedState = (byte)state;
        }

        public override void OnDeserialization() {
            if (!isSynced) return;
            state = syncedState;
            UpdateState();
#if VRC_ENABLE_PLAYER_PERSISTENCE
            Save();
#endif
        }

        void UpdateAndSync() {
            UpdateState();
            if (isSynced) {
                if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
                RequestSerialization();
            }
        }

#if VRC_ENABLE_PLAYER_PERSISTENCE
        void Save() {
            if (string.IsNullOrEmpty(persistenceKey)) return;
            PlayerData.SetByte(persistenceKey, (byte)state);
        }

        bool Load(VRCPlayerApi player) {
            if (string.IsNullOrEmpty(persistenceKey)) return false;
            if (isSynced && !Networking.IsOwner(gameObject)) {
                PlayerData.SetByte(persistenceKey, syncedState);
                return false;
            }
            if (!PlayerData.TryGetByte(player, persistenceKey, out var savedState))
                return false;
            state = savedState;
            return true;
        }
#endif

        void UpdateState() {
            _UpdateState();
            foreach (var other in slaveSwitches) {
                if (other == null) continue;
                other.state = state;
                other._UpdateState();
            }
        }

#if COMPILER_UDONSHARP
        public
#endif
        void _UpdateState() {
            if (!enabled || !gameObject.activeInHierarchy) return;
            int stateMask = 1 << state;
            for (int i = 0, objectsLength = targetObjects.Length; i < objectsLength; i++) {
                var targetObject = targetObjects[i];
                if (!Utilities.IsValid(targetObject)) continue;
                bool shouldActive = (targetObjectEnableMask[i] & stateMask) != 0;
                switch (targetObjectTypes[i]) {
                    // Only small number of types are supported
                    case SwitchDrivenType.GameObject: ((GameObject)targetObject).SetActive(shouldActive); break;
                    case SwitchDrivenType.UdonBehaviour: ((IUdonEventReceiver)targetObject).enabled = shouldActive; break;
                    case SwitchDrivenType.Renderer: ((Renderer)targetObject).enabled = shouldActive; break;
                    case SwitchDrivenType.Collider: ((Collider)targetObject).enabled = shouldActive; break;
                    case SwitchDrivenType.Camera: ((Camera)targetObject).enabled = shouldActive; break;
                    case SwitchDrivenType.Rigidbody: ((Rigidbody)targetObject).isKinematic = !shouldActive; break;
                    case SwitchDrivenType.UGUISelectable: ((Selectable)targetObject).interactable = shouldActive; break;
                    case SwitchDrivenType.PositionConstraint: ((PositionConstraint)targetObject).constraintActive = shouldActive; break;
                    case SwitchDrivenType.RotationConstraint: ((RotationConstraint)targetObject).constraintActive = shouldActive; break;
                    case SwitchDrivenType.ScaleConstraint: ((ScaleConstraint)targetObject).constraintActive = shouldActive; break;
                    case SwitchDrivenType.ParentConstraint: ((ParentConstraint)targetObject).constraintActive = shouldActive; break;
                    case SwitchDrivenType.AimConstraint: ((AimConstraint)targetObject).constraintActive = shouldActive; break;
                    case SwitchDrivenType.LookAtConstraint: ((LookAtConstraint)targetObject).constraintActive = shouldActive; break;
                    case SwitchDrivenType.VRCPickup: ((VRC_Pickup)targetObject).pickupable = shouldActive; break;
                    case SwitchDrivenType.CustomRenderTexture:
                        ((CustomRenderTexture)targetObject).updateMode = shouldActive ?
                            CustomRenderTextureUpdateMode.Realtime : CustomRenderTextureUpdateMode.OnDemand;
                        break;
                    case SwitchDrivenType.ParticleSystemEmissionModule: { var m = ((ParticleSystem)targetObject).emission; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemShapeModule: { var m = ((ParticleSystem)targetObject).shape; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemVelocityOverLifetimeModule: { var m = ((ParticleSystem)targetObject).velocityOverLifetime; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemLimitVelocityOverLifetimeModule: { var m = ((ParticleSystem)targetObject).limitVelocityOverLifetime; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemInheritVelocityModule: { var m = ((ParticleSystem)targetObject).inheritVelocity; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemForceOverLifetimeModule: { var m = ((ParticleSystem)targetObject).forceOverLifetime; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemColorOverLifetimeModule: { var m = ((ParticleSystem)targetObject).colorOverLifetime; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemColorBySpeedModule: { var m = ((ParticleSystem)targetObject).colorBySpeed; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemSizeOverLifetimeModule: { var m = ((ParticleSystem)targetObject).sizeOverLifetime; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemSizeBySpeedModule: { var m = ((ParticleSystem)targetObject).sizeBySpeed; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemRotationOverLifetimeModule: { var m = ((ParticleSystem)targetObject).rotationOverLifetime; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemRotationBySpeedModule: { var m = ((ParticleSystem)targetObject).rotationBySpeed; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemExternalForcesModule: { var m = ((ParticleSystem)targetObject).externalForces; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemNoiseModule: { var m = ((ParticleSystem)targetObject).noise; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemCollisionModule: { var m = ((ParticleSystem)targetObject).collision; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemTriggerModule: { var m = ((ParticleSystem)targetObject).trigger; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemSubEmittersModule: { var m = ((ParticleSystem)targetObject).subEmitters; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemTextureSheetAnimationModule: { var m = ((ParticleSystem)targetObject).textureSheetAnimation; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemLightsModule: { var m = ((ParticleSystem)targetObject).lights; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemTrailModule: { var m = ((ParticleSystem)targetObject).trails; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemCustomDataModule: { var m = ((ParticleSystem)targetObject).customData; m.enabled = shouldActive; } break;
                }
            }
        }
    }
}