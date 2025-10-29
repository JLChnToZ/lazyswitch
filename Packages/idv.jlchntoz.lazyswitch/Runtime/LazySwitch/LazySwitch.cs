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
using JLChnToZ.VRC.Foundation.I18N;

namespace JLChnToZ.VRC {
    /// <summary>
    /// A multi-purpose switch.
    /// </summary>
    [AddComponentMenu("JLChnToZ/Lazy Switch")]
    [BindEvent(typeof(Button), nameof(Button.onClick), nameof(Interact))]
    [BindEvent(typeof(Toggle), nameof(Toggle.onValueChanged), nameof(Interact))]
    public class LazySwitch : UdonSharpBehaviour {
        [SerializeField, UdonMeta(UdonMetaAttributeType.NetworkSyncModeManual)] bool isManualSync;
        [SerializeField] internal int state;
        [SerializeField, LocalizedLabel] internal bool isSynced;
        [SerializeField, LocalizedLabel] internal bool isRandomized;
        [SerializeField, LocalizedLabel] internal LazySwitch masterSwitch;
        [SerializeField, HideInInspector] internal LazySwitch[] slaveSwitches;
        [SerializeField] internal Object[] targetObjects;
        [SerializeField, HideInInspector] internal SwitchDrivenType[] targetObjectTypes;
        [SerializeField, HideInInspector] internal int[] targetObjectEnableMask;
        [SerializeField, HideInInspector] internal string[] targetObjectAnimatorKeys;
        [SerializeField] internal int[] targetObjectGroupOffsets;
        [SerializeField] internal int stateCount;
        [SerializeField, LocalizedLabel, LocalizedEnum] internal FixupMode fixupMode;
#if VRC_ENABLE_PLAYER_PERSISTENCE
        [SerializeField, LocalizedLabel] internal string persistenceKey;
        [SerializeField, LocalizedLabel] internal bool separatePersistencePerPlatform;
        [SerializeField, LocalizedLabel] internal bool separatePersistenceForVR;
#endif
        [UdonSynced] byte syncedState;
        object[] resolvedTargetObjects;
        int[] targetObjectAnimatorHashes;
        bool hasInit;
        int objectCount;

        /// <summary>
        /// The current state of this switch.
        /// </summary>
        public int State {
            get => state;
            set {
                if (Utilities.IsValid(masterSwitch)) {
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
            if (Utilities.IsValid(masterSwitch)) {
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

        void Init() {
            if (hasInit) return;
            hasInit = true;
            int animationCount = 0;
            if (Utilities.IsValid(targetObjectAnimatorKeys)) {
                animationCount = targetObjectAnimatorKeys.Length;
                if (animationCount > 0) targetObjectAnimatorHashes = new int[animationCount];
            }
            objectCount = targetObjects.Length;
            resolvedTargetObjects = new object[objectCount];
            for (int i = 0; i < objectCount; i++) {
                var targetObject = targetObjects[i];
                if (!Utilities.IsValid(targetObject)) continue;
                var ps = (ParticleSystem)targetObject;
                switch (targetObjectTypes[i]) {
                    case SwitchDrivenType.ParticleSystemCollisionModule: resolvedTargetObjects[i] = ps.collision; break;
                    case SwitchDrivenType.ParticleSystemTriggerModule: resolvedTargetObjects[i] = ps.trigger; break;
                    case SwitchDrivenType.ParticleSystemEmissionModule: resolvedTargetObjects[i] = ps.emission; break;
                    case SwitchDrivenType.ParticleSystemShapeModule: resolvedTargetObjects[i] = ps.shape; break;
                    case SwitchDrivenType.ParticleSystemVelocityOverLifetimeModule: resolvedTargetObjects[i] = ps.velocityOverLifetime; break;
                    case SwitchDrivenType.ParticleSystemLimitVelocityOverLifetimeModule: resolvedTargetObjects[i] = ps.limitVelocityOverLifetime; break;
                    case SwitchDrivenType.ParticleSystemInheritVelocityModule: resolvedTargetObjects[i] = ps.inheritVelocity; break;
                    case SwitchDrivenType.ParticleSystemForceOverLifetimeModule: resolvedTargetObjects[i] = ps.forceOverLifetime; break;
                    case SwitchDrivenType.ParticleSystemColorOverLifetimeModule: resolvedTargetObjects[i] = ps.colorOverLifetime; break;
                    case SwitchDrivenType.ParticleSystemColorBySpeedModule: resolvedTargetObjects[i] = ps.colorBySpeed; break;
                    case SwitchDrivenType.ParticleSystemSizeOverLifetimeModule: resolvedTargetObjects[i] = ps.sizeOverLifetime; break;
                    case SwitchDrivenType.ParticleSystemSizeBySpeedModule: resolvedTargetObjects[i] = ps.sizeBySpeed; break;
                    case SwitchDrivenType.ParticleSystemRotationOverLifetimeModule: resolvedTargetObjects[i] = ps.rotationOverLifetime; break;
                    case SwitchDrivenType.ParticleSystemRotationBySpeedModule: resolvedTargetObjects[i] = ps.rotationBySpeed; break;
                    case SwitchDrivenType.ParticleSystemExternalForcesModule: resolvedTargetObjects[i] = ps.externalForces; break;
                    case SwitchDrivenType.ParticleSystemNoiseModule: resolvedTargetObjects[i] = ps.noise; break;
                    case SwitchDrivenType.ParticleSystemLightsModule: resolvedTargetObjects[i] = ps.lights; break;
                    case SwitchDrivenType.ParticleSystemTrailModule: resolvedTargetObjects[i] = ps.trails; break;
                    case SwitchDrivenType.ParticleSystemCustomDataModule: resolvedTargetObjects[i] = ps.customData; break;
                    default: resolvedTargetObjects[i] = targetObject; break;
                }
                if (i < animationCount) {
                    var key = targetObjectAnimatorKeys[i];
                    if (!string.IsNullOrEmpty(key)) targetObjectAnimatorHashes[i] = Animator.StringToHash(key);
                }
            }
        }

#if VRC_ENABLE_PLAYER_PERSISTENCE
        public override void OnPlayerRestored(VRCPlayerApi player) {
            if (player.isLocal && !Utilities.IsValid(masterSwitch) && Load(player)) UpdateAndSync();
        }
#endif

        public override void Interact() {
            if (gameObject.activeInHierarchy && enabled && !DisableInteractive) _SwitchState();
        }

        /// <summary>
        /// Switch to the next state, or a random state if randomized.
        /// </summary>
        /// <remarks>
        /// This method bypasses interaction checks, so it can be called from other scripts or events.
        /// </remarks>
        public void _SwitchState() {
            if (Utilities.IsValid(masterSwitch)) {
                masterSwitch._SwitchState();
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
                if (isManualSync) RequestSerialization();
            }
        }

#if VRC_ENABLE_PLAYER_PERSISTENCE
        bool CheckPersistenceKey() {
            bool hasPersistenceKey = !string.IsNullOrEmpty(persistenceKey);
            if (separatePersistenceForVR) {
                if (hasPersistenceKey && Networking.LocalPlayer.IsUserInVR())
                    persistenceKey += "_VR";
                separatePersistenceForVR = false;
            }
            return hasPersistenceKey;
        }

        void Save() {
            if (!CheckPersistenceKey()) return;
            PlayerData.SetByte(persistenceKey, (byte)state);
        }

        bool Load(VRCPlayerApi player) {
            if (!CheckPersistenceKey()) return false;
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
            Init();
            int stateMask = 1 << state;
            for (int i = 0; i < objectCount; i++) {
                var targetObject = resolvedTargetObjects[i];
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
                    case SwitchDrivenType.ParticleSystemEmissionModule: { var m = (ParticleSystem.EmissionModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemShapeModule: { var m = (ParticleSystem.ShapeModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemVelocityOverLifetimeModule: { var m = (ParticleSystem.VelocityOverLifetimeModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemLimitVelocityOverLifetimeModule: { var m = (ParticleSystem.LimitVelocityOverLifetimeModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemInheritVelocityModule: { var m = (ParticleSystem.InheritVelocityModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemForceOverLifetimeModule: { var m = (ParticleSystem.ForceOverLifetimeModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemColorOverLifetimeModule: { var m = (ParticleSystem.ColorOverLifetimeModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemColorBySpeedModule: { var m = (ParticleSystem.ColorBySpeedModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemSizeOverLifetimeModule: { var m = (ParticleSystem.SizeOverLifetimeModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemSizeBySpeedModule: { var m = (ParticleSystem.SizeBySpeedModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemRotationOverLifetimeModule: { var m = (ParticleSystem.RotationOverLifetimeModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemRotationBySpeedModule: { var m = (ParticleSystem.RotationBySpeedModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemExternalForcesModule: { var m = (ParticleSystem.ExternalForcesModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemNoiseModule: { var m = (ParticleSystem.NoiseModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemCollisionModule: { var m = (ParticleSystem.CollisionModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemTriggerModule: { var m = (ParticleSystem.TriggerModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemSubEmittersModule: { var m = (ParticleSystem.SubEmittersModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemTextureSheetAnimationModule: { var m = (ParticleSystem.TextureSheetAnimationModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemLightsModule: { var m = (ParticleSystem.LightsModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemTrailModule: { var m = (ParticleSystem.TrailModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.ParticleSystemCustomDataModule: { var m = (ParticleSystem.CustomDataModule)targetObject; m.enabled = shouldActive; } break;
                    case SwitchDrivenType.AnimatorBool: ((Animator)targetObject).SetBool(targetObjectAnimatorHashes[i], shouldActive); break;
                    case SwitchDrivenType.AnimatorTrigger:
                        if (shouldActive) ((Animator)targetObject).SetTrigger(targetObjectAnimatorHashes[i]);
                        else ((Animator)targetObject).ResetTrigger(targetObjectAnimatorHashes[i]);
                        break;
                }
            }
        }
    }
}