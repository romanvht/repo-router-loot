using System;
using System.IO;
using Nomnom.UnityProjectPatcher;
using Nomnom.UnityProjectPatcher.Editor;
using Nomnom.UnityProjectPatcher.Editor.Steps;
using UnityEditor;
using UnityEngine;
using PatcherUtility = Nomnom.UnityProjectPatcher.PatcherUtility;

namespace RouterLoot.Editor
{
    public static class ProjectSetup
    {
        private const string ProgressPath = "Library/RouterLootSetup.txt";

        public static void Finish()
        {
            var group = BuildTargetGroup.Standalone;
            string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            if (!symbols.Contains("ROUTER_LOOT_READY"))
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, symbols + ";ROUTER_LOOT_READY");
            }

            EditorSettings.serializationMode = SerializationMode.ForceText;
            AssetDatabase.SaveAssets();
        }

        public static async void Run()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                int gameArg = Array.IndexOf(args, "-gameDir");
                if (gameArg < 0 || gameArg + 1 >= args.Length)
                {
                    throw new ArgumentException("Pass -gameDir with the R.E.P.O. installation path.");
                }

                var user = new SerializedObject(PatcherUtility.GetUserSettings());
                user.FindProperty("_gameFolderPath").stringValue = Path.GetFullPath(args[gameArg + 1]);
                user.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();

                var pipeline = PatcherSteps.GetPipeline();
                pipeline.Steps.RemoveAll(step => step is GenerateGitIgnoreStep || step is GenerateReadmeStep);
                int index = File.Exists(ProgressPath) ? int.Parse(File.ReadAllText(ProgressPath)) : 0;
                for (; index < pipeline.Steps.Count; index++)
                {
                    var step = pipeline.Steps[index];
                    Debug.Log($"RouterLoot setup {index + 1}/{pipeline.Steps.Count}: {step.GetType().Name}");
                    var result = await step.Run();
                    if (result == StepResult.Failure)
                    {
                        throw new InvalidOperationException($"{step.GetType().Name} failed.");
                    }

                    File.WriteAllText(ProgressPath, (index + 1).ToString());
                    if (result == StepResult.RestartEditor || result == StepResult.Recompile)
                    {
                        EditorApplication.Exit(0);
                        return;
                    }
                }

                foreach (var step in pipeline.Steps)
                {
                    step.OnComplete(false);
                }

                File.WriteAllText("Assets/REPO/.router-loot-ready", "Ready");
                Debug.Log("RouterLoot Unity project is ready.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
    }
}
