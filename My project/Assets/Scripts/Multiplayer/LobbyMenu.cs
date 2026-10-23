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
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Menu principal : pseudo, bouton JOUER, état des rooms et personnalisation du perso (aperçu 3D à droite).
// Rooms : jusqu'à NetworkGameManager.MaxRooms rooms de MaxPlayers joueurs. Seul le build Admin (AppRole) en ouvre
// une et l'héberge ; le build Élève rejoint la room ouverte la plus remplie, même si la partie est lancée.
// L'hôte annonce sa room dans Unity Lobby avec son code Unity Relay, les autres lisent ce code et se connectent
// directement dans la scène de jeu.
public class LobbyMenu : MonoBehaviour
{
    [Header("Personnalisation")]
    [Tooltip("Mêmes persos, dans le même ordre, que NetworkGameManager.playerPrefabs (sans réseau, pour l'aperçu)")]
    public GameObject[] avatarPreviewPrefabs;
    [Tooltip("Noms affichés pour chaque perso")]
    public string[] avatarNames;
    [Tooltip("Animations de l'aperçu (idle + salut)")]
    public RuntimeAnimatorController previewController;
    public Transform previewSpot;

    [Header("Cadrage de l'aperçu")]
    [Tooltip("Position à l'écran (0-1) des pieds du perso : à droite, au-dessus du panneau de personnalisation")]
    public Vector2 previewFeetOnScreen = new Vector2(0.835f, 0.37f);
    public float previewDistance = 6.8f;
    public float autoSpinSpeed = 25f;

    static readonly int WaveId = Animator.StringToHash("Wave");

    bool servicesReady, busy;
    List<Lobby> rooms = new List<Lobby>();
    float nextRoomsRefresh;

    NetworkManager Network => NetworkManager.Singleton;
    NetworkGameManager GameManager => Network.GetComponent<NetworkGameManager>();
    UnityTransport Transport => Network.GetComponent<UnityTransport>();

    async void Start()
    {
        if (string.IsNullOrEmpty(PlayerProfile.Name)) PlayerProfile.Name = "Joueur" + UnityEngine.Random.Range(100, 1000);

        BuildUI();
        RefreshPreview(false);
        SetStatus(NetworkGameManager.MenuMessage ?? "Connexion aux services Unity…");
        NetworkGameManager.MenuMessage = null;

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

            servicesReady = true;
            if (statusText.text.StartsWith("Connexion")) SetStatus("");
            await RefreshRooms();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Impossible de se connecter aux services Unity. Vérifie ta connexion Internet " +
                      "(et que Lobby + Relay sont activés dans Unity Cloud).\n" + e.Message);
        }
    }

    void Update()
    {
        if (servicesReady && !busy && Time.unscaledTime >= nextRoomsRefresh)
            _ = RefreshRooms();

        bool hasName = PlayerProfile.Name.Trim().Length > 0;
        bool canPlay = AppRole.IsAdmin ? rooms.Count < NetworkGameManager.MaxRooms : BestRoom() != null;
        playButton.interactable = servicesReady && !busy && hasName && canPlay;
        foreach (Button b in roomButtons) b.interactable = servicesReady && !busy && hasName;
        playLabel.text = busy ? "…" : AppRole.IsAdmin ? "OUVRIR UNE ROOM" : "JOUER";

        UpdatePreview();
    }

    // ---------- Rooms ----------

    async Task RefreshRooms()
    {
        nextRoomsRefresh = Time.unscaledTime + 4f;
        try
        {
            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions { Count = 10 });
            rooms = response.Results
                .Where(l => l.Data != null && l.Data.ContainsKey(NetworkGameManager.JoinCodeKey))
                .OrderBy(l => l.Name)
                .ToList();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning(e);
        }
        RedrawRooms();
    }

    static int PlayerCount(Lobby room)
    {
        return room.Data.TryGetValue(NetworkGameManager.PlayersKey, out DataObject d) && int.TryParse(d.Value, out int n) ? n : 1;
    }

    static bool IsFull(Lobby room) => PlayerCount(room) >= NetworkGameManager.MaxPlayers;

    // La room ouverte la plus remplie (les joueurs se regroupent), sinon null
    Lobby BestRoom() => rooms.Where(r => !IsFull(r)).OrderByDescending(PlayerCount).FirstOrDefault();

    void OnPlayClicked() => _ = AppRole.IsAdmin ? OpenRoom() : QuickPlay();

    // Élève : rejoint la room ouverte la plus remplie
    async Task QuickPlay()
    {
        busy = true;
        SetStatus("Recherche d'une room…");
        await RefreshRooms();

        Lobby room = BestRoom();
        if (room != null)
            await JoinRoom(room);
        else
        {
            SetStatus(rooms.Count == 0
                ? "Aucune room ouverte pour l'instant : attends que l'organisateur lance la session."
                : "Toutes les rooms sont pleines, réessaie dans un instant.");
            busy = false;
        }
    }

    // Admin : ouvre une nouvelle room (dans la limite de MaxRooms) et l'héberge sur ce PC
    async Task OpenRoom()
    {
        busy = true;
        SetStatus("Vérification des rooms…");
        await RefreshRooms();

        if (rooms.Count < NetworkGameManager.MaxRooms)
            await HostRoom(FreeRoomName());
        else
        {
            SetStatus($"Il y a déjà {NetworkGameManager.MaxRooms} rooms ouvertes.");
            busy = false;
        }
    }

    string FreeRoomName()
    {
        for (int i = 1; i <= NetworkGameManager.MaxRooms; i++)
            if (rooms.All(r => r.Name != "Room " + i)) return "Room " + i;
        return "Room " + (rooms.Count + 1);
    }

    // Personne dans la room : on la crée et on l'héberge
    async Task HostRoom(string roomName)
    {
        busy = true;
        SetStatus("Ouverture de " + roomName + "…");
        try
        {
            // 1. Serveur relais pour les autres joueurs
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(NetworkGameManager.MaxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            RelayServerEndpoint endpoint = GetEndpoint(allocation.ServerEndpoints);
            Transport.SetRelayServerData(endpoint.Host, (ushort)endpoint.Port,
                allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData, null, endpoint.Secure);

            // 2. On démarre en hôte
            GameManager.PrepareConnection();
            if (!Network.StartHost()) throw new Exception("Le démarrage de l'hôte a échoué.");

            // 3. Room annoncée dans Lobby avec le code Relay : visible depuis le menu de tous les joueurs
            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { NetworkGameManager.JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) },
                    { NetworkGameManager.PlayersKey, new DataObject(DataObject.VisibilityOptions.Public, "1") },
                },
            };
            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(roomName, NetworkGameManager.MaxPlayers, options);

            // 4. Direction la scène de jeu
            GameManager.StartGame(lobby.Id, roomName);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Impossible d'ouvrir la room : " + e.Message);
            if (Network.IsListening) Network.Shutdown();
            busy = false;
        }
    }

    async Task JoinRoom(Lobby room)
    {
        busy = true;
        SetStatus("Connexion à " + room.Name + "…");
        try
        {
            string joinCode = room.Data[NetworkGameManager.JoinCodeKey].Value;
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            RelayServerEndpoint endpoint = GetEndpoint(allocation.ServerEndpoints);
            Transport.SetRelayServerData(endpoint.Host, (ushort)endpoint.Port,
                allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData, allocation.HostConnectionData, endpoint.Secure);

            // Netcode charge la scène de jeu de l'hôte automatiquement après la connexion.
            // Si la room est pleine, l'hôte refuse et NetworkGameManager ramène au menu avec le message.
            NetworkGameManager.RoomName = room.Name;
            GameManager.PrepareConnection();
            if (!Network.StartClient()) throw new Exception("La connexion au jeu a échoué.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Impossible de rejoindre " + room.Name + " (l'hôte vient peut-être de partir).");
            busy = false;
            await RefreshRooms();
        }
    }

    static RelayServerEndpoint GetEndpoint(List<RelayServerEndpoint> endpoints)
    {
        return endpoints.FirstOrDefault(e => e.ConnectionType == "dtls") ?? endpoints.First();
    }

    // ---------- Aperçu du perso ----------

    GameObject preview;
    Animator previewAnimator;
    float lastDrag = -10f;

    void RefreshPreview(bool wave)
    {
        if (preview != null) Destroy(preview);
        if (avatarPreviewPrefabs == null || avatarPreviewPrefabs.Length == 0 || previewSpot == null) return;

        preview = Instantiate(avatarPreviewPrefabs[ModelIndex], previewSpot.position, previewSpot.rotation, previewSpot);
        // Le script de démo City People change les tenues tout seul : on le retire
        foreach (var c in preview.GetComponentsInChildren<CityPeople.CityPeople>())
            DestroyImmediate(c);

        previewAnimator = preview.GetComponentInChildren<Animator>();
        if (previewAnimator != null && previewController != null)
        {
            previewAnimator.runtimeAnimatorController = previewController;
            previewAnimator.applyRootMotion = false;
            if (wave) previewAnimator.SetTrigger(WaveId);
        }
        ApplyPalette();
        modelText.text = ModelName(ModelIndex);
    }

    void ApplyPalette()
    {
        if (preview != null && NetworkGameManager.Instance != null)
            PlayerProfile.ApplyPalette(preview, NetworkGameManager.Instance.GetPalette(PaletteIndex));
        paletteText.text = PaletteCount > 0 ? $"Tenue {PaletteIndex + 1} / {PaletteCount}" : "—";
    }

    int ModelCount => avatarPreviewPrefabs != null ? avatarPreviewPrefabs.Length : 0;
    int PaletteCount => NetworkGameManager.Instance != null && NetworkGameManager.Instance.palettes != null
        ? NetworkGameManager.Instance.palettes.Length : 0;
    int ModelIndex => Mathf.Clamp(PlayerProfile.Model, 0, Mathf.Max(0, ModelCount - 1));
    int PaletteIndex => Mathf.Clamp(PlayerProfile.Palette, 0, Mathf.Max(0, PaletteCount - 1));

    string ModelName(int i)
    {
        if (avatarNames != null && i < avatarNames.Length && !string.IsNullOrEmpty(avatarNames[i])) return avatarNames[i];
        return ModelCount > 0 ? avatarPreviewPrefabs[i].name.Replace('_', ' ') : "—";
    }

    void ChangeModel(int step)
    {
        if (ModelCount == 0) return;
        PlayerProfile.Model = (ModelIndex + step + ModelCount) % ModelCount;
        RefreshPreview(true);
    }

    void ChangePalette(int step)
    {
        if (PaletteCount == 0) return;
        PlayerProfile.Palette = (PaletteIndex + step + PaletteCount) % PaletteCount;
        ApplyPalette();
    }

    void UpdatePreview()
    {
        if (previewSpot == null) return;

        // Tourne tout seul, sauf juste après que le joueur l'a fait tourner à la souris
        if (Time.unscaledTime - lastDrag > 1.5f)
            previewSpot.Rotate(0f, autoSpinSpeed * Time.deltaTime, 0f);

        // Caméra placée pour que le perso tombe à droite, au-dessus du panneau, quel que soit le format d'écran
        Camera cam = Camera.main;
        if (cam == null) return;
        float halfHeight = previewDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 spot = previewSpot.position;
        cam.transform.SetPositionAndRotation(new Vector3(
            spot.x - (previewFeetOnScreen.x - 0.5f) * 2f * halfHeight * cam.aspect,
            spot.y - (previewFeetOnScreen.y - 0.5f) * 2f * halfHeight,
            spot.z - previewDistance), Quaternion.identity);
    }

    // ---------- Interface ----------

    Text statusText, playLabel, playInfo, modelText, paletteText;
    Button playButton;
    RectTransform roomList;
    readonly List<Button> roomButtons = new List<Button>();

    void SetStatus(string message)
    {
        statusText.text = message;
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    void BuildUI()
    {
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        BuildBackground();

        var canvasGo = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasGo.transform;

        // Zone invisible au-dessus du panneau de droite : glisser fait tourner le perso
        Image drag = MenuStyle.Box("DragArea", root, Color.clear);
        drag.rectTransform.anchorMin = new Vector2(0.66f, 0.36f);
        drag.rectTransform.anchorMax = new Vector2(1f, 1f);
        drag.rectTransform.offsetMin = drag.rectTransform.offsetMax = Vector2.zero;
        drag.gameObject.AddComponent<DragArea>().onDrag = dx =>
        {
            previewSpot.Rotate(0f, -dx * 0.5f, 0f);
            lastDrag = Time.unscaledTime;
        };

        BuildLeftColumn(root);
        BuildCustomization(root);
    }

    // Dégradé + halo derrière le perso, dans un canvas rendu par la caméra derrière la scène 3D
    void BuildBackground()
    {
        var go = new GameObject("MenuBackground", typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Camera.main;
        canvas.planeDistance = 40f;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var gradient = MenuStyle.Rect("Gradient", go.transform).gameObject.AddComponent<RawImage>();
        gradient.texture = MenuStyle.Gradient(MenuStyle.BackgroundTop, MenuStyle.BackgroundBottom);
        gradient.raycastTarget = false;
        MenuStyle.Stretch(gradient.rectTransform);

        Glow(go.transform, previewFeetOnScreen + new Vector2(0f, 0.25f), 1100f, new Color(MenuStyle.Accent.r, MenuStyle.Accent.g, MenuStyle.Accent.b, 0.4f));
        Glow(go.transform, new Vector2(0.12f, 0.85f), 900f, new Color(MenuStyle.Secondary.r, MenuStyle.Secondary.g, MenuStyle.Secondary.b, 0.08f));
    }

    static void Glow(Transform parent, Vector2 anchor, float size, Color color)
    {
        var img = MenuStyle.Rect("Glow", parent).gameObject.AddComponent<RawImage>();
        img.texture = MenuStyle.RadialGlow;
        img.color = color;
        img.raycastTarget = false;
        img.rectTransform.anchorMin = img.rectTransform.anchorMax = anchor;
        img.rectTransform.sizeDelta = new Vector2(size, size);
    }

    void BuildLeftColumn(Transform root)
    {
        RectTransform col = MenuStyle.Rect("LeftColumn", root);
        col.anchorMin = new Vector2(0f, 0f);
        col.anchorMax = new Vector2(0f, 1f);
        col.pivot = new Vector2(0f, 0.5f);
        col.anchoredPosition = new Vector2(110f, 0f);
        col.sizeDelta = new Vector2(620f, -160f);
        var layout = MenuStyle.Column(col, 14f);
        layout.childForceExpandWidth = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        MenuStyle.Label("Title", col, "SUPINFO", 124, MenuStyle.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
        MenuStyle.Size(MenuStyle.Box("TitleBar", col, MenuStyle.Accent, MenuStyle.RoundedSmall), 8f, 140f);
        MenuStyle.Label("Subtitle", col, "WEBINAR  ·  Découvre SUPINFO en jouant !", 28, MenuStyle.Text);
        if (AppRole.IsAdmin)
            MenuStyle.Label("AdminBadge", col, "MODE ADMIN", 18, MenuStyle.Secondary, TextAnchor.MiddleLeft, FontStyle.Bold);
        Spacer(col, 46f);

        MenuStyle.Label("NameLabel", col, "PSEUDO", 18, MenuStyle.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
        InputField nameField = MenuStyle.Input("NameField", col, PlayerProfile.Name, 30, 16);
        MenuStyle.Size(nameField, 68f, -1, 1f);
        nameField.onValueChanged.AddListener(v => PlayerProfile.Name = v.Trim());
        Spacer(col, 18f);

        playButton = MenuStyle.Button("PlayButton", col, "JOUER", 46, MenuStyle.Accent, MenuStyle.AccentHover, Color.white, OnPlayClicked);
        playLabel = playButton.GetComponentInChildren<Text>();
        MenuStyle.Size(playButton, 104f, -1, 1f);
        playInfo = MenuStyle.Label("PlayInfo", col, "", 20, MenuStyle.TextDim);
        Spacer(col, 30f);

        MenuStyle.Label("RoomsLabel", col, "ROOMS", 18, MenuStyle.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
        roomList = MenuStyle.Rect("RoomList", col);
        MenuStyle.Column(roomList, 12f);
        MenuStyle.Size(roomList, -1, -1, 1f);

        statusText = MenuStyle.Label("Status", col, "", 20, MenuStyle.Secondary);
        MenuStyle.Size(statusText, -1, -1, 1f);

        Button quit = MenuStyle.Button("QuitButton", root, "QUITTER", 20, new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.14f),
            MenuStyle.TextDim, Application.Quit);
        var rt = (RectTransform)quit.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(110f, 60f);
        rt.sizeDelta = new Vector2(180f, 52f);
    }

    static void Spacer(Transform parent, float height) => MenuStyle.Size(MenuStyle.Rect("Space", parent), height);

    void RedrawRooms()
    {
        foreach (Transform child in roomList) Destroy(child.gameObject);
        roomButtons.Clear();

        Lobby best = BestRoom();
        if (AppRole.IsAdmin)
            playInfo.text = rooms.Count < NetworkGameManager.MaxRooms
                ? $"Ouvre {FreeRoomName()}, hébergée sur ce PC : garde le jeu ouvert pendant la session"
                : $"Maximum de {NetworkGameManager.MaxRooms} rooms atteint";
        else if (best != null)
            playInfo.text = $"Tu vas rejoindre {best.Name}  ·  {PlayerCount(best)}/{NetworkGameManager.MaxPlayers} joueurs";
        else if (rooms.Count == 0)
            playInfo.text = "En attente de l'ouverture d'une room par l'organisateur…";
        else
            playInfo.text = "Toutes les rooms sont pleines";

        if (rooms.Count == 0)
        {
            Image empty = MenuStyle.Box("Empty", roomList, MenuStyle.Panel);
            MenuStyle.Size(empty, 74f);
            Text t = MenuStyle.Label("Text", empty.transform, "Aucune room ouverte pour l'instant", 22, MenuStyle.TextDim, TextAnchor.MiddleCenter);
            MenuStyle.Stretch(t.rectTransform);
            return;
        }

        foreach (Lobby room in rooms)
            RoomCard(room);
    }

    void RoomCard(Lobby room)
    {
        int count = PlayerCount(room);
        bool full = count >= NetworkGameManager.MaxPlayers;

        Image card = MenuStyle.Box(room.Name, roomList, MenuStyle.Panel);
        MenuStyle.Size(card, 86f);
        MenuStyle.Row(card, 18f, new RectOffset(24, 14, 14, 18)).childForceExpandHeight = false;

        Image dot = MenuStyle.Box("Dot", card.transform, full ? MenuStyle.Accent : MenuStyle.Secondary, MenuStyle.RoundedSmall);
        MenuStyle.Size(dot, 14f, 14f);

        Text name = MenuStyle.Label("Name", card.transform, room.Name, 28, MenuStyle.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
        MenuStyle.Size(name, -1, -1, 1f);
        MenuStyle.Label("Count", card.transform, $"{count}/{NetworkGameManager.MaxPlayers}", 24, MenuStyle.TextDim, TextAnchor.MiddleRight);

        Button join = MenuStyle.Button("Join", card.transform, full ? "PLEINE" : "REJOINDRE", 18,
            new Color(1f, 1f, 1f, 0.1f), new Color(1f, 1f, 1f, 0.2f), MenuStyle.Text, () => _ = JoinRoom(room));
        MenuStyle.Size(join, 52f, 150f);
        if (!full) roomButtons.Add(join);
        else join.interactable = false;

        // Jauge de remplissage en bas de la carte
        Image bar = MenuStyle.Box("Fill", card.transform, full ? MenuStyle.Accent : MenuStyle.Secondary, MenuStyle.RoundedSmall);
        bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        bar.rectTransform.anchorMin = new Vector2(0f, 0f);
        bar.rectTransform.anchorMax = new Vector2((float)count / NetworkGameManager.MaxPlayers, 0f);
        bar.rectTransform.pivot = new Vector2(0f, 0f);
        bar.rectTransform.offsetMin = new Vector2(10f, 6f);
        bar.rectTransform.offsetMax = new Vector2(-4f, 10f);
    }

    // Panneau en bas à droite, sous l'aperçu du perso
    void BuildCustomization(Transform root)
    {
        Image panel = MenuStyle.Box("Customization", root, MenuStyle.Panel);
        var rt = panel.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-90f, 60f);
        rt.sizeDelta = new Vector2(500f, 300f);
        MenuStyle.Column(panel, 12f, new RectOffset(28, 28, 22, 26));

        Text header = MenuStyle.Label("Header", panel.transform, "TON PERSONNAGE", 20, MenuStyle.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
        MenuStyle.Size(header, 30f);
        Text hint = MenuStyle.Label("Hint", panel.transform, "Glisse sur le perso pour le faire tourner", 16, MenuStyle.TextDim);
        MenuStyle.Size(hint, 22f);

        modelText = Selector(panel.transform, "MODÈLE", ChangeModel);
        paletteText = Selector(panel.transform, "TENUE", ChangePalette);
    }

    // Ligne  "LIBELLÉ   <   valeur   >"
    Text Selector(Transform parent, string title, Action<int> change)
    {
        Text label = MenuStyle.Label(title, parent, title, 15, MenuStyle.TextDim, TextAnchor.LowerLeft, FontStyle.Bold);
        MenuStyle.Size(label, 22f);

        Image row = MenuStyle.Box(title + "Row", parent, new Color(1f, 1f, 1f, 0.05f));
        MenuStyle.Size(row, 62f);
        var layout = MenuStyle.Row(row, 8f, new RectOffset(6, 6, 6, 6));

        Button prev = MenuStyle.Button("Prev", row.transform, "<", 28, MenuStyle.Accent, MenuStyle.AccentHover, Color.white, () => change(-1));
        MenuStyle.Size(prev, -1, 54f);
        Text value = MenuStyle.Label("Value", row.transform, "", 26, MenuStyle.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
        MenuStyle.Size(value, -1, -1, 1f);
        Button next = MenuStyle.Button("Next", row.transform, ">", 28, MenuStyle.Accent, MenuStyle.AccentHover, Color.white, () => change(1));
        MenuStyle.Size(next, -1, 54f);
        return value;
    }
}
