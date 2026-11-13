using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace CriticalMass.Editor.Effects
{
    /// <summary>
    /// Creates Assets/_Game/Audio/Resources/CMAudioMixer.mixer once (ProjectSetup.Apply calls EnsureMixer): groups
    /// Master > Music and Master > Sfx, with each group's volume exposed as MasterVol, MusicVol and SfxVol for the settings
    /// page (AudioMixer.SetFloat in dB). Unity has no public API to author a mixer, so this uses the editor's internal
    /// AudioMixerController by reflection (Unity 6000.3); it is idempotent and leaves an existing complete mixer alone.
    /// </summary>
    public static class CombatAudioSetup
    {
        /// <summary>Names of a mixer asset's exposed parameters (internal AudioMixerController.exposedParameters).</summary>
        public static string[] ExposedNames(Type ctl, UnityEngine.Object mixer)
        {
            var arr = (Array)ctl.GetProperty("exposedParameters", Any).GetValue(mixer);
            Type t = arr.GetType().GetElementType();
            FieldInfo n = t.GetField("name", Any);
            return arr.Cast<object>().Select(e => (string)n.GetValue(e)).ToArray();
        }

        public static string[] ExposedNames(UnityEngine.Object mixer) =>
            ExposedNames(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController", true), mixer);

        public const string MixerPath = "Assets/_Game/Audio/Resources/CMAudioMixer.mixer";
        public static readonly string[] Exposed = { "MasterVol", "MusicVol", "SfxVol" };

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static void EnsureMixer()
        {
            Assembly ed = typeof(UnityEditor.Editor).Assembly;
            Type ctl = ed.GetType("UnityEditor.Audio.AudioMixerController", true);
            var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (existing != null && Exposed.All(p => ExposedNames(ctl, existing).Contains(p))) return;
            if (existing != null) AssetDatabase.DeleteAsset(MixerPath);

            Type grp = ed.GetType("UnityEditor.Audio.AudioMixerGroupController", true);
            Type pathT = ed.GetType("UnityEditor.Audio.AudioGroupParameterPath", true);
            Type exposedT = ed.GetType("UnityEditor.Audio.ExposedAudioParameter", true);

            object mixer = ctl.GetMethod("CreateMixerControllerAtPath", Any).Invoke(null, new object[] { MixerPath });
            object master = ctl.GetProperty("masterGroup", Any).GetValue(mixer);
            MethodInfo createGroup = ctl.GetMethod("CreateNewGroup", Any);
            MethodInfo addChild = ctl.GetMethod("AddChildToParent", Any);
            object music = createGroup.Invoke(mixer, new object[] { "Music", false });
            addChild.Invoke(mixer, new[] { music, master });
            object sfx = createGroup.Invoke(mixer, new object[] { "Sfx", false });
            addChild.Invoke(mixer, new[] { sfx, master });
            // (AddGroupToCurrentView is skipped: a mixer made in batch mode has no views and it indexes an empty array.)

            MethodInfo volGuid = grp.GetMethod("GetGUIDForVolume", Any);
            MethodInfo addExposed = ctl.GetMethods(Any).First(m => m.Name == "AddExposedParameter" && m.GetParameters().Length == 1);
            PropertyInfo exposedProp = ctl.GetProperty("exposedParameters", Any);
            object[] groups = { master, music, sfx };
            for (int i = 0; i < groups.Length; i++)
            {
                object guid = volGuid.Invoke(groups[i], null);
                object path = Activator.CreateInstance(pathT, groups[i], guid);
                addExposed.Invoke(mixer, new[] { path });
                // Rename the new parameter (Unity names it "MyExposedParam..."): find it by GUID.
                var arr = (Array)exposedProp.GetValue(mixer);
                FieldInfo gField = exposedT.GetField("guid", Any), nField = exposedT.GetField("name", Any);
                for (int k = 0; k < arr.Length; k++)
                {
                    object e = arr.GetValue(k);
                    if (!gField.GetValue(e).Equals(guid)) continue;
                    nField.SetValue(e, Exposed[i]);
                    arr.SetValue(e, k);
                }
                exposedProp.SetValue(mixer, arr);
            }
            ctl.GetMethod("OnChangedExposedParameter", Any)?.Invoke(mixer, null);
            EditorUtility.SetDirty((UnityEngine.Object)mixer);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate);
            var check = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            string missing = string.Join(",", Exposed.Where(p => check == null || !ExposedNames(ctl, check).Contains(p)));
            if (missing.Length > 0) throw new InvalidOperationException("CMAudioMixer: exposed parameters missing after setup: " + missing);
            Debug.Log("[CriticalMass.Setup] CMAudioMixer groups " + string.Join("/", check.FindMatchingGroups("").Select(g => g.name)) + "; created with Master/Music/Sfx and " + string.Join(", ", Exposed));
        }
    }
}
