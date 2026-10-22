using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu Tools > Animation Test > Créer la scène de test
// Persos du pack City People + animations City People et Mixamo, retargetées en Humanoid.
public static class AnimationTestSceneBuilder
{
    const string MixamoDir = "Assets/Animations/Mixamo";
    const string CityDir = "Assets/DenysAlmaral/CityPeople";
    const string CityAnims = CityDir + "/Animations/";
    const string CityPrefabs = CityDir + "/Prefabs/";

    const string PlayerControllerPath = "Assets/Animations/TestCharacter.controller";
    const string ShowcaseControllerPath = "Assets/Animations/Showcase.controller";
    const string GroundMatPath = "Assets/Animations/Ground.mat";
    const string ScenePath = "Assets/Scenes/AnimationTest.unity";

    // Seuils du blend tree = vitesses de déplacement du joueur (m/s)
    const float WalkSpeed = 1.5f, JogSpeed = 3f, RunSpeed = 5f;

    static readonly string[] PlayerClips =
    {
        CityAnims + "idle_m_1_200f.fbx",
        CityAnims + "locom_m_basicWalk_30f.fbx",
        CityAnims + "locom_m_jogging_30f.fbx",
        CityAnims + "locom_m_running_20f.fbx",
        MixamoDir + "/Waving.fbx",
    };

    // (prefab, clip) : une ligne par perso de démo
    static readonly (string prefab, string clip)[] RowFront =
    {
        ("city/casual_Female_G", CityAnims + "idle_f_1_150f.fbx"),
        ("city/casual_Male_G", CityAnims + "locom_m_basicWalk_30f.fbx"),
        ("downtown/casual_Female_K", CityAnims + "locom_f_jogging_30f.fbx"),
        ("downtown/casual_Male_K", CityAnims + "locom_m_running_20f.fbx"),
        ("elder/elder_Female_A", CityAnims + "locom_f_slowWalk_40f.fbx"),
        ("professions/Doctor_Male_B", CityAnims + "idle_phoneTalking_180f.fbx"),
        ("professions/police_Female_A", CityAnims + "exercise_warmingUp_170f.fbx"),
    };

    static readonly (string prefab, string clip)[] RowBack =
    {
        ("little_kids/little_boy_B", CityAnims + "dance_hype_100f.fbx"),
        ("city/casual_Male_G", CityAnims + "dance_m_flossing_40f.fbx"),
        ("city/casual_Female_G", CityAnims + "dance_riverdance_60f.fbx"),
        ("downtown/casual_Female_K", CityAnims + "dance_afro_56f.fbx"),
        ("downtown/casual_Male_K", MixamoDir + "/Idle.fbx"),
        ("professions/Doctor_Male_B", MixamoDir + "/Walking.fbx"),
        ("professions/police_Female_A", MixamoDir + "/Waving.fbx"),
    };

    [MenuItem("Tools/Animation Test/Créer la scène de test")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // 1. Mixamo en Humanoid (Idle crée l'avatar, les deux autres le réutilisent)
        ConfigureMixamo(MixamoDir + "/Idle.fbx", null, true);
        Avatar mixamoAvatar = AssetDatabase.LoadAllAssetsAtPath(MixamoDir + "/Idle.fbx").OfType<Avatar>().FirstOrDefault();
        if (mixamoAvatar == null || !mixamoAvatar.isValid || !mixamoAvatar.isHuman)
        {
            Debug.LogError("Avatar Humanoid Mixamo invalide. Ouvre Idle.fbx > Rig > Configure... pour corriger le mapping.");
            return;
        }
        ConfigureMixamo(MixamoDir + "/Walking.fbx", mixamoAvatar, true);
        ConfigureMixamo(MixamoDir + "/Waving.fbx", mixamoAvatar, false);

        // Vérifie que tous les clips se chargent avant de modifier quoi que ce soit
        foreach (string path in PlayerClips.Concat(RowFront.Select(r => r.clip)).Concat(RowBack.Select(r => r.clip)))
            LoadClip(path);

        // 2. Controllers
        AnimatorController playerController = CreatePlayerController();
        AnimatorController showcaseController = CreateShowcaseController();

        // 3. Scène
        CreateScene(playerController, showcaseController);
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

    static AnimatorController CreatePlayerController()
    {
        AssetDatabase.DeleteAsset(PlayerControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(PlayerControllerPath);
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
        toWave.hasExitTime = false;
        toWave.duration = 0.2f;
        toWave.canTransitionToSelf = false;

        AnimatorStateTransition back = waving.AddTransition(locomotion);
        back.hasExitTime = true;
        back.exitTime = 0.9f;
        back.duration = 0.25f;

        AssetDatabase.SaveAssets();
        return controller;
    }

    // Un état par clip de démo, joué par AnimTestShowcase via Animator.Play
    static AnimatorController CreateShowcaseController()
    {
        AssetDatabase.DeleteAsset(ShowcaseControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ShowcaseControllerPath);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        foreach (var (_, clipPath) in RowFront.Concat(RowBack))
        {
            AnimationClip clip = LoadClip(clipPath);
            if (sm.states.Any(s => s.state.name == clip.name)) continue;
            sm.AddState(clip.name).motion = clip;
        }

        AssetDatabase.SaveAssets();
        return controller;
    }

    static void CreateScene(AnimatorController playerController, AnimatorController showcaseController)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = GroundMaterial();

        var cam = Camera.main;
        cam.transform.position = new Vector3(0f, 4f, -7f);
        cam.transform.LookAt(new Vector3(0f, 0.8f, 2.5f));

        // Perso joueur
        Animator player = Spawn("casual_Male_G (Player)", "city/casual_Male_G", new Vector3(0f, 0f, -1.5f), 0f, playerController);
        var playerScript = player.gameObject.AddComponent<AnimTestPlayer>();
        playerScript.walkSpeed = WalkSpeed;
        playerScript.runSpeed = RunSpeed;

        // Persos de démo, face caméra, sur deux rangées
        SpawnRow(RowFront, 2.5f, showcaseController);
        SpawnRow(RowBack, 5f, showcaseController);

        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = player.gameObject;
        Debug.Log("Scène de test créée : " + ScenePath + " — lance Play pour tester.");
    }

    static void SpawnRow((string prefab, string clip)[] row, float z, AnimatorController controller)
    {
        const float spacing = 1.6f;
        float x0 = -spacing * (row.Length - 1) * 0.5f;
        for (int i = 0; i < row.Length; i++)
        {
            string clipName = LoadClip(row[i].clip).name;
            Animator animator = Spawn(clipName, row[i].prefab, new Vector3(x0 + i * spacing, 0f, z), 180f, controller);
            var showcase = animator.gameObject.AddComponent<AnimTestShowcase>();
            showcase.stateName = clipName;
            showcase.label = clipName;
        }
    }

    static Animator Spawn(string name, string prefabName, Vector3 pos, float yaw, AnimatorController controller)
    {
        string path = CityPrefabs + prefabName + ".prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new FileNotFoundException("Prefab introuvable : " + path);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

        // Le script CityPeople lance des anims au hasard : on le retire pour garder le contrôle
        foreach (var c in go.GetComponentsInChildren<CityPeople.CityPeople>())
            Object.DestroyImmediate(c);

        Animator animator = go.GetComponentInChildren<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        return animator;
    }

    static Material GroundMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.57f, 0.6f) };
        AssetDatabase.CreateAsset(mat, GroundMatPath);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
