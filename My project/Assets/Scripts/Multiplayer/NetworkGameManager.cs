using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sur le même objet que le NetworkManager (survit aux changements de scène).
// Serveur : fait apparaître le perso choisi par chaque joueur, limite la room à MaxPlayers et tient à jour
// l'annonce de la room dans Unity Lobby (ping + nombre de joueurs) pendant toute la partie.
// Tous : retour au menu en cas de déconnexion ou quand on quitte.
[RequireComponent(typeof(NetworkManager))]
public class NetworkGameManager : MonoBehaviour
{
    public const string MenuScene = "MainMenu";
    public const string GameScene = "Game";

    // Une seule grande room, une deuxième ouvre seulement quand la première est pleine
    public const int MaxPlayers = 30;
    public const int MaxRooms = 2;

    // Données publiques de l'annonce Lobby d'une room
    public const string JoinCodeKey = "joinCode";
    public const string PlayersKey = "players";

    public static NetworkGameManager Instance { get; private set; }
    public static string MenuMessage;            // affiché par le menu au retour (ex : "L'hôte a quitté")
    public static string RoomName;               // room actuelle, affichée en jeu

    [Tooltip("Persos jouables, choisis dans l'éditeur d'avatar")]
    public GameObject[] playerPrefabs;
    [Tooltip("Palettes de couleurs City People (people_pal...)")]
    public Material[] palettes;
    [Tooltip("Écart entre les joueurs à l'apparition (ils sont placés en spirale)")]
    public float spawnSpacing = 1.3f;

    NetworkManager networkManager;
    readonly Dictionary<ulong, int> chosenModel = new Dictionary<ulong, int>();
    int spawnCount;
    bool returningToMenu;

    // Annonce Lobby de la room (hôte seulement)
    string hostLobbyId;
    float nextHeartbeat, nextCountPush;
    bool countDirty;

    // Nombre de joueurs affiché en jeu (compté localement, valable aussi côté client)
    int playersOnline;
    float nextPlayersCount;

    void Awake()
    {
        Instance = this;
        networkManager = GetComponent<NetworkManager>();
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        networkManager.OnServerStarted += HandleServerStarted;
        networkManager.OnClientConnectedCallback += HandleClientConnect;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnect;
        networkManager.ConnectionApprovalCallback = ApproveConnection;
    }

    void OnDisable()
    {
        networkManager.OnServerStarted -= HandleServerStarted;
        networkManager.OnClientConnectedCallback -= HandleClientConnect;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnect;
        networkManager.ConnectionApprovalCallback = null;
        if (Instance == this) Instance = null;
    }

    // À appeler avant StartHost / StartClient : le modèle d'avatar choisi part avec la demande de connexion
    public void PrepareConnection()
    {
        networkManager.NetworkConfig.ConnectionData = new[] { (byte)Mathf.Clamp(PlayerProfile.Model, 0, playerPrefabs.Length - 1) };
    }

    // Appelé par l'hôte une fois la room annoncée : il charge la scène de jeu, les clients y arrivent en se connectant
    public void StartGame(string lobbyId, string roomName)
    {
        hostLobbyId = lobbyId;
        RoomName = roomName;
        nextHeartbeat = Time.unscaledTime + 15f;
        networkManager.SceneManager.LoadScene(GameScene, LoadSceneMode.Single);
    }

    public Material GetPalette(int index)
    {
        if (palettes == null || palettes.Length == 0) return null;
        return palettes[Mathf.Clamp(index, 0, palettes.Length - 1)];
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (now >= nextPlayersCount)
        {
            nextPlayersCount = now + 1f;
            playersOnline = FindObjectsOfType<NetworkPlayer>().Length;
        }

        if (hostLobbyId == null || !networkManager.IsServer) return;

        // Sans ping toutes les 30 s, Unity Lobby considère la room inactive et ne la liste plus
        if (now >= nextHeartbeat)
        {
            nextHeartbeat = now + 15f;
            _ = Safe(() => LobbyService.Instance.SendHeartbeatPingAsync(hostLobbyId));
        }

        // Nombre de joueurs publié pour le menu (regroupé : Lobby limite le nombre de mises à jour)
        if (countDirty && now >= nextCountPush)
        {
            countDirty = false;
            nextCountPush = now + 3f;
            var update = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { PlayersKey, new DataObject(DataObject.VisibilityOptions.Public, networkManager.ConnectedClientsIds.Count.ToString()) },
                },
            };
            string id = hostLobbyId;
            _ = Safe(() => LobbyService.Instance.UpdateLobbyAsync(id, update));
        }
    }

    void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        if (networkManager.ConnectedClientsIds.Count >= MaxPlayers)
        {
            response.Approved = false;
            response.Reason = $"La room est pleine ({MaxPlayers} joueurs).";
            return;
        }

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

        // Spirale (angle d'or) : jusqu'à 30 joueurs bien espacés, tournés vers le centre
        int i = spawnCount++ % MaxPlayers;
        float angle = i * 2.39996f;
        Vector3 pos = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (spawnSpacing * Mathf.Sqrt(i + 1));
        Quaternion rot = Quaternion.LookRotation(-pos.normalized);

        GameObject prefab = playerPrefabs[Mathf.Clamp(model, 0, playerPrefabs.Length - 1)];
        GameObject player = Instantiate(prefab, pos, rot);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
    }

    void HandleClientConnect(ulong clientId)
    {
        if (networkManager.IsServer) countDirty = true;
    }

    void HandleClientDisconnect(ulong clientId)
    {
        if (networkManager.IsServer)
        {
            chosenModel.Remove(clientId);
            countDirty = true;
            return;
        }

        // Côté client : perte de l'hôte, room pleine ou connexion impossible
        string reason = networkManager.DisconnectReason;
        ReturnToMenu(string.IsNullOrEmpty(reason) ? "Déconnecté de la partie (l'hôte a quitté ?)" : reason);
    }

    public void ReturnToMenu(string message = null)
    {
        if (returningToMenu) return;
        returningToMenu = true;
        MenuMessage = message;
        CloseRoom();

        networkManager.Shutdown();
        // La scène du menu contient son propre NetworkManager : on détruit celui-ci pour éviter un doublon
        Destroy(gameObject);
        SceneManager.LoadScene(MenuScene);
    }

    void OnApplicationQuit() => CloseRoom();

    // L'hôte part : la room disparaît de la liste tout de suite (sinon au bout de 30 s sans ping)
    void CloseRoom()
    {
        if (hostLobbyId == null) return;
        string id = hostLobbyId;
        hostLobbyId = null;
        _ = Safe(() => LobbyService.Instance.DeleteLobbyAsync(id));
    }

    static async Task Safe(Func<Task> call)
    {
        try { await call(); }
        catch (Exception e) { Debug.LogWarning(e); }
    }

    void OnGUI()
    {
        if (SceneManager.GetActiveScene().name != GameScene) return;

        GUILayout.BeginArea(new Rect(Screen.width - 230f, 10f, 220f, 180f), GUI.skin.box);
        GUILayout.Label($"{RoomName ?? "Room"}  —  {playersOnline}/{MaxPlayers} joueurs");
        GUILayout.Label(networkManager.IsHost ? "Tu héberges la room" : "Connecté à la room");
        GUILayout.Label("ZQSD : marcher   Maj : courir");
        GUILayout.Label("E : saluer   Entrée : chat");
        if (GUILayout.Button("Quitter la partie", GUILayout.Height(30f)))
            ReturnToMenu(networkManager.IsHost ? null : "Tu as quitté la partie.");
        GUILayout.EndArea();
    }
}
