using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Animations;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using UdonSharp;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditorInternal;
using UdonSharpEditor;

using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
using BaseUnityEditor = UnityEditor.Editor;
#endif

namespace JLChnToZ.VRC {
    public enum SwitchDrivenType {
        Unknown = 0,
        GameObject,
        UdonBehaviour,
        Renderer,
        Collider,
        Camera,
        Rigidbody,
        UGUISelectable,
        PositionConstraint,
        RotationConstraint,
        ScaleConstraint,
        ParentConstraint,
        AimConstraint,
        LookAtConstraint,
        VRCPickup,
        CustomRenderTexture,
        ParticleSystemEmissionModule,
        ParticleSystemShapeModule,
        ParticleSystemVelocityOverLifetimeModule,
        ParticleSystemLimitVelocityOverLifetimeModule,
        ParticleSystemInheritVelocityModule,
        ParticleSystemForceOverLifetimeModule,
        ParticleSystemColorOverLifetimeModule,
        ParticleSystemColorBySpeedModule,
        ParticleSystemSizeOverLifetimeModule,
        ParticleSystemSizeBySpeedModule,
        ParticleSystemRotationOverLifetimeModule,
        ParticleSystemRotationBySpeedModule,
        ParticleSystemExternalForcesModule,
        ParticleSystemNoiseModule,
        ParticleSystemCollisionModule,
        ParticleSystemTriggerModule,
        ParticleSystemSubEmittersModule,
        ParticleSystemTextureSheetAnimationModule,
        ParticleSystemLightsModule,
        ParticleSystemTrailModule,
        ParticleSystemCustomDataModule,
        ParticleSystem = -1, // Vaild only in editor
    }

    [AddComponentMenu("JLChnToZ/Lazy Switch")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class LazySwitch : UdonSharpBehaviour {
        [SerializeField, Range(0, 1)] int state;
        [Tooltip("Will this switch synchronized across the network.")]
        [SerializeField] bool isSynced;
        [Tooltip("Randomize the state when interacted.")]
        [SerializeField] bool isRandomized;
        [Tooltip("Link this switch to other switch, both switches will synchronize their state.")]
        [SerializeField] LazySwitch masterSwitch;
        [SerializeField, HideInInspector] LazySwitch[] slaveSwitches;
        [SerializeField] Object[] targetObjects;
        [SerializeField, HideInInspector] SwitchDrivenType[] targetObjectTypes;
        [SerializeField, HideInInspector] int[] targetObjectEnableMask;
        [SerializeField] int[] targetObjectGroupOffsets;
        [SerializeField] int stateCount;
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
            }
        }

        void OnEnable() {
            if (masterSwitch == null) {
                if (isSynced && !Networking.IsOwner(gameObject))
                    state = syncedState;
                UpdateState();
            } else
                _UpdateState();
        }

        public override void Interact() {
            if (masterSwitch != null) {
                masterSwitch.Interact();
                return;
            }
            state = isRandomized ? Random.Range(0, stateCount) : (state + 1) % stateCount;
            UpdateAndSync();
        }

        public override void OnPreSerialization() {
            if (!isSynced) return;
            syncedState = (byte)state;
        }

        public override void OnDeserialization() {
            if (!isSynced) return;
            state = syncedState;
            UpdateState();
        }

        void UpdateAndSync() {
            UpdateState();
            if (isSynced) {
                if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
                RequestSerialization();
            }
        }

        void UpdateState() {
            _UpdateState();
            foreach (var other in slaveSwitches) {
                if (other == null) continue;
                other.state = state;
                other._UpdateState();
            }
        }

        public void _UpdateState() {
            if (!enabled || !gameObject.activeInHierarchy) return;
            int stateMask = 1 << state;
            for (int i = 0, objectsLength = targetObjects.Length; i < objectsLength; i++) {
                var targetObject = targetObjects[i];
                if (targetObject == null) continue;
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

    #if UNITY_EDITOR && !COMPILER_UDONSHARP
        static SwitchDrivenType GetTypeCode(Object obj) =>
            obj is GameObject ? SwitchDrivenType.GameObject :
            obj is IUdonEventReceiver ? SwitchDrivenType.UdonBehaviour :
            obj is Renderer ? SwitchDrivenType.Renderer :
            obj is Collider ? SwitchDrivenType.Collider :
            obj is Camera ? SwitchDrivenType.Camera :
            obj is Rigidbody ? SwitchDrivenType.Rigidbody :
            obj is Selectable ? SwitchDrivenType.UGUISelectable :
            obj is PositionConstraint ? SwitchDrivenType.PositionConstraint :
            obj is RotationConstraint ? SwitchDrivenType.RotationConstraint :
            obj is ScaleConstraint ? SwitchDrivenType.ScaleConstraint :
            obj is ParentConstraint ? SwitchDrivenType.ParentConstraint :
            obj is AimConstraint ? SwitchDrivenType.AimConstraint :
            obj is LookAtConstraint ? SwitchDrivenType.LookAtConstraint :
            obj is VRC_Pickup ? SwitchDrivenType.VRCPickup :
            obj is CustomRenderTexture ? SwitchDrivenType.CustomRenderTexture :
            obj is ParticleSystem ? SwitchDrivenType.ParticleSystem :
            SwitchDrivenType.Unknown;

        static bool IsActive(Object obj, SwitchDrivenType subType) => obj != null && (
            obj is GameObject gameObject ? gameObject.activeSelf :
            obj is IConstraint constraint ? constraint.constraintActive :
            obj is Selectable selectable ? selectable.interactable :
            obj is VRC_Pickup pickup ? pickup.pickupable :
            obj is Rigidbody rigidbody ? !rigidbody.isKinematic :
            obj is Behaviour behaviour ? behaviour.enabled :
            obj is CustomRenderTexture crt ? crt.updateMode == CustomRenderTextureUpdateMode.Realtime :
            obj is ParticleSystem ps ? subType switch {
                SwitchDrivenType.ParticleSystemEmissionModule => ps.emission.enabled,
                SwitchDrivenType.ParticleSystemShapeModule => ps.shape.enabled,
                SwitchDrivenType.ParticleSystemVelocityOverLifetimeModule => ps.velocityOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemLimitVelocityOverLifetimeModule => ps.limitVelocityOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemInheritVelocityModule => ps.inheritVelocity.enabled,
                SwitchDrivenType.ParticleSystemForceOverLifetimeModule => ps.forceOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemColorOverLifetimeModule => ps.colorOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemColorBySpeedModule => ps.colorBySpeed.enabled,
                SwitchDrivenType.ParticleSystemSizeOverLifetimeModule => ps.sizeOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemSizeBySpeedModule => ps.sizeBySpeed.enabled,
                SwitchDrivenType.ParticleSystemRotationOverLifetimeModule => ps.rotationOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemRotationBySpeedModule => ps.rotationBySpeed.enabled,
                SwitchDrivenType.ParticleSystemExternalForcesModule => ps.externalForces.enabled,
                SwitchDrivenType.ParticleSystemNoiseModule => ps.noise.enabled,
                SwitchDrivenType.ParticleSystemCollisionModule => ps.collision.enabled,
                SwitchDrivenType.ParticleSystemTriggerModule => ps.trigger.enabled,
                SwitchDrivenType.ParticleSystemSubEmittersModule => ps.subEmitters.enabled,
                SwitchDrivenType.ParticleSystemTextureSheetAnimationModule => ps.textureSheetAnimation.enabled,
                SwitchDrivenType.ParticleSystemLightsModule => ps.lights.enabled,
                SwitchDrivenType.ParticleSystemTrailModule => ps.trails.enabled,
                SwitchDrivenType.ParticleSystemCustomDataModule => ps.customData.enabled,
                _ => true
            } :
            true
        );

        static void ToggleActive(Object obj, SwitchDrivenType subType) {
            if (obj == null) return;
            if (obj is GameObject gameObject) gameObject.SetActive(!gameObject.activeSelf);
            else if (obj is IConstraint constraint) constraint.constraintActive = !constraint.constraintActive;
            else if (obj is Selectable selectable) selectable.interactable = !selectable.interactable;
            else if (obj is VRC_Pickup pickup) pickup.pickupable = !pickup.pickupable;
            else if (obj is Rigidbody rigidbody) rigidbody.isKinematic = !rigidbody.isKinematic;
            else if (obj is Behaviour behaviour) behaviour.enabled = !behaviour.enabled;
            else if (obj is CustomRenderTexture crt)
                crt.updateMode = crt.updateMode == CustomRenderTextureUpdateMode.Realtime ?
                    CustomRenderTextureUpdateMode.OnDemand : CustomRenderTextureUpdateMode.Realtime;
            else if (obj is ParticleSystem ps) switch (subType) {
                case SwitchDrivenType.ParticleSystemEmissionModule: { var m = ps.emission; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemShapeModule: { var m = ps.shape; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemVelocityOverLifetimeModule: { var m = ps.velocityOverLifetime; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemLimitVelocityOverLifetimeModule: { var m = ps.limitVelocityOverLifetime; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemInheritVelocityModule: { var m = ps.inheritVelocity; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemForceOverLifetimeModule: { var m = ps.forceOverLifetime; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemColorOverLifetimeModule: { var m = ps.colorOverLifetime; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemColorBySpeedModule: { var m = ps.colorBySpeed; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemSizeOverLifetimeModule: { var m = ps.sizeOverLifetime; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemSizeBySpeedModule: { var m = ps.sizeBySpeed; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemRotationOverLifetimeModule: { var m = ps.rotationOverLifetime; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemRotationBySpeedModule: { var m = ps.rotationBySpeed; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemExternalForcesModule: { var m = ps.externalForces; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemNoiseModule: { var m = ps.noise; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemCollisionModule: { var m = ps.collision; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemTriggerModule: { var m = ps.trigger; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemSubEmittersModule: { var m = ps.subEmitters; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemTextureSheetAnimationModule: { var m = ps.textureSheetAnimation; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemLightsModule: { var m = ps.lights; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemTrailModule: { var m = ps.trails; m.enabled = !m.enabled; } break;
                case SwitchDrivenType.ParticleSystemCustomDataModule: { var m = ps.customData; m.enabled = !m.enabled; } break;
            }
        }

        [CustomEditor(typeof(LazySwitch))]
        [CanEditMultipleObjects]
        sealed class Editor : BaseUnityEditor {
            const string masterSwitchMessage = "This switch will be synchronized with the master switch.";
            const string multipleValuesMessage = "Unable to display values because they are different across multiple objects.\n" +
                "Attempt to add, reorder or change the values will cause all selected switches to have the same value.";
            static readonly List<Component> tempComponents = new List<Component>();
            static readonly List<(Object component, SwitchDrivenType subType)> menuComponents = new List<(Object, SwitchDrivenType)>();
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

            static string GetBehaviourDisplayName(Object obj, SwitchDrivenType subType = SwitchDrivenType.Unknown) {
                if (obj == null) return "<None>";
                if (obj is UdonBehaviour ub && !UdonSharpEditorUtility.IsUdonSharpBehaviour(ub)) {
                    obj = ub.programSource;
                    if (obj != null) return obj.name;
                }
                if (obj is ParticleSystem)
                    return ObjectNames.NicifyVariableName(subType.ToString());
                return ObjectNames.NicifyVariableName(obj.GetType().Name);
            }

            void OnEnable() {
                if (tempContent == null) tempContent = new GUIContent();
                stateProp = serializedObject.FindProperty(nameof(state));
                isSyncedProp = serializedObject.FindProperty(nameof(isSynced));
                isRandomizedProp = serializedObject.FindProperty(nameof(isRandomized));
                masterSwitchProp = serializedObject.FindProperty(nameof(masterSwitch));
                targetObjectsProp = serializedObject.FindProperty(nameof(targetObjects));
                targetObjectTypesProp = serializedObject.FindProperty(nameof(targetObjectTypes));
                targetObjectGroupOffsetsProp = serializedObject.FindProperty(nameof(targetObjectGroupOffsets));
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
                    var newTargetObject = EditorGUI.ObjectField(rect, entry.targetObject, typeof(Object), true);
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

            bool AddEntries(Object[] objects) {
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
                        targetObjectsProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject,
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
                public Object targetObject;
                public SwitchDrivenType objectType;

                public Entry(byte separatorIndex) {
                    isSeparator = true;
                    this.separatorIndex = separatorIndex;
                    targetObject = null;
                    objectType = SwitchDrivenType.Unknown;
                }

                public Entry(Object targetObject, byte separatorIndex, SwitchDrivenType objectType = SwitchDrivenType.Unknown) {
                    isSeparator = false;
                    this.separatorIndex = separatorIndex;
                    this.targetObject = targetObject;
                    this.objectType = objectType == SwitchDrivenType.Unknown ? GetTypeCode(targetObject) : objectType;
                }
            }
        }

        sealed class Preprocessor : IProcessSceneWithReport {
            public int callbackOrder => 0;

            public void OnProcessScene(Scene scene, BuildReport report) {
                var switches = new List<LazySwitch>();
                foreach (var rootGameObject in scene.GetRootGameObjects())
                    switches.AddRange(rootGameObject.GetComponentsInChildren<LazySwitch>(true));
                if (switches.Count == 0) return;
                var switchGroups = new Dictionary<LazySwitch, List<LazySwitch>>();
                var targetObjectEnableMask = new Dictionary<Object, (SwitchDrivenType objectType, int onFlags, int offFlags)>();
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
                        Object destObj;
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

            static bool IsAvailableOnRuntime(Object gameObjectOrComponent) {
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
    #endif
    }
}