using UnityEditor;
using JLChnToZ.VRC.Foundation.Editors;
using UdonSharpEditor;
using UdonSharp;
using JLChnToZ.VRC.Foundation.I18N;
using JLChnToZ.VRC.Foundation.I18N.Editors;

namespace JLChnToZ.VRC {
    abstract class LazySwitchEditorBase : Editor {
        static PackageSelfUpdater selfUpdater;
        protected static EditorI18N i18n;
        protected static PackageSelfUpdater SelfUpdater {
            get {
                if (selfUpdater == null) {
                    selfUpdater = new PackageSelfUpdater(
                        typeof(LazySwitchEditorBase).Assembly,
                        "idv.jlchntoz.xtlcdn-listing",
                        "https://xtlcdn.github.io/vpm/index.json"
                    );
                    selfUpdater.CheckInstallationInBackground();
                }
                return selfUpdater;
            }
        }

        public override void OnInspectorGUI() {
            if (DrawUdonSharpHeader()) return;
            if (i18n == null) i18n = EditorI18N.Instance;
            I18NUtils.DrawLocaleField();
            SelfUpdater.DrawUpdateNotifier();
            DrawContent();
        }

        protected virtual bool DrawUdonSharpHeader() =>
            !(target is UdonSharpBehaviour) ||
            UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target);

        protected virtual void DrawContent() {
            var p = serializedObject.GetIterator();
            p.Next(true);
            while (p.NextVisible(false)) {
                if (p.name == "m_Script") continue;
                EditorGUILayout.PropertyField(p, p.isExpanded);
            }
        }
    }
}