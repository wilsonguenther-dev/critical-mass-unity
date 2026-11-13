using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace CriticalMass.Editor.Settings
{
    /// <summary>
    /// Creates Assets/_Game/Resources/Audio/CMMixer.mixer (groups Master, Music, Effects; exposed volume parameters
    /// MasterVolume, MusicVolume, EffectsVolume) for the settings page's Audio tab. Unity has no public API to author a
    /// mixer, so this calls the editor's own AudioMixerController by reflection; it runs once, by hand
    /// (-executeMethod CriticalMass.Editor.Settings.AudioMixerBuilder.Build), and the asset Unity writes is committed.
    /// Exit code 0 on success, 1 on any failure (the reason is logged).
    /// </summary>
    public static class AudioMixerBuilder
    {
        public const string Path = "Assets/_Game/Resources/Audio/CMMixer.mixer";

        public static void Build()
        {
            int code = 0;
            try
            {
                BuildMixer();
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioMixerBuilder] " + e);
                code = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void BuildMixer()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(Path) != null) AssetDatabase.DeleteAsset(Path);
            Assembly ed = typeof(UnityEditor.Editor).Assembly;
            Type ctl = ed.GetType("UnityEditor.Audio.AudioMixerController", true);
            Type grpT = ed.GetType("UnityEditor.Audio.AudioMixerGroupController", true);
            Type pathT = ed.GetType("UnityEditor.Audio.AudioGroupParameterPath", true);
            Type baseT = ed.GetType("UnityEditor.Audio.AudioParameterPath", true);
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            object mixer = ctl.GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { Path });
            object master = ctl.GetProperty("masterGroup", F).GetValue(mixer);
            MethodInfo create = ctl.GetMethods(F).First(m => m.Name == "CreateNewGroup" && m.GetParameters().Length == 2);
            MethodInfo addChild = ctl.GetMethods(F).First(m => m.Name == "AddChildToParent" && m.GetParameters().Length == 2);
            object music = create.Invoke(mixer, new object[] { "Music", false });
            addChild.Invoke(mixer, new[] { music, master });
            object effects = create.Invoke(mixer, new object[] { "Effects", false });
            addChild.Invoke(mixer, new[] { effects, master });

            MethodInfo guidForVolume = grpT.GetMethod("GetGUIDForVolume", F);
            MethodInfo expose = ctl.GetMethods(F).First(m => m.Name == "AddExposedParameter" && m.GetParameters().Length == 1 &&
                                                             m.GetParameters()[0].ParameterType == baseT);
            ConstructorInfo pathCtor = pathT.GetConstructors(F).First(c => c.GetParameters().Length == 2);
            object[] groups = { master, music, effects };
            string[] names = { "MasterVolume", "MusicVolume", "EffectsVolume" };
            var guids = new GUID[3];
            for (int i = 0; i < 3; i++)
            {
                guids[i] = (GUID)guidForVolume.Invoke(groups[i], null);
                expose.Invoke(mixer, new[] { pathCtor.Invoke(new[] { groups[i], (object)guids[i] }) });
            }

            // Rename the exposed parameters ("MyExposedParam", "... 1", ...) to the names GameSettings sets.
            PropertyInfo exposedProp = ctl.GetProperty("exposedParameters", F);
            Array exposed = (Array)exposedProp.GetValue(mixer);
            Type epT = exposed.GetType().GetElementType();
            FieldInfo gField = epT.GetField("guid", F), nField = epT.GetField("name", F);
            for (int k = 0; k < exposed.Length; k++)
            {
                object ep = exposed.GetValue(k);
                GUID g = (GUID)gField.GetValue(ep);
                int i = Array.IndexOf(guids, g);
                if (i < 0) continue;
                nField.SetValue(ep, names[i]);
                exposed.SetValue(ep, k);
            }
            exposedProp.SetValue(mixer, exposed);
            ctl.GetMethod("OnSubAssetChanged", F)?.Invoke(mixer, null);
            EditorUtility.SetDirty((UnityEngine.Object)mixer);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);

            var check = AssetDatabase.LoadAssetAtPath<AudioMixer>(Path);
            if (check == null) throw new InvalidOperationException("mixer not saved at " + Path);
            for (int i = 0; i < 3; i++)
                if (!check.GetFloat(names[i], out float db)) throw new InvalidOperationException("exposed parameter missing: " + names[i]);
            if (check.FindMatchingGroups("Music").Length == 0 || check.FindMatchingGroups("Effects").Length == 0)
                throw new InvalidOperationException("Music or Effects group missing");
            Debug.Log("[AudioMixerBuilder] wrote " + Path + " with " + string.Join(", ", names));
        }
    }
}
