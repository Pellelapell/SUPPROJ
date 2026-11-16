using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Menu Tools > Build : le jeu se joue uniquement dans le navigateur (WebGL), en deux versions dans Builds/ (ignoré par git) :
// Admin (symbole ADMIN_BUILD, ouvre et héberge les rooms) et Élève (rejoint seulement les rooms ouvertes).
// Chaque build donne un dossier jouable (Builds/Web/...) et un zip à mettre en ligne (itch.io, GitHub Pages...).
// Demande le module "WebGL Build Support" dans Unity Hub.
public static class BuildTools
{
    const string GameName = "SUPPROJ";
    const string BuildDir = "Builds";
    const string AdminDefine = "ADMIN_BUILD";

    [MenuItem("Tools/Build/Élève - Web", priority = 1)]
    public static void BuildStudent() => Build(false);

    [MenuItem("Tools/Build/Admin - Web", priority = 2)]
    public static void BuildAdmin() => Build(true);

    [MenuItem("Tools/Build/Tout (Admin + Élève)", priority = 20)]
    public static void BuildAll()
    {
        if (Build(true)) Build(false);
    }

    // Dans l'éditeur on joue en admin ; cocher ce menu pour voir le menu tel que les élèves le voient
    const string StudentToggle = "Tools/Build/Tester en élève dans l'éditeur";

    [MenuItem(StudentToggle, priority = 60)]
    static void ToggleStudent() => EditorPrefs.SetBool(AppRole.EditorStudentKey, !EditorPrefs.GetBool(AppRole.EditorStudentKey, false));

    [MenuItem(StudentToggle, true)]
    static bool ToggleStudentValidate()
    {
        Menu.SetChecked(StudentToggle, EditorPrefs.GetBool(AppRole.EditorStudentKey, false));
        return true;
    }

    // Builds/Web/Eleve/index.html + Builds/SUPPROJ-Eleve-Web.zip
    static bool Build(bool admin)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            EditorUtility.DisplayDialog("Module manquant",
                "Le module \"WebGL Build Support\" n'est pas installé.\n\n" +
                "Unity Hub > Installs > 2022.3.62f3 > ⚙ > Add modules, coche-le puis relance Unity.", "OK");
            return false;
        }

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("Aucune scène", "Lance d'abord Tools > Multijoueur > Créer le menu et la scène de jeu.", "OK");
            return false;
        }

        string role = admin ? "Admin" : "Eleve";
        string folder = $"{BuildDir}/Web/{role}";
        PlayerSettings.productName = GameName;
        PlayerSettings.runInBackground = true;
        // Gzip + décompression dans le navigateur : marche sur n'importe quel hébergeur, sans réglage serveur
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;

        Directory.CreateDirectory(folder);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = folder,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            extraScriptingDefines = admin ? new[] { AdminDefine } : null,
        });
        Debug.Log($"Build Web {(admin ? "Admin" : "Élève")} : {report.summary.result} → {Path.GetFullPath(folder)}");
        if (report.summary.result != BuildResult.Succeeded) return false;

        // Module installé pendant qu'Unity était ouvert : Unity annonce "Succeeded" sans créer l'index.html
        if (!File.Exists(Path.Combine(folder, "index.html")))
        {
            EditorUtility.DisplayDialog("Build Web incomplet",
                "Unity n'a rien créé : le module WebGL a sans doute été installé pendant qu'Unity était ouvert.\n\n" +
                "Ferme Unity, rouvre le projet, puis relance le build (Tools > Build).", "OK");
            return false;
        }

        string zip = Path.Combine(BuildDir, $"{GameName}-{role}-Web.zip");
        if (File.Exists(zip)) File.Delete(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                archive.CreateEntryFromFile(file, Path.GetRelativePath(folder, file).Replace('\\', '/'));
        Debug.Log("Version Web prête à mettre en ligne → " + Path.GetFullPath(zip));
        EditorUtility.RevealInFinder(zip);
        return true;
    }
}
