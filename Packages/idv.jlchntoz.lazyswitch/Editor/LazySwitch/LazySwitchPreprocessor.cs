using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.Udon;
using UdonSharp;
using UdonSharpEditor;
using JLChnToZ.VRC.Foundation.Editors;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.VRC {
    using static LazySwitchEditorUtils;

    sealed class LazySwitchPreprocessor : IPreprocessor {
        static readonly List<Collider> tempColliders = new List<Collider>();
        readonly List<LazySwitch> switches = new List<LazySwitch>();
        readonly HashSet<GameObject> gameObjects = new HashSet<GameObject>();
        readonly Dictionary<LazySwitch, List<LazySwitch>> switchGroups = new Dictionary<LazySwitch, List<LazySwitch>>();
        readonly Dictionary<UnityObject, (SwitchDrivenType objectType, int onFlags, int offFlags)> targetObjectEnableMask = new Dictionary<UnityObject, (SwitchDrivenType, int, int)>();

        public int Priority => 0;

        public void OnPreprocess(Scene scene) {
            foreach (var sw in scene.IterateAllComponents<LazySwitch>(false))
                ConsolidateSwitchStates(sw);
            foreach (var kv in switchGroups)
                ConfigureMasterSwitch(kv.Key, kv.Value);
            switchGroups.Clear();
            foreach (var sw in switches)
                UdonSharpEditorUtility.CopyProxyToUdon(sw);
            foreach (var ped in scene.IterateAllComponents<PlayerEnterDetector>(false))
                ProcessPlayerDetectors(ped);
        }

        void ConsolidateSwitchStates(LazySwitch sw) {
            var masterSwitch = sw;
            while (masterSwitch != null) {
                var next = masterSwitch.masterSwitch;
                if (next == sw) masterSwitch.masterSwitch = next = null;
                if (next == null) {
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
#if VRC_ENABLE_PLAYER_PERSISTENCE
                sw.persistenceKey = null;
#endif
                sw.isSynced = false;
            }
        }

        void ProcessPlayerDetectors(PlayerEnterDetector ped) {
            if (!ped.anyOwnedObjects || !ped.detectAllPlayers) return;
            var sw = ped.lazySwitch;
            if (!Utils.IsAvailableOnRuntime(sw)) return;
            ped.GetComponents(tempColliders);
            foreach (var collider in tempColliders)
                collider.isTrigger = true;
            foreach (var obj in sw.targetObjects)
                if (obj != null && obj is GameObject go)
                    foreach (var component in go.IterateAllComponents<UdonBehaviour>(false))
                        if (component != null && component.SyncMethod != Networking.SyncType.None)
                            gameObjects.Add(component.gameObject);
            ped.childrenToCheck = new GameObject[gameObjects.Count];
            gameObjects.CopyTo(ped.childrenToCheck);
            gameObjects.Clear();
            UdonSharpEditorUtility.CopyProxyToUdon(ped);
        }
    }
}