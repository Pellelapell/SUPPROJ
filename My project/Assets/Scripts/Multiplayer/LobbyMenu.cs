using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

// Menu principal : liste des rooms (Unity Lobby), création d'une room, salle d'attente, lancement de la partie.
// La salle d'attente n'utilise que Lobby. Au lancement, l'hôte crée un serveur relais (Unity Relay) et publie son
// code dans la room : les autres joueurs le lisent et se connectent directement dans la scène de jeu.
public class LobbyMenu : MonoBehaviour
{
    const string JoinCodeKey = "joinCode";
    const string NameKey = "name";

    public int maxPlayers = 4;

    [Header("Éditeur d'avatar")]
    [Tooltip("Mêmes persos, dans le même ordre, que NetworkGameManager.playerPrefabs (sans réseau, pour l'aperçu)")]
    public GameObject[] avatarPreviewPrefabs;
    public Transform previewSpot;

    enum MenuScreen { Connecting, Rooms, Avatar, InRoom, Error }

    MenuScreen screen = MenuScreen.Connecting;
    string status = "Connexion aux services Unity...";
    string playerName;
    string roomName;
    bool busy;

    List<Lobby> rooms = new List<Lobby>();
    Lobby currentLobby;
    bool isHost;
    float nextRoomsRefresh, nextLobbyPoll, nextHeartbeat;

    NetworkManager Network => NetworkManager.Singleton;
    NetworkGameManager GameManager => Network.GetComponent<NetworkGameManager>();
    UnityTransport Transport => Network.GetComponent<UnityTransport>();

    async void Start()
    {
        playerName = PlayerProfile.Name;
        if (string.IsNullOrEmpty(playerName)) playerName = "Joueur" + UnityEngine.Random.Range(100, 1000);
        PlayerProfile.Name = playerName;
        roomName = "Room de " + playerName;
        if (!string.IsNullOrEmpty(NetworkGameManager.MenuMessage))
        {
            status = NetworkGameManager.MenuMessage;
            NetworkGameManager.MenuMessage = null;
        }

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // Profil aléatoire : permet de lancer plusieurs instances sur le même PC (éditeur + build) pour tester
                var options = new InitializationOptions().SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 12));
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            screen = MenuScreen.Rooms;
            if (status.StartsWith("Connexion")) status = "";
            await RefreshRooms();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            screen = MenuScreen.Error;
            status = "Impossible de se connecter aux services Unity.\n" +
                     "Vérifie ta connexion Internet et que le projet est lié à Unity Cloud " +
                     "(Edit > Project Settings > Services) avec Lobby et Relay activés.\n\n" + e.Message;
        }
    }

    void Update()
    {
        if (busy) return;
        float now = Time.unscaledTime;

        if (screen == MenuScreen.Rooms && now >= nextRoomsRefresh)
            _ = RefreshRooms();

        if (screen == MenuScreen.InRoom && currentLobby != null)
        {
            // L'hôte doit "pinger" la room, sinon Unity Lobby la considère inactive au bout de 30 s
            if (isHost && now >= nextHeartbeat)
            {
                nextHeartbeat = now + 15f;
                _ = Safe(() => LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id));
            }
            if (now >= nextLobbyPoll)
                _ = PollLobby();
        }
    }

    // ---------- Liste des rooms ----------

    async Task RefreshRooms()
    {
        nextRoomsRefresh = Time.unscaledTime + 3f;
        try
        {
            var options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                },
            };
            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
            rooms = response.Results.Where(l => !l.IsLocked).ToList();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning(e);
        }
    }

    async Task CreateRoom()
    {
        busy = true;
        status = "Création de la room...";
        try
        {
            var options = new CreateLobbyOptions { IsPrivate = false, Player = MakePlayer() };
            currentLobby = await LobbyService.Instance.CreateLobbyAsync(roomName.Trim(), maxPlayers, options);
            isHost = true;
            screen = MenuScreen.InRoom;
            status = "";
            nextHeartbeat = Time.unscaledTime + 15f;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            status = "Erreur pendant la création : " + e.Message;
        }
        busy = false;
    }

    async Task JoinRoom(Lobby lobby)
    {
        busy = true;
        status = "Connexion à " + lobby.Name + "...";
        try
        {
            currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id, new JoinLobbyByIdOptions { Player = MakePlayer() });
            isHost = false;
            screen = MenuScreen.InRoom;
            status = "";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            status = "Impossible de rejoindre : " + e.Message;
            await RefreshRooms();
        }
        busy = false;
    }

    // ---------- Salle d'attente ----------

    async Task PollLobby()
    {
        nextLobbyPoll = Time.unscaledTime + 2f;
        try
        {
            currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning(e);
            if (!isHost)
            {
                // Room supprimée par l'hôte (ou on a été retiré)
                currentLobby = null;
                screen = MenuScreen.Rooms;
                status = "La room a été fermée.";
            }
            return;
        }

        // L'hôte a lancé la partie : son code Relay est publié dans la room
        if (!isHost && currentLobby.Data != null && currentLobby.Data.TryGetValue(JoinCodeKey, out DataObject code))
            await JoinGame(code.Value);
    }

    async Task StartGame()
    {
        busy = true;
        status = "Lancement de la partie...";
        try
        {
            // 1. Serveur relais pour les autres joueurs
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            RelayServerEndpoint endpoint = GetEndpoint(allocation.ServerEndpoints);
            Transport.SetRelayServerData(endpoint.Host, (ushort)endpoint.Port,
                allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData, null, endpoint.Secure);

            // 2. On démarre en hôte
            GameManager.PrepareConnection();
            if (!Network.StartHost()) throw new Exception("Le démarrage de l'hôte a échoué.");

            // 3. Code publié dans la room (fermée aux nouveaux) : les autres se connectent
            var update = new UpdateLobbyOptions
            {
                IsLocked = true,
                Data = new Dictionary<string, DataObject>
                {
                    { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Member, joinCode) },
                },
            };
            await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, update);
            GameManager.DeleteLobbyLater(currentLobby.Id);
            currentLobby = null;

            // 4. Tout le monde passe dans la scène de jeu
            GameManager.StartGame();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            status = "Erreur au lancement : " + e.Message;
            if (Network.IsListening) Network.Shutdown();
            busy = false;
        }
    }

    async Task JoinGame(string joinCode)
    {
        busy = true;
        status = "Connexion à la partie...";
        try
        {
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            RelayServerEndpoint endpoint = GetEndpoint(allocation.ServerEndpoints);
            Transport.SetRelayServerData(endpoint.Host, (ushort)endpoint.Port,
                allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData, allocation.HostConnectionData, endpoint.Secure);

            // La room n'est plus utile pour ce joueur (l'hôte la supprimera)
            await LeaveLobbyOnly();

            // Netcode charge la scène de jeu de l'hôte automatiquement après la connexion
            GameManager.PrepareConnection();
            if (!Network.StartClient()) throw new Exception("La connexion au jeu a échoué.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            screen = MenuScreen.Rooms;
            status = "Impossible de rejoindre la partie : " + e.Message;
            busy = false;
        }
    }

    async Task LeaveRoom()
    {
        busy = true;
        await LeaveLobbyOnly();
        screen = MenuScreen.Rooms;
        status = "";
        busy = false;
        await RefreshRooms();
    }

    async Task LeaveLobbyOnly()
    {
        if (currentLobby == null) return;
        string id = currentLobby.Id;
        currentLobby = null;
        if (isHost)
            await Safe(() => LobbyService.Instance.DeleteLobbyAsync(id));
        else
            await Safe(() => LobbyService.Instance.RemovePlayerAsync(id, AuthenticationService.Instance.PlayerId));
    }

    async void OnApplicationQuit()
    {
        // Évite de laisser une room fantôme dans la liste
        await LeaveLobbyOnly();
    }

    // ---------- Utilitaires ----------

    Player MakePlayer()
    {
        return new Player(data: new Dictionary<string, PlayerDataObject>
        {
            { NameKey, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName.Trim()) },
        });
    }

    static RelayServerEndpoint GetEndpoint(List<RelayServerEndpoint> endpoints)
    {
        return endpoints.FirstOrDefault(e => e.ConnectionType == "dtls") ?? endpoints.First();
    }

    static async Task Safe(Func<Task> call)
    {
        try { await call(); }
        catch (Exception e) { Debug.LogWarning(e); }
    }

    // ---------- Interface ----------

    GUIStyle title, label, button;

    void OnGUI()
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
        }

        float width = Mathf.Min(520f, Screen.width - 32f);
        float x = screen == MenuScreen.Avatar ? 40f : (Screen.width - width) * 0.5f;
        if (screen == MenuScreen.Avatar) width = Mathf.Min(420f, Screen.width * 0.45f);
        var area = new Rect(x, 40f, width, Screen.height - 80f);
        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.Label("SUPPROJ — Multijoueur", title, GUILayout.Height(60f));

        GUI.enabled = !busy;
        switch (screen)
        {
            case MenuScreen.Connecting: GUILayout.Label(status, label); break;
            case MenuScreen.Error: GUILayout.Label(status, label); break;
            case MenuScreen.Rooms: DrawRooms(); break;
            case MenuScreen.Avatar: DrawAvatarEditor(); break;
            case MenuScreen.InRoom: DrawRoom(); break;
        }
        GUI.enabled = true;

        if (screen != MenuScreen.Connecting && screen != MenuScreen.Error && !string.IsNullOrEmpty(status))
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(status, label);
        }
        GUILayout.EndArea();
    }

    void DrawRooms()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Pseudo :", label, GUILayout.Width(110f));
        string newName = GUILayout.TextField(playerName, 16, GUILayout.Height(26f));
        if (newName != playerName)
        {
            playerName = newName;
            PlayerProfile.Name = playerName.Trim();
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Mon avatar", button, GUILayout.Height(32f)))
            OpenAvatarEditor();
        GUILayout.Space(8f);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Nom de room :", label, GUILayout.Width(110f));
        roomName = GUILayout.TextField(roomName, 30, GUILayout.Height(26f));
        GUILayout.EndHorizontal();

        bool hasName = playerName.Trim().Length > 0;
        GUI.enabled = !busy && hasName && roomName.Trim().Length > 0;
        if (GUILayout.Button("Créer une room", button, GUILayout.Height(40f)))
        {
            _ = CreateRoom();
        }
        GUI.enabled = !busy;

        GUILayout.Space(16f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Rooms disponibles", label);
        if (GUILayout.Button("Rafraîchir", button, GUILayout.Width(110f))) _ = RefreshRooms();
        GUILayout.EndHorizontal();

        if (rooms.Count == 0)
            GUILayout.Label("Aucune room pour l'instant. Crée la tienne !", label);

        foreach (Lobby lobby in rooms)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"{lobby.Name}   ({lobby.Players.Count}/{lobby.MaxPlayers})", label);
            GUI.enabled = !busy && hasName;
            if (GUILayout.Button("Rejoindre", button, GUILayout.Width(110f), GUILayout.Height(30f)))
            {
                _ = JoinRoom(lobby);
            }
            GUI.enabled = !busy;
            GUILayout.EndHorizontal();
        }
    }

    // ---------- Éditeur d'avatar ----------

    GameObject preview;

    void OpenAvatarEditor()
    {
        screen = MenuScreen.Avatar;
        status = "";
        RefreshPreview();
    }

    void CloseAvatarEditor()
    {
        if (preview != null) Destroy(preview);
        screen = MenuScreen.Rooms;
    }

    void RefreshPreview()
    {
        if (preview != null) Destroy(preview);
        if (avatarPreviewPrefabs == null || avatarPreviewPrefabs.Length == 0 || previewSpot == null) return;

        int model = Mathf.Clamp(PlayerProfile.Model, 0, avatarPreviewPrefabs.Length - 1);
        preview = Instantiate(avatarPreviewPrefabs[model], previewSpot.position, previewSpot.rotation, previewSpot);
        if (NetworkGameManager.Instance != null)
            PlayerProfile.ApplyPalette(preview, NetworkGameManager.Instance.GetPalette(PlayerProfile.Palette));
    }

    void LateUpdate()
    {
        // L'aperçu tourne doucement sur lui-même
        if (screen == MenuScreen.Avatar && previewSpot != null)
            previewSpot.Rotate(0f, 30f * Time.deltaTime, 0f);
    }

    void DrawAvatarEditor()
    {
        int modelCount = avatarPreviewPrefabs != null ? avatarPreviewPrefabs.Length : 0;
        int paletteCount = NetworkGameManager.Instance != null && NetworkGameManager.Instance.palettes != null
            ? NetworkGameManager.Instance.palettes.Length : 0;

        GUILayout.Label("Crée ton avatar", label);
        GUILayout.Space(8f);

        if (modelCount > 0)
        {
            int model = Mathf.Clamp(PlayerProfile.Model, 0, modelCount - 1);
            int newModel = Selector("Perso", model, modelCount, avatarPreviewPrefabs[model].name);
            if (newModel != model)
            {
                PlayerProfile.Model = newModel;
                RefreshPreview();
            }
        }

        if (paletteCount > 0)
        {
            int palette = Mathf.Clamp(PlayerProfile.Palette, 0, paletteCount - 1);
            int newPalette = Selector("Couleurs", palette, paletteCount, $"Palette {palette + 1}");
            if (newPalette != palette)
            {
                PlayerProfile.Palette = newPalette;
                RefreshPreview();
            }
        }

        GUILayout.Space(16f);
        if (GUILayout.Button("Valider", button, GUILayout.Height(40f)))
            CloseAvatarEditor();
    }

    // Ligne  "Libellé   <   valeur   >"  qui fait défiler un index en boucle
    int Selector(string title, int index, int count, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(title, label, GUILayout.Width(90f));
        if (GUILayout.Button("<", button, GUILayout.Width(40f), GUILayout.Height(30f))) index = (index - 1 + count) % count;
        GUILayout.Label($"{value}  ({index + 1}/{count})", label, GUILayout.MinWidth(150f));
        if (GUILayout.Button(">", button, GUILayout.Width(40f), GUILayout.Height(30f))) index = (index + 1) % count;
        GUILayout.EndHorizontal();
        return index;
    }

    void DrawRoom()
    {
        if (currentLobby == null)
        {
            GUILayout.Label(status, label);
            return;
        }

        GUILayout.Label($"Room : {currentLobby.Name}", label);
        GUILayout.Label($"Joueurs ({currentLobby.Players.Count}/{currentLobby.MaxPlayers}) :", label);
        foreach (Player p in currentLobby.Players)
        {
            string name = p.Data != null && p.Data.TryGetValue(NameKey, out PlayerDataObject d) ? d.Value : "?";
            if (p.Id == currentLobby.HostId) name += "  (hôte)";
            GUILayout.Label("  • " + name, label);
        }

        GUILayout.Space(16f);
        if (isHost)
        {
            if (GUILayout.Button("Lancer la partie", button, GUILayout.Height(40f))) _ = StartGame();
        }
        else
        {
            GUILayout.Label("En attente du lancement par l'hôte...", label);
        }

        if (GUILayout.Button("Quitter la room", button, GUILayout.Height(32f))) _ = LeaveRoom();
    }
}
