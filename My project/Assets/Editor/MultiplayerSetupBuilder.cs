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

    // Un perso différent par joueur (dans l'ordre d'arrivée dans la room)
    static readonly string[] Characters =
    {
        "city/casual_Male_G",
        "city/casual_Female_G",
        "downtown/casual_Male_K",
        "downtown/casual_Female_K",
        "professions/Doctor_Male_B",
        "professions/police_Female_A",
        "elder/elder_Female_A",
        "little_kids/little_boy_B",
    };

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
        CreateMenuScene(prefabs, prefabsList, LoadPalettes());
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
            .OrderBy(m => int.TryParse(m.name.Replace("people_pal", "").TrimStart('_', 's'), out int n) ? n : 0)
            .ToArray();
    }

    static void CreateMenuScene(GameObject[] prefabs, NetworkPrefabsList prefabsList, Material[] palettes)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Caméra décalée : l'aperçu de l'avatar apparaît à droite, le menu est à gauche
        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
        cam.transform.position = new Vector3(-1.3f, 1.2f, -3.6f);
        cam.transform.LookAt(new Vector3(-1.3f, 0.9f, 0f));

        var previewSpot = new GameObject("AvatarPreview").transform;
        previewSpot.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

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

        EditorSceneManager.SaveScene(scene, MenuScenePath);
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
