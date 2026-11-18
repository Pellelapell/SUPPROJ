using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Outils d'éditeur partagés par les constructeurs du jeu (MultiplayerSetupBuilder, CampusMapBuilder) :
// animations des persos City People (clips City People + Mixamo retargetés en Humanoid), sol, création de dossiers.
public static class CharacterSetup
{
    const string MixamoDir = "Assets/Animations/Mixamo";
    internal const string CityDir = "Assets/ThirdParty/CityPeople";
    const string CityAnims = CityDir + "/Animations/";
    internal const string CityPrefabs = CityDir + "/Prefabs/";
    const string GroundMatPath = "Assets/Animations/Ground.mat";

    // Seuils du blend tree = vitesses de déplacement du joueur (m/s)
    internal const float WalkSpeed = 1.5f, JogSpeed = 3f, RunSpeed = 5f;
    const float MoveThreshold = 0.1f; // en dessous, le perso est considéré immobile

    static readonly string[] PlayerClips =
    {
        CityAnims + "idle_m_1_200f.fbx",
        CityAnims + "locom_m_basicWalk_30f.fbx",
        CityAnims + "locom_m_jogging_30f.fbx",
        CityAnims + "locom_m_running_20f.fbx",
        MixamoDir + "/Waving.fbx",
    };

    // Mixamo en Humanoid (Idle crée l'avatar, les deux autres le réutilisent)
    internal static bool PrepareMixamo()
    {
        ConfigureMixamo(MixamoDir + "/Idle.fbx", null, true);
        Avatar mixamoAvatar = AssetDatabase.LoadAllAssetsAtPath(MixamoDir + "/Idle.fbx").OfType<Avatar>().FirstOrDefault();
        if (mixamoAvatar == null || !mixamoAvatar.isValid || !mixamoAvatar.isHuman)
        {
            Debug.LogError("Avatar Humanoid Mixamo invalide. Ouvre Idle.fbx > Rig > Configure... pour corriger le mapping.");
            return false;
        }
        ConfigureMixamo(MixamoDir + "/Walking.fbx", mixamoAvatar, true);
        ConfigureMixamo(MixamoDir + "/Waving.fbx", mixamoAvatar, false);

        foreach (string path in PlayerClips)
            LoadClip(path);
        return true;
    }

    static void ConfigureMixamo(string path, Avatar sourceAvatar, bool loop)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new FileNotFoundException("FBX introuvable : " + path);

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = sourceAvatar == null ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = sourceAvatar;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();

        // Clips : renommés comme le fichier, boucle, rotation/hauteur figées (le perso reste sur place)
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.name = Path.GetFileNameWithoutExtension(path);
            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static AnimationClip LoadClip(string path)
    {
        AnimationClip clip = FindClip(path);
        if (clip == null)
        {
            // Import pas encore à jour : on force la réimport puis on réessaie
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            clip = FindClip(path);
        }
        if (clip == null)
        {
            string found = string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(path).Select(o => o.GetType().Name + " '" + o.name + "'"));
            throw new FileNotFoundException("Clip introuvable dans " + path + " (contenu : " + found + ")");
        }
        return clip;
    }

    static AnimationClip FindClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    // Idle / marche / jogging / course selon "Speed", + salut immobile sur le trigger "Wave"
    internal static AnimatorController CreatePlayerController(string path)
    {
        AssetDatabase.DeleteAsset(path);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Wave", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(LoadClip(PlayerClips[0]), 0f);
        tree.AddChild(LoadClip(PlayerClips[1]), WalkSpeed);
        tree.AddChild(LoadClip(PlayerClips[2]), JogSpeed);
        tree.AddChild(LoadClip(PlayerClips[3]), RunSpeed);
        sm.defaultState = locomotion;

        AnimatorState waving = sm.AddState("Waving");
        waving.motion = LoadClip(PlayerClips[4]);

        AnimatorStateTransition toWave = sm.AddAnyStateTransition(waving);
        toWave.AddCondition(AnimatorConditionMode.If, 0f, "Wave");
        toWave.AddCondition(AnimatorConditionMode.Less, MoveThreshold, "Speed"); // pas de salut en marchant
        toWave.hasExitTime = false;
        toWave.duration = 0.2f;
        toWave.canTransitionToSelf = false;

        AnimatorStateTransition back = waving.AddTransition(locomotion);
        back.hasExitTime = true;
        back.exitTime = 0.9f;
        back.duration = 0.25f;

        // Le salut est immobile : se déplacer l'annule immédiatement
        AnimatorStateTransition cancel = waving.AddTransition(locomotion);
        cancel.AddCondition(AnimatorConditionMode.Greater, MoveThreshold, "Speed");
        cancel.hasExitTime = false;
        cancel.duration = 0.15f;

        // IK pour la pose assise (SeatedPose)
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;

        AssetDatabase.SaveAssets();
        return controller;
    }

    internal static Material GroundMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.57f, 0.6f) };
        AssetDatabase.CreateAsset(mat, GroundMatPath);
        return mat;
    }

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
