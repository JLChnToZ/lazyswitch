using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UdonSharp;
using UdonSharpEditor;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.VRC {
    using static LazySwitchEditorUtils;

    sealed class LazySwitchPreprocessor : IProcessSceneWithReport {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report) {
            var switches = new List<LazySwitch>();
            foreach (var rootGameObject in scene.GetRootGameObjects())
                switches.AddRange(rootGameObject.GetComponentsInChildren<LazySwitch>(true));
            if (switches.Count == 0) return;
            var switchGroups = new Dictionary<LazySwitch, List<LazySwitch>>();
            var targetObjectEnableMask = new Dictionary<UnityObject, (SwitchDrivenType objectType, int onFlags, int offFlags)>();
            foreach (var sw in switches) {
                if (!IsAvailableOnRuntime(sw.gameObject)) continue;
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
                for (
                    int i = 0, s = -1, next = 0, currentStateMask = 0,
                        offsetLength = sw.targetObjectGroupOffsets.Length,
                        objectsLength = sw.targetObjects.Length;
                    i < objectsLength;
                    i++
                ) {
                    while (next >= i && s < offsetLength) {
                        s++;
                        next = s < offsetLength ? sw.targetObjectGroupOffsets[s] : objectsLength;
                        isCurrentState = s == masterSwitch.state;
                        currentStateMask = 1 << s;
                    }
                    var srcObj = sw.targetObjects[i];
                    UnityObject destObj;
                    SwitchDrivenType objectType;
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
                    } else {
                        objectType = GetTypeCode(srcObj);
                        destObj = srcObj;
                        ub = null;
                    }
                    if (objectType == SwitchDrivenType.Unknown) continue;
                    if (!IsAvailableOnRuntime(destObj)) continue;
                    if (!targetObjectEnableMask.TryGetValue(destObj, out var flags)) flags = (objectType, 0, 0);
                    bool enabled = ub != null ? ub.enabled : IsActive(destObj, objectType);
                    if (isCurrentState == enabled)
                        flags.onFlags |= currentStateMask;
                    else
                        flags.offFlags |= currentStateMask;
                    targetObjectEnableMask[destObj] = flags;
                }
                masterSwitch.stateCount = Mathf.Max(masterSwitch.stateCount, sw.targetObjectGroupOffsets.Length);
                sw.targetObjectGroupOffsets = Array.Empty<int>(); // Clean up on build to save space
                sw.targetObjects = new GameObject[targetObjectEnableMask.Count];
                sw.targetObjectEnableMask = new int[targetObjectEnableMask.Count];
                sw.targetObjectTypes = new SwitchDrivenType[targetObjectEnableMask.Count];
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
            foreach (var kv in switchGroups) {
                var masterSwitch = kv.Key;
                var canidates = kv.Value;
                if (!IsAvailableOnRuntime(masterSwitch.gameObject))
                    foreach (var sw in canidates)
                        if (sw != masterSwitch && IsAvailableOnRuntime(sw.gameObject)) {
                            masterSwitch = sw;
                            break;
                        }
                canidates.Remove(masterSwitch);
                masterSwitch.slaveSwitches = canidates.ToArray();
                foreach (var sw in canidates) {
                    sw.masterSwitch = masterSwitch;
                    sw.state = masterSwitch.state;
                    sw.stateCount = masterSwitch.stateCount;
                    sw.isSynced = false;
                }
            }
            foreach (var sw in switches) UdonSharpEditorUtility.CopyProxyToUdon(sw);
        }

        static bool IsAvailableOnRuntime(UnityObject gameObjectOrComponent) {
            if (gameObjectOrComponent == null) return false;
            for (var transform =
                gameObjectOrComponent is Transform t ? t :
                gameObjectOrComponent is GameObject go ? go.transform :
                gameObjectOrComponent is Component c ? c.transform :
                null;
                transform != null; transform = transform.parent)
                if (transform.CompareTag("EditorOnly")) return false;
            return true;
        }
    }
}