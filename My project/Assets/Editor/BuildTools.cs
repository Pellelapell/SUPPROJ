using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Menu Tools > Build : builds Windows et macOS dans le dossier Builds/ (ignoré par git).
// Le build macOS depuis Windows demande le module "Mac Build Support (Mono)" dans Unity Hub.
public static class BuildTools
{
    const string GameName = "SUPPROJ";
    const string BuildDir = "Builds";

    [MenuItem("Tools/Build/Windows")]
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, BuildDir + "/Windows/" + GameName + ".exe");

    [MenuItem("Tools/Build/macOS")]
    public static void BuildMac() => Build(BuildTarget.StandaloneOSX, BuildDir + "/macOS/" + GameName + ".app");

    [MenuItem("Tools/Build/Windows + macOS")]
    public static void BuildAll()
    {
        if (Build(BuildTarget.StandaloneWindows64, BuildDir + "/Windows/" + GameName + ".exe"))
            Build(BuildTarget.StandaloneOSX, BuildDir + "/macOS/" + GameName + ".app");
    }

    static bool Build(BuildTarget target, string path)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
        {
            string module = target == BuildTarget.StandaloneOSX ? "Mac Build Support (Mono)" : "Windows Build Support";
            EditorUtility.DisplayDialog("Module manquant",
                $"Le module \"{module}\" n'est pas installé.\n\n" +
                "Unity Hub > Installs > 2022.3.62f3 > ⚙ > Add modules, coche-le puis relance Unity.", "OK");
            return false;
        }

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("Aucune scène", "Lance d'abord Tools > Multijoueur > Créer le menu et la scène de jeu.", "OK");
            return false;
        }

        // Mono : seul backend capable de produire un build macOS depuis Windows (IL2CPP exige un Mac)
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.productName = GameName;
        PlayerSettings.runInBackground = true;

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
        });

        bool ok = report.summary.result == BuildResult.Succeeded;

        // Module installé pendant qu'Unity était ouvert : Unity annonce "Succeeded" sans créer le .app
        if (ok && target == BuildTarget.StandaloneOSX && !Directory.Exists(path))
        {
            EditorUtility.DisplayDialog("Build macOS incomplet",
                "Unity n'a pas créé le .app : le module Mac a sans doute été installé pendant qu'Unity était ouvert.\n\n" +
                "Ferme Unity, rouvre le projet, puis relance Tools > Build > macOS.", "OK");
            return false;
        }

        string result = path;
        if (ok && target == BuildTarget.StandaloneOSX)
        {
            // Unity modifie Info.plist et ajoute nos DLL après coup : la signature d'origine est cassée,
            // et macOS affiche alors "le fichier est endommagé" sans proposer de l'ouvrir. On re-signe.
            if (!SignMacApp(path)) return false;

            // Zip prêt à envoyer, avec les droits d'exécution macOS (un zip Windows classique les perd)
            result = Path.Combine(BuildDir, GameName + "-Mac.zip");
            MacZip.Create(path, result, new Dictionary<string, (string, bool)>
            {
                { "LISEZ-MOI (Mac).txt", (MacReadme, false) },
            });
        }

        Debug.Log($"Build {target} : {report.summary.result} → {Path.GetFullPath(result)}");
        if (ok) EditorUtility.RevealInFinder(result);
        return ok;
    }

    // rcodesign : outil open source qui signe les apps macOS depuis Windows.
    // Téléchargé une seule fois dans Library/ (ignoré par git), empreinte vérifiée.
    const string RcodesignVersion = "0.29.0";
    const string RcodesignSha256 = "54bb500e2da7a8de02fcae0f331d1cac6e6d7173b4281042ff9c528ba3159aaa";
    const string RcodesignDir = "Library/rcodesign";

    // Signature "ad-hoc" (sans compte développeur Apple) : obligatoire pour que le jeu tourne sur les Mac
    // Apple Silicon, et pour que macOS propose "Ouvrir quand même" au lieu de "endommagé".
    static bool SignMacApp(string appPath)
    {
        string exe = GetRcodesign();
        if (exe == null) return false;

        EditorUtility.DisplayProgressBar("Build macOS", "Signature du .app…", 1f);
        try
        {
            var process = Process.Start(new ProcessStartInfo(exe, $"sign \"{Path.GetFullPath(appPath)}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0) return true;

            Debug.LogError("Signature macOS échouée :\n" + output);
            EditorUtility.DisplayDialog("Signature macOS échouée", "Voir la console pour le détail.", "OK");
            return false;
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    static string GetRcodesign()
    {
        string exe = Path.Combine(RcodesignDir, "rcodesign.exe");
        if (File.Exists(exe)) return exe;

        string name = $"apple-codesign-{RcodesignVersion}-x86_64-pc-windows-msvc";
        string url = $"https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/{RcodesignVersion}/{name}.zip";
        EditorUtility.DisplayProgressBar("Build macOS", "Téléchargement de rcodesign (une seule fois)…", 0.5f);
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            byte[] zip;
            using (var client = new WebClient()) zip = client.DownloadData(url);

            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(zip)).Replace("-", "").ToLowerInvariant();
            if (hash != RcodesignSha256) throw new Exception("empreinte SHA-256 inattendue pour " + url);

            Directory.CreateDirectory(RcodesignDir);
            using (var archive = new ZipArchive(new MemoryStream(zip)))
            using (var entry = archive.GetEntry(name + "/rcodesign.exe").Open())
            using (var file = File.Create(exe))
                entry.CopyTo(file);
            return exe;
        }
        catch (Exception e)
        {
            Debug.LogError("Téléchargement de rcodesign impossible : " + e);
            EditorUtility.DisplayDialog("Signature macOS impossible",
                "Impossible de télécharger rcodesign (outil de signature macOS). Vérifie ta connexion puis relance le build.\n\n" + e.Message, "OK");
            return null;
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    // Jeu signé "ad-hoc" mais pas validé par Apple (il faudrait un compte développeur payant) :
    // macOS bloque la première ouverture, le joueur doit l'autoriser une fois.
    const string MacReadme =
        "Première ouverture sur Mac\n" +
        "==========================\n\n" +
        "macOS bloque les jeux qui ne viennent pas de l'App Store. A faire une seule fois :\n\n" +
        "1. Double-clique sur " + GameName + ".app. macOS affiche \"" + GameName + " n'a pas été ouvert\" : clique sur OK / Terminé.\n" +
        "2. Ouvre Réglages Système > Confidentialité et sécurité, descends tout en bas :\n" +
        "   à côté de \"" + GameName + " a été bloqué\", clique sur \"Ouvrir quand même\", puis confirme avec ton mot de passe.\n" +
        "   (Sur macOS 14 ou plus ancien, tu peux aussi faire clic droit sur " + GameName + ".app > Ouvrir > Ouvrir.)\n" +
        "3. Le jeu se lance. Les fois suivantes, un double-clic suffit.\n\n" +
        "Si ça ne marche toujours pas (\"endommagé\", pas de bouton \"Ouvrir quand même\") :\n" +
        "1. Ouvre le Terminal (Cmd + Espace, tape Terminal, Entrée).\n" +
        "2. Tape   xattr -cr   (avec un espace à la fin), puis glisse " + GameName + ".app dans la fenêtre du Terminal.\n" +
        "3. Appuie sur Entrée, puis double-clique sur " + GameName + ".app.\n\n" +
        "Conseil : sors " + GameName + ".app du dossier Téléchargements (mets-le par ex. dans Applications) avant de le lancer.\n";
}
