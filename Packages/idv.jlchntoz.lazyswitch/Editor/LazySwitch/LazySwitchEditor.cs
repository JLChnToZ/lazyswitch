using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEditor;
using UnityEditorInternal;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using UdonSharp;
using UdonSharpEditor;
using JLChnToZ.VRC.Foundation.I18N.Editors;
#if CYAN_TRIGGER_IMPORTED
using Cyan.CT.Editor;
#endif
using UnityObject = UnityEngine.Object;
using ACParameterType = UnityEngine.AnimatorControllerParameterType;

namespace JLChnToZ.VRC {
    using static LazySwitchEditorUtils;

    [CustomEditor(typeof(LazySwitch))]
    [CanEditMultipleObjects]
    sealed class LazySwitchEditor : LazySwitchEditorBase {
        static readonly string[] allowedStatesOptions = new string[32];
        static readonly List<(UnityObject component, SwitchDrivenType subType, string parameter)> menuComponents = new List<(UnityObject, SwitchDrivenType, string)>();
        static GUIContent tempContent;
        SerializedProperty stateProp, isSyncedProp, isRandomizedProp, masterSwitchProp, fixupModeProp, allowedStatesMaskProp,
            targetObjectsProp, targetObjectTypesProp, targetObjectGroupOffsetsProp, targetObjectEnableMaskProp, targetObjectAnimatorKeysProp,
            tooltipTextsProp, useLocalizedTooltipsProp, interactTextProp;
#if VRC_ENABLE_PLAYER_PERSISTENCE
        SerializedProperty persistenceKeyProp, separatePersistencePerPlatformProp, separatePersistenceForVRProp;
#endif
        ReorderableList targetObjectsList;
        readonly List<Entry> targetObjectsEntries = new List<Entry>();
        int masterSwitchState;
        bool syncActiveState;
        bool entriesUpdated;
        SerializedObject backingUdonSerializedObject;
        UdonBehaviour[] backingUdonBehaviours;

        byte LastSeparatorIndex {
            get {
                for (int i = targetObjectsEntries.Count - 1; i >= 0; i--) {
                    var entry = targetObjectsEntries[i];
                    if (entry.isSeparator) return entry.separatorIndex;
                }
                return 0;
            }
        }

        bool IsSingleState => targetObjectGroupOffsetsProp.arraySize switch {
            0 => true,
            1 => targetObjectGroupOffsetsProp.GetArrayElementAtIndex(0).intValue == 0,
            _ => false,
        };

        static string GetBehaviourDisplayName(UnityObject obj, SwitchDrivenType subType = SwitchDrivenType.Unknown, string parameter = null) {
            if (obj == null) return "<None>";
            if (obj is UdonBehaviour ub) {
                var proxy = UdonSharpEditorUtility.GetProxyBehaviour(ub);
                if (proxy != null) return ObjectNames.GetInspectorTitle(proxy);
                var programSource = ub.programSource;
                if (programSource != null) {
#if CYAN_TRIGGER_IMPORTED
                    if (programSource is CyanTriggerProgramAsset ctProgram)
                        return $"{ctProgram.GetCyanTriggerProgramName()} (Cyan Trigger)";
#endif
                    return $"{programSource.name} (Udon)";
                }
            }
            if (obj is ParticleSystem)
                return ObjectNames.NicifyVariableName(subType.ToString());
            if (obj is Animator)
                return $"{parameter} (Animator)";
            return ObjectNames.NicifyVariableName(obj.GetType().Name);
        }

        void OnEnable() {
            if (tempContent == null) tempContent = new GUIContent();
            stateProp = serializedObject.FindProperty(nameof(LazySwitch.state));
            isSyncedProp = serializedObject.FindProperty(nameof(LazySwitch.isSynced));
            isRandomizedProp = serializedObject.FindProperty(nameof(LazySwitch.isRandomized));
            masterSwitchProp = serializedObject.FindProperty(nameof(LazySwitch.masterSwitch));
            targetObjectsProp = serializedObject.FindProperty(nameof(LazySwitch.targetObjects));
            targetObjectTypesProp = serializedObject.FindProperty(nameof(LazySwitch.targetObjectTypes));
            targetObjectGroupOffsetsProp = serializedObject.FindProperty(nameof(LazySwitch.targetObjectGroupOffsets));
            targetObjectEnableMaskProp = serializedObject.FindProperty(nameof(LazySwitch.targetObjectEnableMask));
            targetObjectAnimatorKeysProp = serializedObject.FindProperty(nameof(LazySwitch.targetObjectAnimatorKeys));
            tooltipTextsProp = serializedObject.FindProperty(nameof(LazySwitch.tooltipTexts));
            useLocalizedTooltipsProp = serializedObject.FindProperty(nameof(LazySwitch.useLocalizedTooltips));
            fixupModeProp = serializedObject.FindProperty(nameof(LazySwitch.fixupMode));
            allowedStatesMaskProp = serializedObject.FindProperty(nameof(LazySwitch.allowedStatesMask));
#if VRC_ENABLE_PLAYER_PERSISTENCE
            persistenceKeyProp = serializedObject.FindProperty(nameof(LazySwitch.persistenceKey));
            separatePersistencePerPlatformProp = serializedObject.FindProperty(nameof(LazySwitch.separatePersistencePerPlatform));
            separatePersistenceForVRProp = serializedObject.FindProperty(nameof(LazySwitch.separatePersistenceForVR));
#endif
            if (targetObjectsList == null)
                targetObjectsList = new ReorderableList(targetObjectsEntries, typeof(Entry)) {
                    drawHeaderCallback = DrawTargetObjectHeader,
                    drawElementCallback = DrawTargetObjectElement,
                    drawNoneElementCallback = DrawEmptyTargetObjectElement,
                    drawFooterCallback = DrawTargetObjectFooter,
                    onAddDropdownCallback = OnAddTargetDropdown,
                    onRemoveCallback = OnRemoveEntry,
                    onReorderCallbackWithDetails = OnReorderTargetObjects,
                    elementHeightCallback = CalculateTargetObjectElementHeight,
                };
            LoadEntries();
            Undo.undoRedoPerformed += LoadEntries;
        }

        void OnDisable() {
            Undo.undoRedoPerformed -= LoadEntries;
            backingUdonSerializedObject?.Dispose();
            backingUdonSerializedObject = null;
            interactTextProp = null;
            backingUdonBehaviours = null;
        }

        void CheckAndUpdateSyncMode() {
            if (serializedObject.isEditingMultipleObjects)
                foreach (var target in targets)
                    LazySwitchEditorUtils.CheckAndUpdateSyncMode(target as LazySwitch);
            else
                LazySwitchEditorUtils.CheckAndUpdateSyncMode(target as LazySwitch);
        }

        bool InitBackingUdonSerializedProperties() {
            if (backingUdonSerializedObject != null) return true;
            using (ListPool<UdonBehaviour>.Get(out var tempBackingTargets)) {
                foreach (var target in targets) {
                    if (!(target is UdonSharpBehaviour usb)) continue;
                    if (usb.TryGetComponent(out LazySwitchInteractionBlocker _)) continue;
                    var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(usb);
                    if (ub != null) tempBackingTargets.Add(ub);
                }
                if (tempBackingTargets.Count > 0) {
                    backingUdonBehaviours = tempBackingTargets.ToArray();
                    backingUdonSerializedObject = new SerializedObject(backingUdonBehaviours);
                    interactTextProp = backingUdonSerializedObject.FindProperty(nameof(UdonBehaviour.interactText));
                    return true;
                }
            }
            return false;
        }

        protected override bool DrawUdonSharpHeader() {
            if (UdonSharpGUI.DrawProgramSource(target, false)) return true;
            UdonSharpGUI.DrawCompileErrorTextArea();
            UdonSharpGUI.DrawUtilities(target);
            EditorGUILayout.Space();
            return false;
        }

        protected override void DrawContent() {
            if (InitBackingUdonSerializedProperties()) {
                UdonSharpGUI.DrawInteractSettings(backingUdonBehaviours);
                EditorGUILayout.Space();
            }
            serializedObject.Update();
            entriesUpdated = false;
            var isPlaying = EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(isPlaying)) {
                EditorGUILayout.PropertyField(masterSwitchProp);
                if (masterSwitchProp.hasMultipleDifferentValues) {
                    using (new EditorGUI.DisabledScope(true)) {
                        EditorGUILayout.PropertyField(isSyncedProp);
                        EditorGUILayout.PropertyField(stateProp);
                    }
                } else if (masterSwitchProp.objectReferenceValue != null) {
                    if (isSyncedProp.boolValue) isSyncedProp.boolValue = false;
                    using (new EditorGUI.DisabledScope(true)) {
                        var masterSwitch = masterSwitchProp.objectReferenceValue as LazySwitch;
                        masterSwitchState = masterSwitch.state;
                        EditorGUILayout.Toggle(i18n["JLChnToZ.VRC.LazySwitch.isSynced"], masterSwitch.isSynced);
                        EditorGUILayout.Toggle(i18n["JLChnToZ.VRC.LazySwitch.isRandomized"], masterSwitch.isRandomized);
                        if (isPlaying) EditorGUILayout.IntField(stateProp.displayName, masterSwitchState);
#if VRC_ENABLE_PLAYER_PERSISTENCE
                        EditorGUILayout.TextField(persistenceKeyProp.displayName, masterSwitch.persistenceKey);
#endif
                    }
                    EditorGUILayout.HelpBox(i18n["JLChnToZ.VRC.LazySwitch.masterSwitch:info"], MessageType.Info);
                } else {
                    EditorGUILayout.PropertyField(isSyncedProp);
                    EditorGUILayout.PropertyField(isRandomizedProp);
                    masterSwitchState = stateProp.intValue;
                    if (isPlaying) EditorGUILayout.PropertyField(stateProp);
#if VRC_ENABLE_PLAYER_PERSISTENCE
                    EditorGUILayout.PropertyField(persistenceKeyProp);
                    if (!string.IsNullOrEmpty(persistenceKeyProp.stringValue)) {
                        if (isSyncedProp.boolValue)
                            EditorGUILayout.HelpBox(i18n["JLChnToZ.VRC.LazySwitch.persistence:info"], MessageType.Info);
                        using (new EditorGUI.IndentLevelScope()) {
                            EditorGUILayout.PropertyField(separatePersistencePerPlatformProp);
                            EditorGUILayout.PropertyField(separatePersistenceForVRProp);
                        }
                    }
#endif
                }
                EditorGUILayout.PropertyField(fixupModeProp);
                var fixupMode = (FixupMode)fixupModeProp.intValue;
                switch (fixupMode) {
                    case FixupMode.AsIs:
                        EditorGUILayout.HelpBox(i18n["JLChnToZ.VRC.FixupMode.AsIs:info"], MessageType.Info);
                        break;
                    case FixupMode.OnBuild:
                        EditorGUILayout.HelpBox(i18n["JLChnToZ.VRC.FixupMode.OnBuild:info"], MessageType.Info);
                        break;
                    case FixupMode.OnEnable:
                        EditorGUILayout.HelpBox(i18n["JLChnToZ.VRC.FixupMode.OnEnable:info"], MessageType.Info);
                        break;
                }
                syncActiveState = fixupMode == FixupMode.AsIs;
                var rect = EditorGUILayout.GetControlRect();
                using (var scope = new EditorGUI.PropertyScope(rect, null, allowedStatesMaskProp))
                    allowedStatesMaskProp.intValue = EditorGUI.MaskField(
                        rect,
                        i18n.GetLocalizedContent("JLChnToZ.VRC.LazySwitch.allowedStates"),
                        allowedStatesMaskProp.intValue,
                        allowedStatesOptions
                    );
                EditorGUILayout.PropertyField(useLocalizedTooltipsProp);
                EditorGUILayout.Space();
                targetObjectsList.DoLayoutList();
            }
            if (entriesUpdated) SaveEntries();
            if (targetObjectGroupOffsetsProp.hasMultipleDifferentValues ||
                targetObjectsProp.hasMultipleDifferentValues ||
                targetObjectTypesProp.hasMultipleDifferentValues)
                EditorGUILayout.HelpBox(i18n["JLChnToZ.VRC.LazySwitch.multiedit"], MessageType.Warning);
            CheckAndUpdateSyncMode();
            serializedObject.ApplyModifiedProperties();
        }

        protected override void OnLanguageChanged() {
            for (int i = 0; i < allowedStatesOptions.Length; i++)
                allowedStatesOptions[i] = string.Format(i18n["JLChnToZ.VRC.LazySwitch.state"], i);
        }

        float CalculateTargetObjectElementHeight(int index) {
            if (index < 0 || index >= targetObjectsEntries.Count) return 0;
            float height = 0;
            var entry = targetObjectsEntries[index];
            if (entry.isSeparator || (index == 0 && !EditorApplication.isPlayingOrWillChangePlaymode)) {
                tempContent.text = $"State {entry.separatorIndex}";
                height += Mathf.Max(
                    (IsSingleState ? EditorStyles.toggle : EditorStyles.radioButton).CalcHeight(GUIContent.none, 16F),
                    EditorStyles.miniBoldLabel.CalcHeight(tempContent, EditorGUIUtility.currentViewWidth - 16F)
                );
            }
            if (!entry.isSeparator)
                height += EditorStyles.objectField.CalcHeight(GUIContent.none, EditorGUIUtility.currentViewWidth);
            return height;
        }

        void DrawTargetObjectHeader(Rect rect) {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                EditorGUI.LabelField(rect, i18n["JLChnToZ.VRC.LazySwitch.state:title_playing"], EditorStyles.boldLabel);
                return;
            }
            if ((targetObjectsEntries.Count != 0 && !targetObjectsEntries[0].isSeparator) || !InitBackingUdonSerializedProperties())
                EditorGUI.LabelField(rect, i18n["JLChnToZ.VRC.LazySwitch.state:title"], EditorStyles.boldLabel);
            else {
                backingUdonSerializedObject.Update();
                EditorGUI.PropertyField(rect, interactTextProp, i18n.GetLocalizedContent("JLChnToZ.VRC.LazySwitch.state:title"));
                backingUdonSerializedObject.ApplyModifiedProperties();
            }
            HandleDrop(rect);
        }

        void DrawTargetObjectElement(Rect rect, int index, bool isActive, bool isFocused) {
            var entry = targetObjectsEntries[index];
            bool isNotPlaying = !EditorApplication.isPlayingOrWillChangePlaymode;
            if (entry.isSeparator || (index == 0 && isNotPlaying)) {
                var labelRect = rect;
                bool isSingleState = IsSingleState;
                var toggleStyle = isSingleState ? EditorStyles.toggle : EditorStyles.radioButton;
                var labelStyle = EditorStyles.miniBoldLabel;
                tempContent.text = string.Format(i18n["JLChnToZ.VRC.LazySwitch.state"], entry.separatorIndex);
                labelRect.height = Mathf.Max(
                    toggleStyle.CalcHeight(GUIContent.none, 16F),
                    labelStyle.CalcHeight(tempContent, EditorGUIUtility.currentViewWidth - 16F)
                );
                var toggleRect = labelRect;
                toggleRect.width = 16F;
                labelRect.xMin = toggleRect.xMax + 2;
                using (new EditorGUI.DisabledScope(masterSwitchProp.objectReferenceValue))
                using (var changed = new EditorGUI.ChangeCheckScope()) {
                    bool isDefaultState = GUI.Toggle(toggleRect, masterSwitchState == entry.separatorIndex, GUIContent.none, toggleStyle);
                    if (changed.changed) {
                        if (isSingleState) stateProp.intValue = stateProp.intValue == 1 ? 0 : 1;
                        else if (isDefaultState) stateProp.intValue = entry.separatorIndex;
                        masterSwitchState = stateProp.intValue;
                    }
                }
                rect.y += labelRect.height;
                labelRect.height = EditorGUIUtility.singleLineHeight;
                if (entry.isSeparator) {
                    using (new EditorGUI.DisabledScope(!isNotPlaying))
                    using (var changed = new EditorGUI.ChangeCheckScope()) {
                        var labelWidth = EditorGUIUtility.labelWidth;
                        EditorGUIUtility.labelWidth = labelWidth - 36;
                        entry.parameter = EditorGUI.TextField(labelRect, tempContent, entry.parameter);
                        EditorGUIUtility.labelWidth = labelWidth;
                        if (changed.changed) {
                            targetObjectsEntries[index] = entry;
                            entriesUpdated = true;
                        }
                    }
                    return;
                }
                if (InitBackingUdonSerializedProperties()) {
                    backingUdonSerializedObject.Update();
                    var labelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = labelWidth - 36;
                    EditorGUI.PropertyField(labelRect, interactTextProp, tempContent);
                    EditorGUIUtility.labelWidth = labelWidth;
                    backingUdonSerializedObject.ApplyModifiedProperties();
                }
            }
            rect.height = EditorStyles.objectField.CalcHeight(GUIContent.none, EditorGUIUtility.currentViewWidth);
            bool entryUpdated = false;
            if (isNotPlaying) {
                bool isCurrentState = masterSwitchState == entry.separatorIndex;
                bool isObjectActive = syncActiveState ? IsActive(entry.targetObject, entry.objectType, entry.parameter, isCurrentState) : entry.isActive;
                var visibleIcon = EditorGUIUtility.IconContent(
                    isObjectActive == isCurrentState ? "VisibilityOn" : "VisibilityOff"
                );
                var buttonStyle = EditorStyles.iconButton;
                var buttonRect = rect;
                buttonRect.size = buttonStyle.CalcSize(visibleIcon);
                tempContent.text = entry.targetObject == null ? "None" : GetBehaviourDisplayName(entry.targetObject, entry.objectType, entry.parameter);
                var popupStyle = EditorStyles.popup;
                var popupRect = rect;
                popupRect.size = popupStyle.CalcSize(tempContent);
                popupRect.x = rect.xMax - popupRect.width;
                rect.xMin = buttonRect.xMax + 2;
                rect.xMax = popupRect.xMin - 2;
                using (new EditorGUI.DisabledScope(entry.targetObject == null)) {
                    if (GUI.Button(buttonRect, visibleIcon, buttonStyle)) {
                        isObjectActive = !isObjectActive;
                        if (syncActiveState && entry.targetObject != null) {
                            Undo.RecordObject(entry.targetObject, "Toggle Active");
                            ToggleActive(entry.targetObject, entry.objectType);
                        }
                    }
                    using (new EditorGUI.DisabledScope(!(entry.targetObject is GameObject) && !(entry.targetObject is Component)))
                        if (GUI.Button(popupRect, tempContent, popupStyle))
                            ShowSelectComponentOrGameObjectMenu(popupRect, index);
                }
                if (entry.isActive != isObjectActive) {
                    entry.isActive = isObjectActive;
                    entryUpdated = true;
                }
            }
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                var newTargetObject = EditorGUI.ObjectField(rect, entry.targetObject, typeof(UnityObject), true);
                if (changed.changed) {
                    if (newTargetObject is IUdonEventReceiver) {
                        UdonSharpBehaviour proxy = null;
                        if (newTargetObject is UdonBehaviour ub) proxy = UdonSharpEditorUtility.GetProxyBehaviour(ub);
                        if (proxy == null) entry.targetObject = newTargetObject;
                    } else if (GetTypeCode(newTargetObject) != SwitchDrivenType.Unknown)
                        entry.targetObject = newTargetObject;
                    else if (newTargetObject is Component c)
                        entry.targetObject = c.gameObject;
                    entryUpdated = true;
                }
            }
            if (entryUpdated) {
                targetObjectsEntries[index] = entry;
                entriesUpdated = true;
            }
        }

        void DrawEmptyTargetObjectElement(Rect rect) {
            EditorGUI.LabelField(rect, i18n["JLChnToZ.VRC.LazySwitch.state:empty"]);
            HandleDrop(rect);
        }

        void DrawTargetObjectFooter(Rect rect) {
            ReorderableList.defaultBehaviours.DrawFooter(rect, targetObjectsList);
            HandleDrop(rect);
        }

        void HandleDrop(Rect rect) {
            var e = Event.current;
            switch (e.type) {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (rect.Contains(e.mousePosition)) {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        if (e.type == EventType.DragPerform) {
                            DragAndDrop.AcceptDrag();
                            if (AddEntries(DragAndDrop.objectReferences)) SaveEntries();
                            e.Use();
                        }
                    }
                    break;
            }
        }

        void OnAddTargetDropdown(Rect buttonRect, ReorderableList list) {
            var menu = new GenericMenu();
            var selectedGameObjects = Selection.gameObjects;
            menu.AddItem(new GUIContent(
                selectedGameObjects.Length > 1 || selectedGameObjects[0] != (target as Component).gameObject ?
                i18n["JLChnToZ.VRC.LazySwitch.addGameObjects"] :
                i18n["JLChnToZ.VRC.LazySwitch.addGameObject"]
            ), false, OnAddEntry);
            int separatorCount = 1;
            foreach (var entry in targetObjectsEntries)
                if (entry.isSeparator)
                    separatorCount++;
            if (separatorCount >= 32)
                menu.AddDisabledItem(new GUIContent(i18n["JLChnToZ.VRC.LazySwitch.addSeparator"]));
            else
                menu.AddItem(new GUIContent(i18n["JLChnToZ.VRC.LazySwitch.addSeparator"]), false, OnAddSeparator);
            menu.DropDown(buttonRect);
        }

        void OnAddSeparator() {
            targetObjectsEntries.Add(new Entry((byte)(LastSeparatorIndex + 1)));
            SaveEntries();
        }

        void OnAddEntry() {
            if (!AddEntries(Selection.gameObjects))
                targetObjectsEntries.Add(new Entry(null, LastSeparatorIndex));
            SaveEntries();
        }

        void ShowSelectComponentOrGameObjectMenu(Rect rect, int index) {
            var targetObject = targetObjectsEntries[index].targetObject;
            if (targetObject == null) return;
            var targetType = targetObjectsEntries[index].objectType;
            var targetParameter = targetObjectsEntries[index].parameter;
            using (ListPool<Component>.Get(out var tempComponents)) {
                if (targetObject is GameObject go)
                    go.GetComponents(tempComponents);
                else if (targetObject is Component c) {
                    c.GetComponents(tempComponents);
                    go = c.gameObject;
                } else return;
                menuComponents.Clear();
                menuComponents.Add((go, SwitchDrivenType.GameObject, null));
                foreach (var component in tempComponents) {
                    if (component is IUdonEventReceiver) {
                        MonoBehaviour obj = null;
                        if (component is UdonBehaviour ub) {
                            obj = UdonSharpEditorUtility.GetProxyBehaviour(ub);
                            if (obj == null) obj = ub;
                        }
                        if (obj == null) continue;
                        menuComponents.Add((obj, SwitchDrivenType.UdonBehaviour, null));
                        continue;
                    }
                    if (component is ParticleSystem) {
                        for (var st = SwitchDrivenType.ParticleSystemEmissionModule;
                            st <= SwitchDrivenType.ParticleSystemCustomDataModule;
                            st++)
                            menuComponents.Add((component, st, null));
                        continue;
                    }
                    if (component is Animator animator) {
                        foreach (var parameter in animator.parameters)
                            switch (parameter.type) {
                                case ACParameterType.Bool:
                                    menuComponents.Add((animator, SwitchDrivenType.AnimatorBool, parameter.name));
                                    break;
                                case ACParameterType.Trigger:
                                    menuComponents.Add((animator, SwitchDrivenType.AnimatorTrigger, parameter.name));
                                    break;
                                default:
                                    continue;
                            }
                        continue;
                    }
                    var type = GetTypeCode(component);
                    if (type == SwitchDrivenType.Unknown) continue;
                    menuComponents.Add((component, type, null));
                }
            }
            int count = menuComponents.Count;
            var options = new string[count];
            bool[] enabled = new bool[count], separator = new bool[count];
            int[] selected = null;
            for (int i = 0; i < count; i++) {
                var (entry, subType, parameter) = menuComponents[i];
                options[i] = GetBehaviourDisplayName(entry, subType, parameter);
                enabled[i] = count > 1;
                if (entry == targetObject && subType == targetType && ParameterEquals(parameter, targetParameter)) {
                    if (selected == null)
                        selected = new[] { i };
                    else
                        selected[0] = i;
                }
            }
            EditorUtility.DisplayCustomMenuWithSeparators(rect,
                options, enabled, separator, selected ?? Array.Empty<int>(),
                OnSelectComponentOrGameObject, index
            );
        }

        static bool ParameterEquals(string a, string b) =>
            string.IsNullOrEmpty(a) ? string.IsNullOrEmpty(b) :
            !string.IsNullOrEmpty(b) && a.Equals(b, StringComparison.Ordinal);

        void OnSelectComponentOrGameObject(object boxedIndex, string[] options, int selected) {
            if (selected < 0 || selected >= menuComponents.Count) return;
            int index = (int)boxedIndex;
            var entry = targetObjectsEntries[index];
            (entry.targetObject, entry.objectType, entry.parameter) = menuComponents[selected];
            targetObjectsEntries[index] = entry;
            SaveEntries();
        }

        bool AddEntries(UnityObject[] objects) {
            if (objects == null && objects.Length <= 0) return false;
            bool added = false;
            byte separatorIndex = LastSeparatorIndex;
            foreach (var o in objects) {
                if (o == null) continue;
                GameObject gameObject;
                if (o is Component c) gameObject = c.gameObject;
                else if (o is GameObject go) gameObject = go;
                else if (o is CustomRenderTexture) {
                    targetObjectsEntries.Add(new Entry(o, separatorIndex));
                    continue;
                } else continue;
                if (gameObject == (target as Component).gameObject) continue;
                targetObjectsEntries.Add(new Entry(gameObject, separatorIndex));
                added = true;
            }
            return added;
        }

        void OnRemoveEntry(ReorderableList list) {
            var entry = targetObjectsEntries[list.index];
            targetObjectsEntries.RemoveAt(list.index);
            byte separatorIndex = entry.separatorIndex;
            if (entry.isSeparator)
                for (int i = list.index; i < targetObjectsEntries.Count; i++)
                    if (targetObjectsEntries[i].isSeparator)
                        targetObjectsEntries[i] = new Entry(separatorIndex++);
            SaveEntries();
        }

        void OnReorderTargetObjects(ReorderableList list, int oldIndex, int newIndex) {
            int i = Mathf.Min(oldIndex, newIndex);
            byte separatorIndex = 0;
            if (i > 0) separatorIndex = targetObjectsEntries[i - 1].separatorIndex;
            for (; i < targetObjectsEntries.Count; i++) {
                var entry = targetObjectsEntries[i];
                if (entry.isSeparator) separatorIndex++;
                entry.separatorIndex = separatorIndex;
                targetObjectsEntries[i] = entry;
            }
            SaveEntries();
        }

        string GetTooltipTextAt(int index) => tooltipTextsProp.arraySize > index ? tooltipTextsProp.GetArrayElementAtIndex(index).stringValue : null;

        void LoadEntries() {
            serializedObject.Update();
            targetObjectsEntries.Clear();
            if (targetObjectGroupOffsetsProp.hasMultipleDifferentValues ||
                targetObjectsProp.hasMultipleDifferentValues ||
                targetObjectTypesProp.hasMultipleDifferentValues) return;
            bool sizeUpdated = false;
            if (targetObjectTypesProp.arraySize != targetObjectsProp.arraySize) {
                targetObjectTypesProp.arraySize = targetObjectsProp.arraySize;
                sizeUpdated = true;
            }
            if (targetObjectEnableMaskProp.arraySize != targetObjectsProp.arraySize) {
                targetObjectEnableMaskProp.arraySize = targetObjectsProp.arraySize;
                sizeUpdated = true;
            }
            if (targetObjectAnimatorKeysProp.arraySize != targetObjectsProp.arraySize) {
                targetObjectAnimatorKeysProp.arraySize = targetObjectsProp.arraySize;
                sizeUpdated = true;
            }
            if (sizeUpdated) serializedObject.ApplyModifiedProperties();
            byte s = 0;
            for (int i = 0; i < targetObjectsProp.arraySize; i++) {
                while (s < targetObjectGroupOffsetsProp.arraySize) {
                    if (targetObjectGroupOffsetsProp.GetArrayElementAtIndex(s).intValue > i) break;
                    targetObjectsEntries.Add(new Entry(++s, GetTooltipTextAt(s)));
                }
                targetObjectsEntries.Add(new Entry(
                    targetObjectsProp.GetArrayElementAtIndex(i).objectReferenceValue,
                    s,
                    targetObjectEnableMaskProp.GetArrayElementAtIndex(i).intValue != 0,
                    (SwitchDrivenType)targetObjectTypesProp.GetArrayElementAtIndex(i).intValue,
                    targetObjectAnimatorKeysProp.GetArrayElementAtIndex(i).stringValue
                ));
            }
            while (s < targetObjectGroupOffsetsProp.arraySize)
                targetObjectsEntries.Add(new Entry(++s, GetTooltipTextAt(s)));
        }

        void SaveEntries() {
            int i = 0, s = 0;
            tooltipTextsProp.arraySize = 0;
            foreach (var entry in targetObjectsEntries)
                if (entry.isSeparator) {
                    if (targetObjectGroupOffsetsProp.arraySize <= s + 1) targetObjectGroupOffsetsProp.arraySize = s + 1;
                    targetObjectGroupOffsetsProp.GetArrayElementAtIndex(s).intValue = i;
                    s++;
                    if (!string.IsNullOrEmpty(entry.parameter)) {
                        if (tooltipTextsProp.arraySize <= s + 1) tooltipTextsProp.arraySize = s + 1;
                        tooltipTextsProp.GetArrayElementAtIndex(s).stringValue = entry.parameter;
                    }
                } else {
                    if (targetObjectsProp.arraySize <= i + 1) targetObjectsProp.arraySize = i + 1;
                    targetObjectsProp.GetArrayElementAtIndex(i).objectReferenceValue = entry.targetObject;
                    if (targetObjectTypesProp.arraySize <= i + 1) targetObjectTypesProp.arraySize = i + 1;
                    targetObjectTypesProp.GetArrayElementAtIndex(i).intValue = (int)entry.objectType;
                    if (targetObjectEnableMaskProp.arraySize <= i + 1) targetObjectEnableMaskProp.arraySize = i + 1;
                    targetObjectEnableMaskProp.GetArrayElementAtIndex(i).intValue = entry.isActive ? -1 : 0;
                    if (targetObjectAnimatorKeysProp.arraySize <= i + 1) targetObjectAnimatorKeysProp.arraySize = i + 1;
                    targetObjectAnimatorKeysProp.GetArrayElementAtIndex(i).stringValue = entry.parameter;
                    i++;
                }
            if (targetObjectGroupOffsetsProp.arraySize > s) targetObjectGroupOffsetsProp.arraySize = s;
            if (targetObjectsProp.arraySize > i) targetObjectsProp.arraySize = i;
            if (targetObjectTypesProp.arraySize > i) targetObjectTypesProp.arraySize = i;
            serializedObject.ApplyModifiedProperties();
        }

        struct Entry {
            public bool isSeparator;
            public byte separatorIndex;
            public UnityObject targetObject;
            public SwitchDrivenType objectType;
            public string parameter;
            public bool isActive;

            public Entry(byte separatorIndex, string tooltip = null) {
                isSeparator = true;
                this.separatorIndex = separatorIndex;
                targetObject = null;
                objectType = SwitchDrivenType.Unknown;
                isActive = false;
                parameter = tooltip;
            }

            public Entry(UnityObject targetObject, byte separatorIndex, bool isActive = false, SwitchDrivenType objectType = SwitchDrivenType.Unknown, string parameter = null) {
                isSeparator = false;
                this.separatorIndex = separatorIndex;
                this.targetObject = targetObject;
                this.objectType = objectType == SwitchDrivenType.Unknown ? GetTypeCode(targetObject) : objectType;
                this.isActive = isActive;
                this.parameter = parameter;
            }
        }
    }
}