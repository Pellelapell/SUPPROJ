using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// HUD en jeu, dans le style du menu : room et nombre de joueurs (haut gauche), commandes (bas),
// pseudos au-dessus des têtes, liste des joueurs (TAB maintenu) et menu pause (Échap).
// Assis à un bureau : la caméra zoome sur l'écran du PC, qui s'allume et affiche le partage de l'organisateur
// (MonitorView). Chaise sans écran (bureau du prof...) : le partage s'affiche en grand dans l'HUD.
// Admin : état de son partage (P écran, C caméra) avec un aperçu.
public class GameHud : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }

    [Tooltip("Au-delà de cette distance, le pseudo n'est plus affiché")]
    public float nameTagDistance = 35f;

    Text roomText, countText, promptText;
    Image prompt;
    GameObject promptKey;
    GameObject pauseMenu, playerList;
    Text playerListTitle;
    RectTransform playerListGrid, tagsRoot;

    // Vue assise
    GameObject classView, classEmpty;
    Text classTitle;
    RawImage classMain, classInset;
    AspectRatioFitter classMainAspect, classInsetAspect;

    // Partage de l'admin
    GameObject shareStatus;
    Text shareText;
    RawImage sharePreview;
    AspectRatioFitter sharePreviewAspect;

    readonly Dictionary<NetworkPlayer, RectTransform> tags = new Dictionary<NetworkPlayer, RectTransform>();
    readonly Dictionary<NetworkPlayer, string> announced = new Dictionary<NetworkPlayer, string>();
    NetworkPlayer[] players = new NetworkPlayer[0];
    float nextRefresh;
    bool firstRefresh = true;

    NetworkManager Network => NetworkManager.Singleton;

    void Start()
    {
        MenuOpen = false;
        MenuStyle.EnsureEventSystem();

        // Pseudos dans un canvas à part, sous le reste du HUD
        tagsRoot = (RectTransform)MenuStyle.OverlayCanvas("NameTags", 0);
        Transform canvas = MenuStyle.OverlayCanvas("HudCanvas", 5);

        BuildRoomBadge(canvas);
        BuildHints(canvas);
        BuildPlayerList(canvas);
        BuildClassView(MenuStyle.OverlayCanvas("ClassCanvas", 3));
        if (AppRole.IsAdmin) BuildShareStatus(canvas);
        BuildPauseMenu(MenuStyle.OverlayCanvas("PauseCanvas", 20));
    }

    void OnDestroy() => MenuOpen = false;

    void Update()
    {
        // Échap : ouvre / ferme la pause (sauf si Échap vient d'annuler la saisie du chat)
        if (Input.GetKeyDown(KeyCode.Escape) && !GameChat.IsTyping && GameChat.ClosedFrame != Time.frameCount)
            SetPause(!MenuOpen);

        playerList.SetActive(Input.GetKey(KeyCode.Tab) && !MenuOpen && !GameChat.IsTyping);

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.5f;
            RefreshPlayers();
        }
    }

    void LateUpdate()
    {
        UpdateNameTags();
        UpdatePrompt();
        UpdateClassView();
        UpdateShareStatus();
    }

    // Ce que fait F ici (mêmes règles que NetworkPlayer) : porte pour l'admin, sinon chaise libre.
    // Élève devant une porte fermée : elle est verrouillée.
    void UpdatePrompt()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        string text = null;
        bool key = true;
        if (local != null && !MenuOpen && local.SeatIndex.Value >= 0)
        {
            text = "Se lever";
        }
        else if (local != null && !MenuOpen)
        {
            SyncedDoor door = local.NearbyDoor();
            SyncedDoor lockedDoor = SyncedDoor.Nearest(local.transform.position, local.doorReach);
            if (door != null)
                text = $"{(door.IsOpen ? "Fermer" : "Ouvrir")} la porte  ·  {door.label}";
            else if (local.NearbySeat() != null)
                text = "S'asseoir";
            else if (lockedDoor != null && !lockedDoor.IsOpen)
            {
                text = $"{lockedDoor.label}  ·  porte fermée, seul l'organisateur peut l'ouvrir";
                key = false;
            }
        }

        prompt.gameObject.SetActive(text != null);
        if (text == null) return;
        promptKey.SetActive(key);
        promptText.text = text;
    }

    // ---------- Room ----------

    void BuildRoomBadge(Transform canvas)
    {
        Image badge = MenuStyle.Box("Room", canvas, new Color(0.02f, 0.03f, 0.08f, 0.7f));
        MenuStyle.Place(badge, new Vector2(0f, 1f), new Vector2(30f, -30f), Vector2.zero);
        MenuStyle.Row(badge, 14f, new RectOffset(22, 22, 14, 14)).childForceExpandHeight = false;
        MenuStyle.FitContent(badge);

        MenuStyle.Size(MenuStyle.Box("Dot", badge.transform, MenuStyle.Secondary, MenuStyle.RoundedSmall), 14f, 14f);
        roomText = MenuStyle.Label("Name", badge.transform, (NetworkGameManager.RoomName ?? "Room").ToUpperInvariant(), 26, MenuStyle.Text,
            TextAnchor.MiddleLeft, FontStyle.Bold);
        countText = MenuStyle.Label("Count", badge.transform, "", 22, MenuStyle.TextDim);

        if (Network != null && Network.IsHost)
        {
            Image host = MenuStyle.Box("Host", badge.transform, MenuStyle.Accent, MenuStyle.RoundedSmall);
            MenuStyle.Row(host, 0f, new RectOffset(12, 12, 4, 4));
            MenuStyle.Label("Text", host.transform, "HÔTE", 16, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        }
    }

    // ---------- Commandes ----------

    void BuildHints(Transform canvas)
    {
        RectTransform bottom = MenuStyle.Rect("Controls", canvas);
        MenuStyle.Place(bottom, new Vector2(0.5f, 0f), new Vector2(0f, 30f), Vector2.zero);
        MenuStyle.Row(bottom, 10f).childForceExpandHeight = false;
        MenuStyle.FitContent(bottom);
        MenuStyle.KeyHint(bottom, "ZQSD", "Marcher   ");
        MenuStyle.KeyHint(bottom, "MAJ", "Courir   ");
        MenuStyle.KeyHint(bottom, "E", "Saluer   ");
        MenuStyle.KeyHint(bottom, "MOLETTE", "Vue 1re pers. / dessus   ");
        MenuStyle.KeyHint(bottom, "F", AppRole.IsAdmin ? "Porte / S'asseoir   " : "S'asseoir   ");
        if (AppRole.IsAdmin)
        {
            MenuStyle.KeyHint(bottom, "P", "Écran   ");
            MenuStyle.KeyHint(bottom, "C", "Caméra   ");
        }
        MenuStyle.KeyHint(bottom, "ENTRÉE", "Chat");

        // Message au-dessus des commandes : action de F (porte, chaise)
        prompt = MenuStyle.Box("Prompt", canvas, new Color(0.02f, 0.03f, 0.08f, 0.8f));
        MenuStyle.Place(prompt, new Vector2(0.5f, 0f), new Vector2(0f, 100f), Vector2.zero);
        MenuStyle.Row(prompt, 12f, new RectOffset(20, 22, 10, 10)).childForceExpandHeight = false;
        MenuStyle.FitContent(prompt);
        promptKey = MenuStyle.Box("Key", prompt.transform, MenuStyle.Accent, MenuStyle.RoundedSmall).gameObject;
        MenuStyle.Row(promptKey.GetComponent<Image>(), 0f, new RectOffset(12, 12, 4, 4));
        MenuStyle.Label("Text", promptKey.transform, "F", 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        promptText = MenuStyle.Label("Text", prompt.transform, "", 22, MenuStyle.Text);
        prompt.gameObject.SetActive(false);

        RectTransform top = MenuStyle.Rect("Menus", canvas);
        MenuStyle.Place(top, new Vector2(1f, 1f), new Vector2(-30f, -36f), Vector2.zero);
        MenuStyle.Row(top, 10f).childForceExpandHeight = false;
        MenuStyle.FitContent(top);
        MenuStyle.KeyHint(top, "TAB", "Joueurs   ");
        MenuStyle.KeyHint(top, "ÉCHAP", "Menu");
    }

    // ---------- Joueurs ----------

    void BuildPlayerList(Transform canvas)
    {
        Image panel = MenuStyle.Box("PlayerList", canvas, new Color(0.02f, 0.03f, 0.08f, 0.88f));
        MenuStyle.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 0f));
        MenuStyle.Column(panel, 16f, new RectOffset(36, 36, 28, 32));
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        playerListTitle = MenuStyle.Label("Title", panel.transform, "", 26, MenuStyle.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
        MenuStyle.Size(MenuStyle.Box("Bar", panel.transform, MenuStyle.Accent, MenuStyle.RoundedSmall), 4f);

        playerListGrid = MenuStyle.Rect("Grid", panel.transform);
        var grid = playerListGrid.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(336f, 36f);
        grid.spacing = new Vector2(16f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        playerList = panel.gameObject;
        playerList.SetActive(false);
    }

    void RefreshPlayers()
    {
        players = FindObjectsOfType<NetworkPlayer>();
        countText.text = $"{players.Length} / {NetworkGameManager.MaxPlayers} joueurs";

        // Arrivées et départs annoncés dans le chat (pas au premier passage : ceux déjà là)
        foreach (NetworkPlayer p in players)
        {
            string name = p.PlayerName.Value.ToString();
            if (name.Length == 0 || announced.ContainsKey(p)) continue;
            announced[p] = name;
            if (!firstRefresh && !p.IsOwner && GameChat.Instance != null) GameChat.Instance.AddSystem(name + " a rejoint la room");
        }
        foreach (var gone in announced.Keys.Where(p => p == null).ToList())
        {
            if (GameChat.Instance != null) GameChat.Instance.AddSystem(announced[gone] + " a quitté la room");
            announced.Remove(gone);
        }
        firstRefresh = false;

        if (playerList.activeSelf) RedrawPlayerList();
    }

    void RedrawPlayerList()
    {
        playerListTitle.text = $"JOUEURS DANS {(NetworkGameManager.RoomName ?? "LA ROOM").ToUpperInvariant()}   {players.Length} / {NetworkGameManager.MaxPlayers}";
        foreach (Transform child in playerListGrid) Destroy(child.gameObject);
        foreach (NetworkPlayer p in players.OrderByDescending(p => p.IsOwner).ThenBy(p => p.PlayerName.Value.ToString()))
        {
            string name = p.PlayerName.Value.ToString();
            if (p.OwnerClientId == NetworkManager.ServerClientId) name += "   (hôte)";
            MenuStyle.Label("Player", playerListGrid, "•  " + name, 22, p.IsOwner ? MenuStyle.Secondary : MenuStyle.Text);
        }
    }

    void OnGUI()
    {
        // Mise à jour immédiate de la liste à l'appui sur TAB (sinon jusqu'à 0,5 s de retard)
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab) RedrawPlayerList();
    }

    // ---------- Pseudos ----------

    void UpdateNameTags()
    {
        Camera cam = Camera.main;
        foreach (var dead in tags.Keys.Where(p => p == null).ToList())
        {
            if (tags[dead] != null) Destroy(tags[dead].gameObject);
            tags.Remove(dead);
        }
        if (cam == null) return;
        // Vue sur l'écran du bureau : pas de pseudos par-dessus
        bool focused = CameraFollow.Instance != null && CameraFollow.Instance.IsFocused;
        bool firstPerson = CameraFollow.Instance != null && CameraFollow.Instance.FirstPerson;

        float scale = ((RectTransform)tagsRoot).localScale.x;
        foreach (NetworkPlayer p in players)
        {
            if (p == null || !p.IsSpawned) continue;
            if (!tags.TryGetValue(p, out RectTransform tag)) tags[p] = tag = CreateTag(p);

            Vector3 world = p.transform.position + Vector3.up * (p.SeatIndex.Value >= 0 ? 1.65f : 2.15f);
            Vector3 screen = cam.WorldToScreenPoint(world);
            bool visible = !focused && !(firstPerson && p.IsOwner) && screen.z > 0f && screen.z < nameTagDistance;
            tag.gameObject.SetActive(visible);
            if (!visible) continue;

            tag.GetComponentInChildren<Text>().text = p.PlayerName.Value.ToString();
            tag.anchoredPosition = new Vector2(screen.x, screen.y) / scale;
            // Plus petit au loin
            float s = Mathf.Lerp(1f, 0.6f, Mathf.InverseLerp(6f, nameTagDistance, screen.z));
            tag.localScale = new Vector3(s, s, 1f);
        }
    }

    RectTransform CreateTag(NetworkPlayer p)
    {
        Image pill = MenuStyle.Box("Tag", tagsRoot, new Color(0.02f, 0.03f, 0.08f, 0.6f));
        MenuStyle.Place(pill, new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
        pill.rectTransform.pivot = new Vector2(0.5f, 0f);
        MenuStyle.Row(pill, 0f, new RectOffset(14, 14, 4, 4));
        MenuStyle.FitContent(pill);
        MenuStyle.Label("Name", pill.transform, "", 20, p.IsOwner ? MenuStyle.Secondary : MenuStyle.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
        pill.raycastTarget = false;
        return pill.rectTransform;
    }

    // ---------- Vue assise : écran de l'organisateur ----------

    void BuildClassView(Transform canvas)
    {
        Image panel = MenuStyle.Box("ClassView", canvas, new Color(0.02f, 0.03f, 0.08f, 0.92f));
        MenuStyle.Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(1320f, 840f));
        MenuStyle.Column(panel, 14f, new RectOffset(28, 28, 22, 24));
        classView = panel.gameObject;

        RectTransform header = MenuStyle.Rect("Header", panel.transform);
        MenuStyle.Row(header, 12f).childForceExpandHeight = false;
        MenuStyle.Size(MenuStyle.Box("Dot", header, new Color(0.95f, 0.25f, 0.3f), MenuStyle.RoundedSmall), 14f, 14f);
        classTitle = MenuStyle.Label("Title", header, "", 24, MenuStyle.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
        MenuStyle.Size(header, 34f);

        // Zone de l'image : écran partagé centré (proportions gardées), caméra en incrustation en bas à droite
        Image area = MenuStyle.Box("Area", panel.transform, new Color(0f, 0f, 0f, 0.6f), MenuStyle.RoundedSmall);
        area.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        area.gameObject.AddComponent<RectMask2D>();

        classMain = Picture("Main", area.transform, out classMainAspect);
        MenuStyle.Stretch(classMain.rectTransform);
        classMainAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        classInset = Picture("Camera", area.transform, out classInsetAspect);
        MenuStyle.Place(classInset, new Vector2(1f, 0f), new Vector2(-16f, 16f), new Vector2(320f, 240f));
        classInsetAspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;

        classEmpty = MenuStyle.Label("Empty", area.transform, "L'organisateur ne partage ni son écran ni sa caméra pour l'instant.",
            24, MenuStyle.TextDim, TextAnchor.MiddleCenter).gameObject;
        MenuStyle.Stretch((RectTransform)classEmpty.transform);

        classView.SetActive(false);
    }

    internal static RawImage Picture(string name, Transform parent, out AspectRatioFitter aspect)
    {
        var image = MenuStyle.Rect(name, parent).gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        aspect = image.gameObject.AddComponent<AspectRatioFitter>();
        return image;
    }

    internal static void ShowPicture(RawImage image, AspectRatioFitter aspect, Texture2D texture, bool on)
    {
        image.gameObject.SetActive(on);
        if (!on) return;
        image.texture = texture;
        aspect.aspectRatio = texture.height > 0 ? (float)texture.width / texture.height : 16f / 9f;
    }

    void UpdateClassView()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        ClassStream stream = ClassStream.Instance;
        Seat seat = local != null ? local.CurrentSeat : null;
        ScreenDisplay monitor = seat != null ? seat.monitor : null;

        // Chaise avec un écran : la caméra vient se placer face à lui, l'écran s'allume
        if (monitor != null && monitor.main != null)
        {
            MonitorView.Show(monitor);
            ScreenDisplay.SetActive(monitor);
            FocusOn(monitor.main.transform);
        }
        else
        {
            MonitorView.Hide();
            ScreenDisplay.SetActive(null);
            if (CameraFollow.Instance != null) CameraFollow.Instance.Unfocus();
        }

        bool show = seat != null && monitor == null && !MenuOpen && stream != null;
        classView.SetActive(show);
        if (!show) return;

        bool screen = stream.ScreenLive, camera = stream.CameraLive;
        classTitle.text = stream.AnyLive ? $"EN DIRECT  ·  {stream.PresenterName.ToUpperInvariant()}" : "ÉCRAN DE L'ORGANISATEUR";
        // Seulement la caméra : elle prend toute la place
        ShowPicture(classMain, classMainAspect, screen ? stream.ScreenTexture : stream.CameraTexture, screen || camera);
        ShowPicture(classInset, classInsetAspect, stream.CameraTexture, screen && camera);
        classEmpty.SetActive(!stream.AnyLive);
    }

    // Caméra face à l'écran, assez près pour qu'il remplisse la vue avec un peu de bureau autour
    static void FocusOn(Transform image)
    {
        CameraFollow follow = CameraFollow.Instance;
        Camera cam = follow != null ? follow.GetComponent<Camera>() : null;
        if (cam == null) return;
        float distance = image.lossyScale.y * 0.65f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 position = image.position - image.forward * distance + Vector3.up * 0.03f;
        follow.Focus(position, Quaternion.LookRotation(image.position - position));
    }

    // ---------- Partage de l'admin ----------

    void BuildShareStatus(Transform canvas)
    {
        Image panel = MenuStyle.Box("Share", canvas, new Color(0.02f, 0.03f, 0.08f, 0.75f));
        MenuStyle.Place(panel, new Vector2(1f, 1f), new Vector2(-30f, -90f), new Vector2(360f, 0f));
        MenuStyle.Column(panel, 10f, new RectOffset(16, 16, 12, 14));
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        shareStatus = panel.gameObject;

        shareText = MenuStyle.Label("Text", panel.transform, "", 18, MenuStyle.Text);
        shareText.horizontalOverflow = HorizontalWrapMode.Wrap;
        sharePreview = Picture("Preview", panel.transform, out sharePreviewAspect);
        MenuStyle.Size(sharePreview, 185f);
        shareStatus.SetActive(false);
    }

    void UpdateShareStatus()
    {
        ClassStream stream = ClassStream.Instance;
        if (shareStatus == null || stream == null) return;

        MediaCapture.State screen = stream.GetCaptureState(MediaSource.Screen);
        MediaCapture.State camera = stream.GetCaptureState(MediaSource.Camera);
        string error = screen == MediaCapture.State.Failed || camera == MediaCapture.State.Failed
            ? stream.CaptureError ?? "Partage refusé ou impossible."
            : null;
        bool show = screen != MediaCapture.State.Off || camera != MediaCapture.State.Off;
        shareStatus.SetActive(show && !MenuOpen);
        if (!show) return;

        shareText.text = error ?? $"<color=#F2404D>●</color>  EN DIRECT  ·  écran : {StateText(screen)}  ·  caméra : {StateText(camera)}";
        bool preview = error == null && (stream.ScreenLive || stream.CameraLive);
        ShowPicture(sharePreview, sharePreviewAspect, stream.ScreenLive ? stream.ScreenTexture : stream.CameraTexture, preview);
    }

    static string StateText(MediaCapture.State state) =>
        state == MediaCapture.State.Live ? "oui" : state == MediaCapture.State.Starting ? "autorisation…" : "non";

    // ---------- Pause ----------

    void BuildPauseMenu(Transform canvas)
    {
        Image dim = MenuStyle.Box("Pause", canvas, new Color(0.02f, 0.03f, 0.06f, 0.75f));
        dim.sprite = null;
        MenuStyle.Stretch(dim.rectTransform);
        pauseMenu = dim.gameObject;

        Image panel = MenuStyle.Box("Panel", dim.transform, new Color(0.08f, 0.1f, 0.18f, 0.96f));
        MenuStyle.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 0f));
        MenuStyle.Column(panel, 16f, new RectOffset(44, 44, 36, 40));
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        MenuStyle.Label("Title", panel.transform, "PAUSE", 54, MenuStyle.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
        MenuStyle.Label("Room", panel.transform, NetworkGameManager.RoomName ?? "", 22, MenuStyle.TextDim);
        MenuStyle.Size(MenuStyle.Rect("Space", panel.transform), 8f);

        Button resume = MenuStyle.Button("Resume", panel.transform, "REPRENDRE", 30, MenuStyle.Accent, MenuStyle.AccentHover, Color.white,
            () => SetPause(false));
        MenuStyle.Size(resume, 80f);

        bool host = Network != null && Network.IsHost;
        Button quit = MenuStyle.Button("Quit", panel.transform, host ? "FERMER LA ROOM" : "QUITTER LA PARTIE", 24,
            new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.16f), MenuStyle.Text, Quit);
        MenuStyle.Size(quit, 64f);

        if (host)
            MenuStyle.Label("Warning", panel.transform, "Tu héberges la room : la fermer renvoie tous les joueurs au menu.", 18,
                MenuStyle.TextDim);

        pauseMenu.SetActive(false);
    }

    void SetPause(bool open)
    {
        MenuOpen = open;
        pauseMenu.SetActive(open);
    }

    void Quit()
    {
        if (NetworkGameManager.Instance != null)
            NetworkGameManager.Instance.ReturnToMenu(Network != null && Network.IsHost ? null : "Tu as quitté la partie.");
    }
}
