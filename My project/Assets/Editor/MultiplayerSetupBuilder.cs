using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu Tools > Multijoueur > Créer le menu et la scène de jeu
// Génère les prefabs réseau des persos, la scène MainMenu (rooms) et la scène Game, et les ajoute aux Build Settings.
public static class MultiplayerSetupBuilder
{
    const string NetDir = "Assets/Prefabs/Network";
    const string ControllerPath = "Assets/Animations/NetworkPlayer.controller";
    const string PrefabsListPath = NetDir + "/PlayerNetworkPrefabs.asset";
    const string MenuScenePath = "Assets/Scenes/" + NetworkGameManager.MenuScene + ".unity";
    const string GameScenePath = "Assets/Scenes/" + NetworkGameManager.GameScene + ".unity";

    const string MenuMaterialsDir = "Assets/Materials/Menu";

    // Persos jouables, choisis dans le menu
    static readonly string[] Characters =
    {
        "city/casual_Male_G",
        "city/casual_Female_G",
        "downtown/casual_Male_K",
        "downtown/casual_Female_K",
        "professions/Doctor_Male_B",
        "professions/police_Female_A",
    };

    // Noms affichés dans le menu (même ordre)
    static readonly string[] CharacterNames =
    {
        "Hugo", "Léa", "Malik", "Inès", "Docteur Martin", "Agent Durand",
    };

    // Palettes pas belles (people_pal_s22 : tout blanc)
    static readonly string[] ExcludedPalettes = { "people_pal_s22" };

    [MenuItem("Tools/Multijoueur/Créer le menu et la scène de jeu")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!AnimationTestSceneBuilder.PrepareMixamo()) return;

        AnimationTestSceneBuilder.EnsureFolder(NetDir);
        AnimatorController controller = AnimationTestSceneBuilder.CreatePlayerController(ControllerPath);

        GameObject[] prefabs = Characters.Select(c => CreatePlayerPrefab(c, controller)).ToArray();
        NetworkPrefabsList prefabsList = CreatePrefabsList(prefabs);

        CreateGameScene();
        CreateMenuScene(prefabs, prefabsList, LoadPalettes(), controller);
        SetBuildScenes();

        // Pour tester à plusieurs sur le même PC (éditeur + build), le jeu doit tourner même sans le focus
        PlayerSettings.runInBackground = true;

        EditorSceneManager.OpenScene(MenuScenePath);
        Debug.Log("Multijoueur prêt : scène " + MenuScenePath + " ouverte. Lance Play pour tester.");
    }

    static GameObject CreatePlayerPrefab(string character, AnimatorController controller)
    {
        string sourcePath = AnimationTestSceneBuilder.CityPrefabs + character + ".prefab";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null) throw new System.IO.FileNotFoundException("Prefab introuvable : " + sourcePath);

        // Instance du prefab City People -> sauvegardée comme variante avec les composants réseau
        var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
        foreach (var c in go.GetComponentsInChildren<CityPeople.CityPeople>())
            Object.DestroyImmediate(c);

        Animator animator = go.GetComponentInChildren<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        go.AddComponent<NetworkObject>();
        var netTransform = go.AddComponent<ClientNetworkTransform>();
        netTransform.SyncRotAngleX = false;
        netTransform.SyncRotAngleZ = false;
        netTransform.SyncScaleX = netTransform.SyncScaleY = netTransform.SyncScaleZ = false;
        animator.gameObject.AddComponent<OwnerNetworkAnimator>().Animator = animator;
        var player = go.AddComponent<NetworkPlayer>();
        player.walkSpeed = AnimationTestSceneBuilder.WalkSpeed;
        player.runSpeed = AnimationTestSceneBuilder.RunSpeed;

        string path = NetDir + "/NetPlayer_" + System.IO.Path.GetFileName(character) + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        // Le chargement de l'asset déclenche NetworkObject.OnValidate (calcul de l'identifiant réseau) : on le sauvegarde
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        EditorUtility.SetDirty(prefab.GetComponent<NetworkObject>());
        AssetDatabase.SaveAssets();
        return prefab;
    }

    static NetworkPrefabsList CreatePrefabsList(GameObject[] prefabs)
    {
        AssetDatabase.DeleteAsset(PrefabsListPath);
        var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
        foreach (GameObject prefab in prefabs)
            list.Add(new NetworkPrefab { Prefab = prefab });
        AssetDatabase.CreateAsset(list, PrefabsListPath);
        AssetDatabase.SaveAssets();
        return list;
    }

    // people_pal, people_pal_s1, ..., people_pal_s23 (ordre numérique)
    static Material[] LoadPalettes()
    {
        return AssetDatabase.FindAssets("people_pal t:Material", new[] { AnimationTestSceneBuilder.CityDir + "/Materials" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Where(m => m != null && m.name.StartsWith("people_pal"))
            .Where(m => !ExcludedPalettes.Contains(m.name))
            .OrderBy(m => int.TryParse(m.name.Replace("people_pal", "").TrimStart('_', 's'), out int n) ? n : 0)
            .GroupBy(m => m.mainTexture) // people_pal_s23 reprend la texture de people_pal : une seule fois
            .Select(g => g.First())
            .ToArray();
    }

    static void CreateMenuScene(GameObject[] prefabs, NetworkPrefabsList prefabsList, Material[] palettes, AnimatorController controller)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Fond dessiné par le menu (dégradé) ; la caméra est recadrée par LobbyMenu pour placer le perso à droite
        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = MenuStyle.BackgroundBottom;
        cam.fieldOfView = 30f;
        cam.transform.SetPositionAndRotation(new Vector3(-1.9f, 0.45f, -6.8f), Quaternion.identity);

        var previewSpot = new GameObject("AvatarPreview").transform;
        previewSpot.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
        CreatePreviewStage();

        // NetworkManager : survit au chargement de la scène de jeu
        var networkGo = new GameObject("NetworkManager");
        var transport = networkGo.AddComponent<UnityTransport>();
        var networkManager = networkGo.AddComponent<NetworkManager>();
        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.EnableSceneManagement = true;
        networkManager.NetworkConfig.ConnectionApproval = true; // le client envoie son modèle d'avatar
        networkManager.NetworkConfig.PlayerPrefab = null; // les persos sont créés par NetworkGameManager
        // Netcode ajoute tout seul sa liste DefaultNetworkPrefabs : on ne garde que la nôtre (sinon doublons)
        networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
        networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabsList);
        var gameManager = networkGo.AddComponent<NetworkGameManager>();
        gameManager.playerPrefabs = prefabs;
        gameManager.palettes = palettes;

        var menu = new GameObject("LobbyMenu").AddComponent<LobbyMenu>();
        menu.previewSpot = previewSpot;
        menu.avatarPreviewPrefabs = Characters
            .Select(c => AssetDatabase.LoadAssetAtPath<GameObject>(AnimationTestSceneBuilder.CityPrefabs + c + ".prefab"))
            .ToArray();
        menu.avatarNames = CharacterNames;
        menu.previewController = controller;

        EditorSceneManager.SaveScene(scene, MenuScenePath);
    }

    // Socle sous l'aperçu du perso (disque sombre + anneau lumineux) et éclairage de vitrine
    static void CreatePreviewStage()
    {
        AnimationTestSceneBuilder.EnsureFolder(MenuMaterialsDir);
        Material disc = MenuMaterial("MenuPlatform", new Color32(0x1E, 0x18, 0x36, 0xFF), Color.black, 0.85f);
        Material ring = MenuMaterial("MenuRing", MenuStyle.Accent, MenuStyle.Accent * 2.2f, 0.5f);

        Cylinder("PlatformRing", new Vector3(0f, -0.03f, 0f), new Vector3(1.75f, 0.02f, 1.75f), ring);
        Cylinder("Platform", new Vector3(0f, -0.02f, 0f), new Vector3(1.6f, 0.03f, 1.6f), disc);

        var sun = Object.FindObjectOfType<Light>();
        sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        sun.intensity = 1.1f;
        sun.color = new Color(1f, 0.95f, 0.9f);

        // Contre-jours colorés pour détacher le perso du fond
        PointLight("RimCyan", new Vector3(1.2f, 2f, 1.3f), MenuStyle.Secondary, 3.5f);
        PointLight("RimWarm", new Vector3(-1.3f, 1.4f, 1.2f), MenuStyle.Accent, 3f);
    }

    static Material MenuMaterial(string name, Color color, Color emission, float smoothness)
    {
        string path = MenuMaterialsDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color;
        mat.SetFloat("_Glossiness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static void Cylinder(string name, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void PointLight(string name, Vector3 position, Color color, float intensity)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Point;
        light.transform.position = position;
        light.color = color;
        light.intensity = intensity;
        light.range = 5f;
    }

    static void CreateGameScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = AnimationTestSceneBuilder.GroundMaterial();

        var cam = Camera.main;
        cam.transform.position = new Vector3(0f, 6f, -9f);
        cam.transform.LookAt(Vector3.zero);

        // Quelques décors City People pour se repérer
        string props = AnimationTestSceneBuilder.CityPrefabs + "PROPS/dummy props/";
        PlaceProp(props + "building_block_A.prefab", new Vector3(-12f, 0f, 14f), 180f);
        PlaceProp(props + "building_block_B.prefab", new Vector3(0f, 0f, 16f), 180f);
        PlaceProp(props + "building_block_C.prefab", new Vector3(12f, 0f, 14f), 180f);
        PlaceProp(props + "building_block_D.prefab", new Vector3(-16f, 0f, 0f), 90f);
        PlaceProp(props + "dprop_streetSign.prefab", new Vector3(5f, 0f, 6f), 0f);
        PlaceProp(props + "dprop_trashBin.prefab", new Vector3(-5f, 0f, 6f), 0f);
        PlaceProp(props + "dprop_bicycle.prefab", new Vector3(7f, 0f, -4f), 60f);
        PlaceProp(props + "dprop_plantPot.prefab", new Vector3(-7f, 0f, -4f), 0f);

        // Chat écrit
        new GameObject("Chat").AddComponent<GameChat>();

        EditorSceneManager.SaveScene(scene, GameScenePath);
    }

    static void PlaceProp(string path, Vector3 position, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning("Décor introuvable : " + path);
            return;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
    }

    // MainMenu en premier (scène de démarrage), puis Game, puis les scènes déjà présentes
    static void SetBuildScenes()
    {
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MenuScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true),
        };
        scenes.AddRange(EditorBuildSettings.scenes.Where(s => s.path != MenuScenePath && s.path != GameScenePath));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
