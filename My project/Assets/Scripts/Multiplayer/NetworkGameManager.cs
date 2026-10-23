using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Lobbies;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sur le même objet que le NetworkManager (survit aux changements de scène).
// Serveur : fait apparaître le perso choisi par chaque joueur quand il arrive dans la scène de jeu.
// Tous : retour au menu en cas de déconnexion ou quand on quitte.
[RequireComponent(typeof(NetworkManager))]
public class NetworkGameManager : MonoBehaviour
{
    public const string MenuScene = "MainMenu";
    public const string GameScene = "Game";

    public static NetworkGameManager Instance { get; private set; }
    public static string MenuMessage;            // affiché par le menu au retour (ex : "L'hôte a quitté")

    [Tooltip("Persos jouables, choisis dans l'éditeur d'avatar")]
    public GameObject[] playerPrefabs;
    [Tooltip("Palettes de couleurs City People (people_pal...)")]
    public Material[] palettes;
    public float spawnRadius = 3f;

    NetworkManager networkManager;
    readonly Dictionary<ulong, int> chosenModel = new Dictionary<ulong, int>();
    int spawnCount;
    bool returningToMenu;

    void Awake()
    {
        Instance = this;
        networkManager = GetComponent<NetworkManager>();
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        networkManager.OnServerStarted += HandleServerStarted;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnect;
        networkManager.ConnectionApprovalCallback = ApproveConnection;
    }

    void OnDisable()
    {
        networkManager.OnServerStarted -= HandleServerStarted;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnect;
        networkManager.ConnectionApprovalCallback = null;
        if (Instance == this) Instance = null;
    }

    // À appeler avant StartHost / StartClient : le modèle d'avatar choisi part avec la demande de connexion
    public void PrepareConnection()
    {
        networkManager.NetworkConfig.ConnectionData = new[] { (byte)Mathf.Clamp(PlayerProfile.Model, 0, playerPrefabs.Length - 1) };
    }

    // Appelé par l'hôte au lancement : il charge la scène de jeu, les clients y arrivent en se connectant
    public void StartGame()
    {
        networkManager.SceneManager.LoadScene(GameScene, LoadSceneMode.Single);
    }

    // La room Lobby reste quelques secondes après le lancement, le temps que les autres lisent le code Relay
    public async void DeleteLobbyLater(string lobbyId, float delay = 20f)
    {
        await Task.Delay(TimeSpan.FromSeconds(delay));
        try { await LobbyService.Instance.DeleteLobbyAsync(lobbyId); }
        catch (Exception e) { Debug.LogWarning(e); }
    }

    public Material GetPalette(int index)
    {
        if (palettes == null || palettes.Length == 0) return null;
        return palettes[Mathf.Clamp(index, 0, palettes.Length - 1)];
    }

    void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        chosenModel[request.ClientNetworkId] = request.Payload != null && request.Payload.Length > 0 ? request.Payload[0] : 0;
        response.Approved = true;
        response.CreatePlayerObject = false; // le perso est créé à l'arrivée dans la scène de jeu
    }

    void HandleServerStarted()
    {
        spawnCount = 0;
        networkManager.SceneManager.OnLoadEventCompleted += HandleSceneLoaded;
        networkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
    }

    // L'hôte a chargé la scène de jeu
    void HandleSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != GameScene) return;
        foreach (ulong clientId in clientsCompleted)
            SpawnPlayer(clientId);
    }

    // Un client vient de se connecter et a fini de charger la scène de l'hôte
    void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneEventType == SceneEventType.SynchronizeComplete && sceneEvent.ClientId != NetworkManager.ServerClientId)
            SpawnPlayer(sceneEvent.ClientId);
    }

    void SpawnPlayer(ulong clientId)
    {
        if (networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
            return; // déjà un perso

        if (!chosenModel.TryGetValue(clientId, out int model))
            model = clientId == NetworkManager.ServerClientId ? PlayerProfile.Model : 0;

        // Joueurs répartis en cercle, tournés vers le centre
        float angle = spawnCount++ * Mathf.PI * 2f / 8f;
        Vector3 pos = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * spawnRadius;
        Quaternion rot = Quaternion.LookRotation(-pos.normalized);

        GameObject prefab = playerPrefabs[Mathf.Clamp(model, 0, playerPrefabs.Length - 1)];
        GameObject player = Instantiate(prefab, pos, rot);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
    }

    void HandleClientDisconnect(ulong clientId)
    {
        // Côté client : perte de l'hôte (ou connexion impossible)
        if (!networkManager.IsServer)
            ReturnToMenu("Déconnecté de la partie (l'hôte a quitté ?)");
    }

    public void ReturnToMenu(string message = null)
    {
        if (returningToMenu) return;
        returningToMenu = true;
        MenuMessage = message;

        networkManager.Shutdown();
        // La scène du menu contient son propre NetworkManager : on détruit celui-ci pour éviter un doublon
        Destroy(gameObject);
        SceneManager.LoadScene(MenuScene);
    }

    void OnGUI()
    {
        if (SceneManager.GetActiveScene().name != GameScene) return;

        GUILayout.BeginArea(new Rect(Screen.width - 230f, 10f, 220f, 160f), GUI.skin.box);
        GUILayout.Label(networkManager.IsHost ? "Hôte de la partie" : "Connecté à la partie");
        GUILayout.Label("ZQSD : marcher   Maj : courir");
        GUILayout.Label("E : saluer   Entrée : chat");
        if (GUILayout.Button("Quitter la partie", GUILayout.Height(30f)))
            ReturnToMenu(networkManager.IsHost ? null : "Tu as quitté la partie.");
        GUILayout.EndArea();
    }
}
