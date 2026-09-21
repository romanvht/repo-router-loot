#if ROUTER_LOOT_READY
using System;
using System.Diagnostics;
using System.IO;
using Nomnom.UnityProjectPatcher;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RouterLoot.Editor
{
    public static class ModBuild
    {
        [MenuItem("Router Loot/Build Mod", false, 0)]
        public static void Build()
        {
            if (!AssetBuild.CanBuild())
            {
                throw new InvalidOperationException("Wait for compilation and exit Play Mode before building.");
            }

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string game = Path.GetFullPath(PatcherUtility.GetUserSettings().GameFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar);

            try
            {
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayProgressBar("Router Loot", "Building plugin", .1f);
                RunScript(root, "build-plugin.ps1", $"-UseLocalDependencies -GameDir \"{game}\"");

                EditorUtility.DisplayProgressBar("Router Loot", "Building assets", .4f);
                AssetBuild.Build();

                EditorUtility.DisplayProgressBar("Router Loot", "Creating package", .9f);
                RunScript(root, "package-zip.ps1");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string output = Path.Combine(root, "dist");
            Debug.Log($"Router Loot build complete: {output}");
            if (!Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(output);
            }
        }

        [MenuItem("Router Loot/Build Mod", true)]
        private static bool CanBuild() => AssetBuild.CanBuild();

        private static void RunScript(string root, string script, string arguments = "")
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe");
            var start = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"scripts/{script}\" {arguments}",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string log = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{script} failed ({process.ExitCode}).\n{log}");
            }
            Debug.Log(log.TrimEnd());
        }
    }
}
#endif
