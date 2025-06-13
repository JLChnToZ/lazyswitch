using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace JLChnToZ.VRC {
    class LazySwitchDefine {
        const string symbolSeparator = ";";
        const string symbol = "LAZY_SWITCH";

        [InitializeOnLoadMethod]
        static void InjectDefineSymbol() {
            InjectDefineSymbol(NamedBuildTarget.Standalone);
            InjectDefineSymbol(NamedBuildTarget.Android);
            InjectDefineSymbol(NamedBuildTarget.iOS);
        }

        static void InjectDefineSymbol(NamedBuildTarget target) {
            try {
                var defines = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbols(target).Split(symbolSeparator, StringSplitOptions.RemoveEmptyEntries));
                if (defines.Add(symbol)) PlayerSettings.SetScriptingDefineSymbols(target, string.Join(symbolSeparator, defines));
            } catch { }
        }
    }
}