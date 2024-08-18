using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using UdonSharp;
using UdonSharpEditor;
#if CYAN_TRIGGER_IMPORTED
using Cyan.CT.Editor;
#endif
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.VRC {
    using static LazySwitchEditorUtils;

    [CustomEditor(typeof(LazySwitch))]
    [CanEditMultipleObjects]
    sealed class LazySwitchEditor : Editor {
        const string masterSwitchMessage = "This switch will be synchronized with the master switch.";
        const string multipleValuesMessage = "Unable to display values because they are different across multiple objects.\n" +
            "Attempt to add, reorder or change the values will cause all selected switches to have the same value.";
        static readonly List<Component> tempComponents = new List<Component>();
        static readonly List<(UnityObject component, SwitchDrivenType subType)> menuComponents = new List<(UnityObject, SwitchDrivenType)>();
        static GUIContent tempContent;
        SerializedProperty stateProp, isSyncedProp, isRandomizedProp, masterSwitchProp, targetObjectsProp, targetObjectTypesProp, targetObjectGroupOffsetsProp;
        ReorderableList targetObjectsList;
        readonly List<Entry> targetObjectsEntries = new List<Entry>();
        int masterSwitchState;

        byte LastSeparatorIndex {
            get {
                for (int i = targetObjectsEntries.Count - 1; i >= 0; i--) {
                    var entry = targetObjectsEntries[i];
                    if (entry.isSeparator) return entry.separatorIndex;
                }
                return 0;
            }
        }

        static string GetBehaviourDisplayName(UnityObject obj, SwitchDrivenType subType = SwitchDrivenType.Unknown) {
            if (obj == null) return "<None>";
            if (obj is GameObject) return "Game Object";
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
            return ObjectNames.GetInspectorTitle(obj);
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
        }

        public override void OnInspectorGUI() {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)) return;
            serializedObject.Update();
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
                    EditorGUILayout.Toggle(isSyncedProp.displayName, masterSwitch.isSynced);
                    EditorGUILayout.Toggle(isRandomizedProp.displayName, masterSwitch.isRandomized);
                    EditorGUILayout.IntSlider(stateProp.displayName, masterSwitchState, 0, masterSwitch.stateCount - 1);
                }
                EditorGUILayout.HelpBox(masterSwitchMessage, MessageType.Info);
            } else {
                EditorGUILayout.PropertyField(isSyncedProp);
                EditorGUILayout.PropertyField(isRandomizedProp);
                var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight, GUI.skin.horizontalSlider);
                using (var changed = new EditorGUI.ChangeCheckScope())
                using (var prop = new EditorGUI.PropertyScope(rect, null, stateProp)) {
                    rect = EditorGUI.PrefixLabel(rect, prop.content);
                    int value = EditorGUI.IntSlider(rect, stateProp.intValue, 0, Mathf.Max(1, targetObjectGroupOffsetsProp.arraySize));
                    masterSwitchState = value;
                    if (changed.changed) stateProp.intValue = value;
                }
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                targetObjectsList.DoLayoutList();
            if (targetObjectGroupOffsetsProp.hasMultipleDifferentValues ||
                targetObjectsProp.hasMultipleDifferentValues ||
                targetObjectTypesProp.hasMultipleDifferentValues)
                EditorGUILayout.HelpBox(multipleValuesMessage, MessageType.Warning);
            serializedObject.ApplyModifiedProperties();
        }

        float CalculateTargetObjectElementHeight(int index) {
            if (index < 0 || index >= targetObjectsEntries.Count) return 0;
            float height = 0;
            var entry = targetObjectsEntries[index];
            if (entry.isSeparator || (index == 0 && !Application.isPlaying)) {
                tempContent.text = $"State {entry.separatorIndex}";
                height += EditorStyles.miniBoldLabel.CalcHeight(tempContent, EditorGUIUtility.currentViewWidth);
            }
            if (!entry.isSeparator)
                height += EditorStyles.objectField.CalcHeight(GUIContent.none, EditorGUIUtility.currentViewWidth);
            return height;
        }

        void DrawTargetObjectHeader(Rect rect) {
            EditorGUI.LabelField(rect, targetObjectsProp.displayName, EditorStyles.boldLabel);
            HandleDrop(rect);
        }

        void DrawTargetObjectElement(Rect rect, int index, bool isActive, bool isFocused) {
            var entry = targetObjectsEntries[index];
            bool isNotPlaying = !Application.isPlaying;
            if (entry.isSeparator || (index == 0 && isNotPlaying)) {
                var labelRect = rect;
                var labelStyle = EditorStyles.miniBoldLabel;
                tempContent.text = $"State {entry.separatorIndex}";
                labelRect.height = labelStyle.CalcHeight(tempContent, EditorGUIUtility.currentViewWidth);
                EditorGUI.LabelField(labelRect, tempContent, labelStyle);
                if (entry.isSeparator) return;
                rect.y += labelRect.height;
            }
            rect.height = EditorStyles.objectField.CalcHeight(GUIContent.none, EditorGUIUtility.currentViewWidth);
            if (isNotPlaying) {
                var visibleIcon = EditorGUIUtility.IconContent(
                    IsActive(entry.targetObject, entry.objectType) == (masterSwitchState == entry.separatorIndex) ?
                    "VisibilityOn" : "VisibilityOff"
                );
                var buttonStyle = EditorStyles.iconButton;
                var buttonRect = rect;
                buttonRect.size = buttonStyle.CalcSize(visibleIcon);
                tempContent.text = entry.targetObject == null ? "None" : GetBehaviourDisplayName(entry.targetObject, entry.objectType);
                var popupStyle = EditorStyles.popup;
                var popupRect = rect;
                popupRect.size = popupStyle.CalcSize(tempContent);
                popupRect.x = rect.xMax - popupRect.width;
                rect.xMin = buttonRect.xMax + 2;
                rect.xMax = popupRect.xMin - 2;
                using (new EditorGUI.DisabledScope(entry.targetObject == null)) {
                    if (GUI.Button(buttonRect, visibleIcon, buttonStyle) && entry.targetObject != null) {
                        Undo.RecordObject(entry.targetObject, "Toggle Active");
                        ToggleActive(entry.targetObject, entry.objectType);
                    }
                    using (new EditorGUI.DisabledScope(!(entry.targetObject is GameObject) && !(entry.targetObject is Component)))
                        if (GUI.Button(popupRect, tempContent, popupStyle))
                            ShowSelectComponentOrGameObjectMenu(popupRect, index);
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
                    targetObjectsEntries[index] = entry;
                    SaveEntries();
                }
            }
        }

        void DrawEmptyTargetObjectElement(Rect rect) {
            EditorGUI.LabelField(rect, "Click `+` button or drop objects here.");
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
                "Add Selected Game Objects" :
                "Add Game Object"
            ), false, OnAddEntry);
            int separatorCount = 1;
            foreach (var entry in targetObjectsEntries)
                if (entry.isSeparator)
                    separatorCount++;
            if (separatorCount >= 32)
                menu.AddDisabledItem(new GUIContent("Add State Separator"));
            else
                menu.AddItem(new GUIContent("Add State Separator"), false, OnAddSeparator);
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
            tempComponents.Clear();
            if (targetObject is GameObject go)
                go.GetComponents(tempComponents);
            else if (targetObject is Component c) {
                c.GetComponents(tempComponents);
                go = c.gameObject;
            } else return;
            menuComponents.Clear();
            menuComponents.Add((go, SwitchDrivenType.GameObject));
            foreach (var component in tempComponents) {
                if (component is IUdonEventReceiver) {
                    MonoBehaviour obj = null;
                    if (component is UdonBehaviour ub) {
                        obj = UdonSharpEditorUtility.GetProxyBehaviour(ub);
                        if (obj == null) obj = ub;
                    }
                    if (obj == null) continue;
                    menuComponents.Add((obj, SwitchDrivenType.UdonBehaviour));
                    continue;
                }
                if (component is ParticleSystem) {
                    for (var st = SwitchDrivenType.ParticleSystemEmissionModule;
                        st <= SwitchDrivenType.ParticleSystemCustomDataModule;
                        st++)
                        menuComponents.Add((component, st));
                    continue;
                }
                var type = GetTypeCode(component);
                if (type == SwitchDrivenType.Unknown) continue;
                menuComponents.Add((component, type));
            }
            int count = menuComponents.Count;
            var options = new string[count];
            bool[] enabled = new bool[count], separator = new bool[count];
            int[] selected = null;
            for (int i = 0; i < count; i++) {
                var (entry, subType) = menuComponents[i];
                options[i] = GetBehaviourDisplayName(entry, subType);
                enabled[i] = count > 1;
                if (entry == targetObject && subType == targetType) {
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

        void OnSelectComponentOrGameObject(object boxedIndex, string[] options, int selected) {
            if (selected < 0 || selected >= menuComponents.Count) return;
            int index = (int)boxedIndex;
            var entry = targetObjectsEntries[index];
            (entry.targetObject, entry.objectType) = menuComponents[selected];
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

        void LoadEntries() {
            serializedObject.Update();
            targetObjectsEntries.Clear();
            if (targetObjectGroupOffsetsProp.hasMultipleDifferentValues ||
                targetObjectsProp.hasMultipleDifferentValues ||
                targetObjectTypesProp.hasMultipleDifferentValues) return;
            if (targetObjectTypesProp.arraySize != targetObjectsProp.arraySize) {
                targetObjectTypesProp.arraySize = targetObjectsProp.arraySize;
                serializedObject.ApplyModifiedProperties();
            }
            byte s = 0;
            for (int i = 0; i < targetObjectsProp.arraySize; i++) {
                while (s < targetObjectGroupOffsetsProp.arraySize) {
                    if (targetObjectGroupOffsetsProp.GetArrayElementAtIndex(s).intValue > i) break;
                    targetObjectsEntries.Add(new Entry(++s));
                }
                targetObjectsEntries.Add(new Entry(
                    targetObjectsProp.GetArrayElementAtIndex(i).objectReferenceValue,
                    s,
                    (SwitchDrivenType)targetObjectTypesProp.GetArrayElementAtIndex(i).intValue
                ));
            }
            while (s < targetObjectGroupOffsetsProp.arraySize)
                targetObjectsEntries.Add(new Entry(++s));
        }

        void SaveEntries() {
            int i = 0, s = 0;
            foreach (var entry in targetObjectsEntries)
                if (entry.isSeparator) {
                    if (targetObjectGroupOffsetsProp.arraySize <= s + 1) targetObjectGroupOffsetsProp.arraySize = s + 1;
                    targetObjectGroupOffsetsProp.GetArrayElementAtIndex(s).intValue = i;
                    s++;
                } else {
                    if (targetObjectsProp.arraySize <= i + 1) targetObjectsProp.arraySize = i + 1;
                    targetObjectsProp.GetArrayElementAtIndex(i).objectReferenceValue = entry.targetObject;
                    if (targetObjectTypesProp.arraySize <= i + 1) targetObjectTypesProp.arraySize = i + 1;
                    targetObjectTypesProp.GetArrayElementAtIndex(i).intValue = (int)entry.objectType;
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

            public Entry(byte separatorIndex) {
                isSeparator = true;
                this.separatorIndex = separatorIndex;
                targetObject = null;
                objectType = SwitchDrivenType.Unknown;
            }

            public Entry(UnityObject targetObject, byte separatorIndex, SwitchDrivenType objectType = SwitchDrivenType.Unknown) {
                isSeparator = false;
                this.separatorIndex = separatorIndex;
                this.targetObject = targetObject;
                this.objectType = objectType == SwitchDrivenType.Unknown ? GetTypeCode(targetObject) : objectType;
            }
        }
    }
}