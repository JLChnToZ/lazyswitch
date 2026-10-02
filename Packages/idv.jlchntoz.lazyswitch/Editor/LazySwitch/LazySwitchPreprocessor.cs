using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Dynamics;
using VRC.SDK3.Data;
using UdonSharp;
using UdonSharpEditor;
using JLChnToZ.VRC.Foundation.Editors;
using UnityObject = UnityEngine.Object;
using PooledObjects = JLChnToZ.VRC.Foundation.PooledObjectExtensions;

namespace JLChnToZ.VRC {
    using static LazySwitchEditorUtils;

    sealed class LazySwitchPreprocessor : IPreprocessor {
        readonly List<LazySwitch> switches = new List<LazySwitch>();
        readonly Dictionary<LazySwitch, List<LazySwitch>> switchGroups = new Dictionary<LazySwitch, List<LazySwitch>>();
        readonly Dictionary<string, LazySwitch> persistenceKeyToMasterSwitch = new Dictionary<string, LazySwitch>();
        readonly Dictionary<UnityObject, (SwitchDrivenType objectType, int onFlags, int offFlags)> targetObjectEnableMask = new Dictionary<UnityObject, (SwitchDrivenType, int, int)>();

        public int Priority => -1;

        public void OnPreprocess(Scene scene) {
            try {
                foreach (var sw in scene.IterateAllComponents<LazySwitch>(false))
                    ConsolidateSwitchStates(sw);
                foreach (var kv in switchGroups)
                    ConfigureMasterSwitch(kv.Key, kv.Value);
                switchGroups.Clear();
                foreach (var sw in switches) {
                    CheckAndUpdateSyncMode(sw);
                    SetAllowedStates(sw);
                    SyncTooltipText(sw);
                    UpdateInteractiveAndCollider(sw);
                    FetchContactSettings(sw);   
                    UdonSharpEditorUtility.CopyProxyToUdon(sw);
                }
                foreach (var ped in scene.IterateAllComponents<PlayerEnterDetector>(false))
                    ProcessPlayerDetectors(ped);
            } finally {
                switches.Clear();
                switchGroups.Clear();
                persistenceKeyToMasterSwitch.Clear();
                targetObjectEnableMask.Clear();
            }
        }

        void ConsolidateSwitchStates(LazySwitch sw) {
            var masterSwitch = sw;
            while (masterSwitch != null) {
                var next = masterSwitch.masterSwitch;
                if (next == sw) masterSwitch.masterSwitch = next = null;
                if (next == null) {
                    // One persistency key should be assigned to only one master switch.
                    // If there are multiple master switches with the same persistency key,
                    // they will be merged into one group and the persistency key will be cleared to avoid conflict.
                    if (!string.IsNullOrEmpty(masterSwitch.persistenceKey)) {
                        if (!persistenceKeyToMasterSwitch.TryGetValue(masterSwitch.persistenceKey, out var existing)) {
                            persistenceKeyToMasterSwitch[masterSwitch.persistenceKey] = masterSwitch;
                        } else if (existing != masterSwitch) {
                            masterSwitch.persistenceKey = null;
                            masterSwitch.masterSwitch = existing;
                            continue;
                        }
                    }
                    if (!switchGroups.TryGetValue(masterSwitch, out var group)) {
                        switchGroups[masterSwitch] = group = new List<LazySwitch>();
                        masterSwitch.stateCount = 2;
                    }
                    group.Add(sw);
                    break;
                }
                masterSwitch = next;
            }
            bool isCurrentState = false;
            int animatorKeysLength = 0;
            for (
                int i = 0, s = -1, next = 0, currentStateMask = 0,
                    offsetLength = sw.targetObjectGroupOffsets.Length,
                    objectsLength = sw.targetObjects.Length;
                i < objectsLength;
                i++
            ) {
                while (i >= next && s < offsetLength) {
                    s++;
                    next = s < offsetLength ? sw.targetObjectGroupOffsets[s] : objectsLength;
                    isCurrentState = s == masterSwitch.state;
                    currentStateMask = 1 << s;
                }
                var srcObj = sw.targetObjects[i];
                UnityObject destObj;
                SwitchDrivenType objectType;
                string parameter = null;
                if (srcObj is UdonSharpBehaviour ub) {
                    destObj = UdonSharpEditorUtility.GetBackingUdonBehaviour(ub);
                    objectType = SwitchDrivenType.UdonBehaviour;
                } else if (srcObj is ParticleSystem) {
                    objectType = sw.targetObjectTypes[i];
                    if (objectType < SwitchDrivenType.ParticleSystemEmissionModule ||
                        objectType > SwitchDrivenType.ParticleSystemCustomDataModule)
                        continue;
                    destObj = srcObj;
                    ub = null;
                } else if (srcObj is Animator) {
                    objectType = sw.targetObjectTypes[i];
                    if (objectType < SwitchDrivenType.AnimatorBool ||
                        objectType > SwitchDrivenType.AnimatorTrigger ||
                        sw.targetObjectAnimatorKeys == null ||
                        sw.targetObjectAnimatorKeys.Length <= i ||
                        string.IsNullOrEmpty(sw.targetObjectAnimatorKeys[i]))
                        continue;
                    destObj = srcObj;
                    parameter = sw.targetObjectAnimatorKeys[i];
                    animatorKeysLength = i + 1;
                    ub = null;
                } else {
                    objectType = GetTypeCode(srcObj);
                    destObj = srcObj;
                    ub = null;
                }
                if (objectType == SwitchDrivenType.Unknown) continue;
                if (!destObj.IsAvailableOnRuntime()) continue;
                if (!targetObjectEnableMask.TryGetValue(destObj, out var flags)) flags = (objectType, 0, 0);
                bool enabled = ub != null ? ub.enabled : IsActive(destObj, objectType, parameter);
                if (isCurrentState == (sw.fixupMode != FixupMode.AsIs ? sw.targetObjectEnableMask[i] != 0 : enabled))
                    flags.onFlags |= currentStateMask;
                else
                    flags.offFlags |= currentStateMask;
                targetObjectEnableMask[destObj] = flags;
                if (sw.fixupMode == FixupMode.OnBuild) {
                    bool isEnableOnConfig = sw.targetObjectEnableMask[i] != 0;
                    if (isEnableOnConfig != enabled) {
                        if (ub != null)
                            ub.enabled = isEnableOnConfig;
                        else
                            ToggleActive(destObj, objectType);
                    }
                }
            }
            masterSwitch.stateCount = Mathf.Max(masterSwitch.stateCount, sw.targetObjectGroupOffsets.Length + 1);
            sw.targetObjectGroupOffsets = Array.Empty<int>(); // Clean up on build to save space
            sw.targetObjects = new UnityObject[targetObjectEnableMask.Count];
            sw.targetObjectEnableMask = new int[targetObjectEnableMask.Count];
            sw.targetObjectTypes = new SwitchDrivenType[targetObjectEnableMask.Count];
            if (animatorKeysLength <= 0)
                sw.targetObjectAnimatorKeys = Array.Empty<string>();
            else if (sw.targetObjectAnimatorKeys.Length != animatorKeysLength)
                Array.Resize(ref sw.targetObjectAnimatorKeys, animatorKeysLength);
            int j = 0;
            foreach (var kv in targetObjectEnableMask) {
                sw.targetObjects[j] = kv.Key;
                var (objectType, onFlags, offFlags) = kv.Value;
                sw.targetObjectEnableMask[j] = onFlags != 0 ? onFlags : ~offFlags;
                sw.targetObjectTypes[j] = objectType;
                j++;
            }
            targetObjectEnableMask.Clear();
        }

        void ConfigureMasterSwitch(LazySwitch masterSwitch, List<LazySwitch> canidates) {
            canidates.Remove(masterSwitch);
            masterSwitch.slaveSwitches = canidates.ToArray();
            foreach (var sw in canidates) {
                sw.masterSwitch = masterSwitch;
                sw.state = masterSwitch.state;
                sw.stateCount = masterSwitch.stateCount;
                sw.persistenceKey = null;
                sw.isSynced = false;
                switches.Add(sw);
            }
#if UNITY_ANDROID || UNITY_IOS
            if (masterSwitch.separatePersistencePerPlatform && !string.IsNullOrEmpty(masterSwitch.persistenceKey))
#if UNITY_ANDROID
                masterSwitch.persistenceKey += "_Android";
#elif UNITY_IOS
                masterSwitch.persistenceKey += "_iOS";
#endif
#endif
            switches.Add(masterSwitch);
        }

        static void SetAllowedStates(LazySwitch sw) {
            sw.allowedStatesMask &= ~((~0) << sw.stateCount);
            using (PooledObjects.Get(out List<byte> allowStates, sw.stateCount)) {
                for (int state = 0; state < sw.stateCount; state++) {
                    if ((sw.allowedStatesMask & (1 << state)) == 0) continue;
                    allowStates.Add((byte)state);
                }
                sw.allowedStatesList = allowStates.ToArray();
                sw.allowedStatesCount = allowStates.Count;
            }
        }

        static void SyncTooltipText(LazySwitch sw) {
            if (sw.tooltipTexts == null || sw.tooltipTexts.Length == 0) return;
            sw.tooltipTexts[0] = UdonSharpEditorUtility.GetBackingUdonBehaviour(sw).interactText;
        }

        static void UpdateInteractiveAndCollider(LazySwitch sw) {
            bool hasPickup = sw.TryGetComponent(out VRC_Pickup _);
            if (sw.isInteractive && (hasPickup ||
                sw.allowedStatesCount == 0 ||
                sw.TryGetComponent(out Button _) ||
                sw.TryGetComponent(out Toggle _)))
                sw.isInteractive = false;
            if (sw.TryGetComponent(out Collider _)) return;
            var collider = sw.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            if (!sw.isInteractive && !hasPickup) {
                collider.enabled = false;
                collider.size = Vector3.zero;
            } else if (sw.TryGetComponent(out Renderer renderer)) {
                var bounds = renderer.localBounds;
                collider.center = bounds.center;
                collider.size = bounds.size;
            } else if (sw.TryGetComponent(out RectTransform rt)) {
                var rect = rt.rect;
                collider.center = rect.center;
                collider.size = rect.size;
            }
        }

        static void FetchContactSettings(LazySwitch sw) {
            if (!sw.TryGetComponent(out ContactReceiver receiver)) {
                sw.contactSensitiveMode = 0;
                sw.contactTransform = null;
                return;
            }
            if (sw.contactSensitiveMode == 0)
                sw.contactSensitiveMode = 1;
            else if (sw.contactSensitiveMode > 1) {
                if (Mathf.Approximately(sw.contactDirection.sqrMagnitude, 0F))
                    sw.contactSensitiveMode = 1;
                else
                    sw.contactDirection.Normalize();
                if (sw.contactSensitiveMode > 2) {
                    sw.contactTransform = receiver.GetRootTransform();
                    sw.contactDirection = receiver.rotation * sw.contactDirection;
                } else
                    sw.contactTransform = null;
            }
            receiver.contentTypes |= DynamicsUsageFlags.Avatar;
            if (sw.hapticsStrength <= 0F) return;
            sw.tagToHaptics ??= new DataDictionary();
            using (PooledObjects.Get(out HashSet<string> tags)) {
                tags.UnionWith(receiver.collisionTags);
                AddLRTagsIfDistinct(tags, receiver.collisionTags, sw.tagToHaptics, "Hand");
                AddLRTagsIfDistinct(tags, receiver.collisionTags, sw.tagToHaptics, "Finger");
                AddLRTagsIfDistinct(tags, receiver.collisionTags, sw.tagToHaptics, "FingerIndex");
                AddLRTagsIfDistinct(tags, receiver.collisionTags, sw.tagToHaptics, "FingerMiddle");
                AddLRTagsIfDistinct(tags, receiver.collisionTags, sw.tagToHaptics, "FingerRing");
                AddLRTagsIfDistinct(tags, receiver.collisionTags, sw.tagToHaptics, "FingerLittle");
            }
        }

        static void AddLRTagsIfDistinct(HashSet<string> tags, List<string> dest, DataDictionary mapping, string tag) {
            if (!tags.Contains(tag)) return;
            AddIfDistinct(tags, dest, mapping, $"{tag}L", VRC_Pickup.PickupHand.Left);
            AddIfDistinct(tags, dest, mapping, $"{tag}R", VRC_Pickup.PickupHand.Right);
        }

        static void AddIfDistinct(HashSet<string> tags, List<string> dest, DataDictionary mapping, string tag, VRC_Pickup.PickupHand hand) {
            if (tags.Add(tag)) dest.Add(tag);
            mapping[tag] = (int)hand;
        }

        void ProcessPlayerDetectors(PlayerEnterDetector ped) {
            if (!ped.anyOwnedObjects || !ped.detectAllPlayers) return;
            var sw = ped.lazySwitch;
            if (!Utils.IsAvailableOnRuntime(sw)) return;
            using (PooledObjects.Get(out List<Collider> tempColliders)) {
                ped.GetComponents(tempColliders);
                foreach (var collider in tempColliders)
                    collider.isTrigger = true;
            }
            using (PooledObjects.Get(out HashSet<GameObject> gameObjects)) {
                foreach (var obj in sw.targetObjects)
                    if (obj != null && obj is GameObject go)
                        foreach (var component in go.IterateAllComponents<UdonBehaviour>(false))
                            if (component != null && component.SyncMethod != Networking.SyncType.None)
                                gameObjects.Add(component.gameObject);
                ped.childrenToCheck = new GameObject[gameObjects.Count];
                gameObjects.CopyTo(ped.childrenToCheck);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(ped);
        }
    }
}